using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft die Phasenläufe des Hintergrund-Cover-Dienstes: den kurzen Lauf im Startbild
    /// und den vollen Lauf danach.
    /// </summary>
    /// <remarks>
    /// Der Lauf im Startbild darf im Offline-Betrieb nicht nach draußen telefonieren — er
    /// hält sonst den Start auf, während der Anwender vor einem Ladebild sitzt. Und keiner
    /// der Läufe darf werfen: Er läuft ohne Aufrufer, der einen Fehler behandeln könnte.
    /// </remarks>
    public sealed class BackgroundCoverPhasesTests
    {
        [Fact]
        public async Task RunSeriesCoversOnce_WhenOffline_NeverAsksTheProvider()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "TKKG", CoverImageUrl = "https://i.example.invalid/tkkg.jpg" },
                TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();
            using BackgroundCoverService sut = Build(seriesService, downloader);

            int loaded = await sut.RunSeriesCoversOnceAsync(
                isOnlineAvailable: false, TestContext.Current.CancellationToken);

            Assert.Equal(0, loaded);
            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task RunSeriesCoversOnce_WhenOnline_FetchesTheMissingSeriesCover()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG", CoverImageUrl = "https://i.example.invalid/tkkg.jpg" };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();
            downloader.SetResponse("https://i.example.invalid/tkkg.jpg", [1, 2, 3]);
            using BackgroundCoverService sut = Build(seriesService, downloader);

            int loaded = await sut.RunSeriesCoversOnceAsync(
                isOnlineAvailable: true, TestContext.Current.CancellationToken);

            // Ohne Serien-Cover zeigt das Startbild eine Wand aus Platzhaltern.
            Assert.Equal(1, loaded);
            Assert.Equal(["https://i.example.invalid/tkkg.jpg"], downloader.RequestedUrls);
        }

        [Fact]
        public async Task RunOnce_OnAnEmptyLibrary_FinishesWithoutWork()
        {
            using BackgroundCoverService sut = Build(new FakeSeriesDataService(), new FakeCoverDownloader());

            Assert.Equal(0, await sut.RunOnceAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task CopyLocalToOnline_OnAnEmptyLibrary_CopiesNothing()
        {
            using BackgroundCoverService sut = Build(new FakeSeriesDataService(), new FakeCoverDownloader());

            Assert.Equal(0, await sut.CopyLocalToOnlineAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task EnsureLocalCoversForSeries_WithoutAMatchingSeries_LoadsNothing()
        {
            using BackgroundCoverService sut = Build(new FakeSeriesDataService(), new FakeCoverDownloader());

            Assert.Equal(0, await sut.EnsureLocalCoversForSeriesAsync(
                "Gibt es nicht", TestContext.Current.CancellationToken));
        }

        [Fact]
        public void Dispose_WithoutAStartedRun_StaysQuiet()
        {
            BackgroundCoverService sut = Build(new FakeSeriesDataService(), new FakeCoverDownloader());

            sut.Dispose();
            sut.Dispose();
        }

        [Fact]
        public async Task RunOnce_WithASeriesWithoutCover_TakesTheBestOnlineHit()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "TKKG" }, TestContext.Current.CancellationToken);

            FakeCoverSearchService search = new();
            search.SetResults(
            [
                new CoverSearchResult(
                    "https://i.example.invalid/thumb.jpg", "https://i.example.invalid/tkkg.jpg",
                    "TKKG", "Cover Art Archive"),
            ]);

            FakeCoverDownloader downloader = new();
            downloader.SetResponse("https://i.example.invalid/tkkg.jpg", [4, 5, 6]);
            using BackgroundCoverService sut = Build(seriesService, downloader, search);

            _ = await sut.RunOnceAsync(TestContext.Current.CancellationToken);

            // Ohne hinterlegte Adresse bleibt nur die Suche — sonst träge die Serie
            // dauerhaft einen Platzhalter.
            Assert.Equal("TKKG", search.LastSearchTitle);
            Assert.Equal(["https://i.example.invalid/tkkg.jpg"], downloader.RequestedUrls);
        }

        [Fact]
        public async Task RunOnce_WithARateLimiter_AsksItBeforeEachDownload()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "TKKG" }, TestContext.Current.CancellationToken);

            FakeCoverSearchService search = new();
            search.SetResults(
            [
                new CoverSearchResult(
                    "https://i.example.invalid/thumb.jpg", "https://i.example.invalid/tkkg.jpg",
                    "TKKG", "Cover Art Archive"),
            ]);

            FakeCoverDownloader downloader = new();
            downloader.SetResponse("https://i.example.invalid/tkkg.jpg", [4, 5, 6]);
            RecordingHostRateLimiter rateLimiter = new();
            using BackgroundCoverService sut = Build(seriesService, downloader, search, rateLimiter);

            _ = await sut.RunOnceAsync(TestContext.Current.CancellationToken);

            // Der Hintergrundlauf muss sich hinten anstellen — sonst nimmt er der
            // sichtbaren Oberfläche die Quote der fremden Gegenstelle weg.
            Assert.NotEmpty(rateLimiter.Waits);
        }

        [Fact]
        public async Task Start_RunsTheFirstRoundAndStopsOnDemand()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "TKKG" }, TestContext.Current.CancellationToken);

            FakeCoverSearchService search = new();
            using BackgroundCoverService sut = Build(
                seriesService, new FakeCoverDownloader(), search,
                options: new BackgroundCoverServiceOptions
                {
                    InitialDelay = TimeSpan.Zero,
                    Interval = TimeSpan.FromMinutes(30),
                });

            sut.Start();
            sut.Start();

            // Der Lauf startet einmal und arbeitet die Phasen sofort ab; ein zweiter
            // Start-Aufruf darf keinen zweiten Lauf erzeugen.
            _ = await search.FirstSearch.WaitAsync(
                TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

            await sut.StopAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task StopAsync_WithoutAStartedRun_StaysQuiet()
        {
            using BackgroundCoverService sut = Build(new FakeSeriesDataService(), new FakeCoverDownloader());

            await sut.StopAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }

        private static BackgroundCoverService Build(
            FakeSeriesDataService seriesService,
            FakeCoverDownloader downloader,
            FakeCoverSearchService? coverSearch = null,
            RecordingHostRateLimiter? rateLimiter = null,
            BackgroundCoverServiceOptions? options = null)
        {
            FakeCoverImageDataService coverImages = new();

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImages);
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => new FakeLocalCoverLoader());
            _ = services.AddScoped<ICoverCopyService>(_ => new FakeCoverCopyService());
            _ = services.AddScoped<ICoverSearchService>(_ => coverSearch ?? new FakeCoverSearchService());
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddSingleton<ICoverDownloader>(downloader);
            _ = services.AddSingleton<EchoPlay.App.Services.CoverService>();
            _ = services.AddSingleton<ICoverService>(sp => sp.GetRequiredService<EchoPlay.App.Services.CoverService>());
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddSingleton<EpisodeCoverCacheService>();

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            return new BackgroundCoverService(
                scopeFactory,
                provider.GetRequiredService<EchoPlay.App.Services.CoverService>(),
                downloader,
                new FakeSpotifyCredentialStore(),
                options ?? new BackgroundCoverServiceOptions(),
                new FakeLoggerFactory(),
                new FakeClock(),
                rateLimiter);
        }
    }
}

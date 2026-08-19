using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft die zweite Stufe des bevorzugten Cover-Ladens: Was weder in der Ablage noch
    /// im Ordner liegt, holt der Koordinator über die hinterlegte Adresse des Anbieters.
    /// </summary>
    /// <remarks>
    /// Diese Stufe entscheidet, ob eine frisch geöffnete Serie Bilder zeigt oder eine Wand
    /// aus Platzhaltern. Sie läuft nacheinander statt gleichzeitig, damit die Sperre des
    /// Anbieters nicht anschlägt.
    /// </remarks>
    public sealed class ForegroundCoverProviderPhaseTests
    {
        private static readonly Guid SeriesId = new("cccccccc-1111-2222-3333-000000000001");
        private const string CoverUrl = "https://i.example.invalid/folge-1.jpg";
        private static readonly byte[] DownloadedBytes = [0x41, 0x42, 0x43];

        [Fact]
        public async Task RequestPriorityForSeries_WhenNothingIsStored_DownloadsFromTheProviderUrl()
        {
            FakeEpisodeDataService episodes = new();
            Episode episode = new() { SeriesId = SeriesId, Title = "Folge 1", CoverImageUrl = CoverUrl };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();
            downloader.SetResponse(CoverUrl, DownloadedBytes);
            FakeCoverService coverService = new();

            ForegroundCoverCoordinator sut = BuildCoordinator(
                episodes, downloader: downloader, coverService: coverService);

            await sut.RequestPriorityForSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

            Assert.Equal([CoverUrl], downloader.RequestedUrls);
            Assert.Contains(episode.Id, coverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task RequestPriorityForSeries_WithoutProviderUrl_DownloadsNothing()
        {
            FakeEpisodeDataService episodes = new();
            await episodes.AddAsync(
                new Episode { SeriesId = SeriesId, Title = "Folge ohne Adresse" },
                TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();
            ForegroundCoverCoordinator sut = BuildCoordinator(episodes, downloader: downloader);

            await sut.RequestPriorityForSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task RequestPriorityForSeries_WhenTheDownloadFails_StoresNothing()
        {
            FakeEpisodeDataService episodes = new();
            await episodes.AddAsync(
                new Episode { SeriesId = SeriesId, Title = "Folge 1", CoverImageUrl = CoverUrl },
                TestContext.Current.CancellationToken);

            // Ohne hinterlegte Antwort meldet der Nachbau „Download fehlgeschlagen".
            FakeCoverDownloader downloader = new();
            FakeCoverService coverService = new();
            ForegroundCoverCoordinator sut = BuildCoordinator(
                episodes, downloader: downloader, coverService: coverService);

            await sut.RequestPriorityForSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

            // Ein leeres Cover in der Ablage wäre schlimmer als keines: Der spätere Lauf
            // hielte die Folge für erledigt und versuchte es nie wieder.
            Assert.Empty(coverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task RequestPriorityForSeries_WhenEveryCoverIsAlreadyStored_SkipsTheDownload()
        {
            FakeEpisodeDataService episodes = new();
            Episode episode = new() { SeriesId = SeriesId, Title = "Folge 1", CoverImageUrl = CoverUrl };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);

            FakeCoverImageDataService coverImages = new();
            await coverImages.SetCoverAsync(
                CoverEntityTypes.Episode, episode.Id, DownloadedBytes,
                cancellationToken: TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();
            ForegroundCoverCoordinator sut = BuildCoordinator(
                episodes, coverImages: coverImages, downloader: downloader);

            await sut.RequestPriorityForSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task RequestPriorityForSeries_WithoutEpisodes_DownloadsNothing()
        {
            FakeCoverDownloader downloader = new();
            ForegroundCoverCoordinator sut = BuildCoordinator(
                new FakeEpisodeDataService(), downloader: downloader);

            await sut.RequestPriorityForSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task RequestPriorityForSeries_WithAnAlreadyCancelledToken_EndsWithoutThrowing()
        {
            FakeEpisodeDataService episodes = new();
            await episodes.AddAsync(
                new Episode { SeriesId = SeriesId, Title = "Folge 1", CoverImageUrl = CoverUrl },
                TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();
            ForegroundCoverCoordinator sut = BuildCoordinator(episodes, downloader: downloader);

            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();

            // Der Anwender hat die Detailseite verlassen. Der Abbruch ist erwartet und darf
            // nicht als Fehler beim Aufrufer landen.
            await sut.RequestPriorityForSeriesAsync(SeriesId, cancelled.Token);

            Assert.Empty(downloader.RequestedUrls);
        }

        private static ForegroundCoverCoordinator BuildCoordinator(
            FakeEpisodeDataService episodeService,
            FakeCoverImageDataService? coverImages = null,
            FakeCoverService? coverService = null,
            FakeCoverDownloader? downloader = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => new ConfigurableLocalCoverLoader());
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImages ?? new FakeCoverImageDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            return new ForegroundCoverCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(),
                coverService ?? new FakeCoverService(),
                downloader ?? new FakeCoverDownloader(),
                rateLimiter: null,
                new FakeLogger());
        }
    }
}

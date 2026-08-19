using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Matching;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft den Cover-Abgleich am Ende des Einlesens: Serien mit lokalem Ordner bekommen
    /// Cover aus der Ablage übernommen.
    /// </summary>
    /// <remarks>
    /// Der Schritt ist bewusst gutmütig gebaut — er darf das Einlesen nie scheitern lassen.
    /// Genau deshalb braucht er Tests: Ohne sie sieht ein Abgleich, der gar nicht läuft,
    /// von außen aus wie einer, der nichts zu tun fand.
    /// <para>
    /// Eigene Datei statt Ergänzung von <c>SyncServiceTests</c>: Die Datei liegt bereits
    /// deutlich über der Grenze aus <c>testing.md</c>.
    /// </para>
    /// </remarks>
    public sealed class SyncServiceCoverSyncTests
    {
        [Fact]
        public async Task Abgleich_FragtJedeSerieMitLokalemOrdner()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "TKKG", LocalFolderPath = @"D:\Media\TKKG" },
                TestContext.Current.CancellationToken);
            await seriesService.AddAsync(
                new Series { Title = "Nur online", SpotifyArtistId = "sp_1" },
                TestContext.Current.CancellationToken);

            FakeCoverCopyService coverCopy = new();
            SyncService service = BuildService(seriesService, coverCopy);

            _ = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            // Nur die Serie mit Ordner wird gefragt — für die andere gibt es nichts zu kopieren.
            Assert.Equal(1, coverCopy.CallCount);
        }

        [Fact]
        public async Task Abgleich_OhneSerienMitOrdnerFragtNiemanden()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "Nur online", SpotifyArtistId = "sp_1" },
                TestContext.Current.CancellationToken);

            FakeCoverCopyService coverCopy = new();
            SyncService service = BuildService(seriesService, coverCopy);

            _ = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, coverCopy.CallCount);
        }

        [Fact]
        public async Task Abgleich_LaeuftAuchOhneFolgenDurch()
        {
            // Eine Serie ohne Folgen hat nichts, was in einen Ordner geschrieben werden
            // könnte. Der Lauf muss trotzdem sauber zu Ende gehen.
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "TKKG", LocalFolderPath = @"D:\Media\TKKG" },
                TestContext.Current.CancellationToken);

            FakeCoverCopyService coverCopy = new();
            SyncService service = BuildService(seriesService, coverCopy);

            SyncResult result = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, result.EpisodesUpdated);
            Assert.Equal(1, coverCopy.CallCount);
        }

        private static SyncService BuildService(
            FakeSeriesDataService seriesService,
            FakeCoverCopyService coverCopy)
        {
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled = true,
                LocalLibraryRootPath = @"D:\Media"
            });

            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => settings);
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalLibraryScanner>(_ => new FakeLocalLibraryScanner([]));
            _ = services.AddScoped<IScanOrchestrator>(sp => new FakeScanOrchestrator(new FakeLocalLibraryScanner([])));
            _ = services.AddScoped<ILocalCoverService>(_ => new FakeLocalCoverService());
            _ = services.AddScoped<ITrackMatcher>(_ => new FakeTrackMatcher());
            _ = services.AddScoped<IAudioMetadataReader>(_ => new FakeAudioMetadataReader());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddScoped<ICoverCopyService>(_ => coverCopy);
            _ = services.AddSingleton<ILoggerFactory>(new FakeLoggerFactory());

            ServiceProvider provider = services.BuildServiceProvider();

            EchoPlay.App.Services.CoverService coverService = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<ILoggerFactory>());

            return new SyncService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<ILoggerFactory>(),
                new FakeScanEventService(),
                coverService);
        }
    }
}

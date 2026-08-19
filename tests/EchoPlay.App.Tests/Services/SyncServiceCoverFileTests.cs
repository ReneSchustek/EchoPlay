using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Matching;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft den zweiten Teil des Cover-Abgleichs nach dem Einlesen: Was in der Ablage
    /// liegt, wird als <c>cover.jpg</c> in den Folgenordner geschrieben.
    /// </summary>
    /// <remarks>
    /// Damit sieht jeder Dateimanager und jeder andere Abspieler dasselbe Bild wie EchoPlay.
    /// Eine bereits vorhandene Datei bleibt unangetastet — sie kann von Hand gepflegt sein.
    ///
    /// Die Tests arbeiten auf echten Ordnern im Temp-Verzeichnis und räumen sie wieder ab;
    /// der geprüfte Schritt schreibt eine Datei und ist anders nicht festzuhalten.
    /// </remarks>
    public sealed class SyncServiceCoverFileTests
    {
        private static readonly byte[] CoverBytes = [0x51, 0x52, 0x53];

        [Fact]
        public async Task Sync_WithStoredCoverAndLocalFolder_WritesCoverJpgIntoTheEpisodeFolder()
        {
            string root = CreateTempFolder();
            try
            {
                Fixture fixture = await BuildFixtureAsync(root, withStoredCover: true, withLocalFolder: true);

                _ = await fixture.Service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

                string coverPath = Path.Combine(root, EchoPlay.Core.CoverConstants.CoverFileName);
                Assert.True(File.Exists(coverPath));
                Assert.Equal(CoverBytes, await File.ReadAllBytesAsync(coverPath, TestContext.Current.CancellationToken));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task Sync_WhenTheCoverFileAlreadyExists_LeavesItUntouched()
        {
            string root = CreateTempFolder();
            try
            {
                string coverPath = Path.Combine(root, EchoPlay.Core.CoverConstants.CoverFileName);
                byte[] handmade = [0x99, 0x98];
                await File.WriteAllBytesAsync(coverPath, handmade, TestContext.Current.CancellationToken);

                Fixture fixture = await BuildFixtureAsync(root, withStoredCover: true, withLocalFolder: true);

                _ = await fixture.Service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

                // Eine von Hand gepflegte Datei ist mehr wert als das, was der Anbieter liefert.
                Assert.Equal(handmade, await File.ReadAllBytesAsync(coverPath, TestContext.Current.CancellationToken));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task Sync_WithoutStoredCover_WritesNoFile()
        {
            string root = CreateTempFolder();
            try
            {
                Fixture fixture = await BuildFixtureAsync(root, withStoredCover: false, withLocalFolder: true);

                _ = await fixture.Service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

                Assert.Empty(Directory.GetFiles(root));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task Sync_WithEpisodeWithoutLocalFolder_WritesNoFile()
        {
            string root = CreateTempFolder();
            try
            {
                Fixture fixture = await BuildFixtureAsync(root, withStoredCover: true, withLocalFolder: false);

                _ = await fixture.Service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

                Assert.Empty(Directory.GetFiles(root));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private sealed class Fixture
        {
            public required SyncService Service { get; init; }
        }

        private static async Task<Fixture> BuildFixtureAsync(
            string seriesRoot, bool withStoredCover, bool withLocalFolder)
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG", LocalFolderPath = seriesRoot };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            Episode episode = new()
            {
                SeriesId = series.Id,
                Title = "Folge 1",
                LocalFolderPath = withLocalFolder ? seriesRoot : null,
            };
            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);

            FakeCoverImageDataService coverImages = new();
            if (withStoredCover)
            {
                await coverImages.SetCoverAsync(
                    CoverEntityTypes.Episode, episode.Id, CoverBytes,
                    cancellationToken: TestContext.Current.CancellationToken);
            }

            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled = true,
                LocalLibraryRootPath = seriesRoot,
            });

            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => settings);
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalLibraryScanner>(_ => new FakeLocalLibraryScanner([]));
            _ = services.AddScoped<IScanOrchestrator>(_ => new FakeScanOrchestrator(new FakeLocalLibraryScanner([])));
            _ = services.AddScoped<ILocalCoverService>(_ => new FakeLocalCoverService());
            _ = services.AddScoped<ITrackMatcher>(_ => new FakeTrackMatcher());
            _ = services.AddScoped<IAudioMetadataReader>(_ => new FakeAudioMetadataReader());
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImages);
            _ = services.AddScoped<ICoverCopyService>(_ => new FakeCoverCopyService());
            _ = services.AddSingleton<ILoggerFactory>(new FakeLoggerFactory());

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            EchoPlay.App.Services.CoverService coverService = new(
                scopeFactory, provider.GetRequiredService<ILoggerFactory>());

            return new Fixture
            {
                Service = new SyncService(
                    scopeFactory,
                    provider.GetRequiredService<ILoggerFactory>(),
                    new FakeScanEventService(),
                    coverService),
            };
        }

        private static string CreateTempFolder()
        {
            string path = Path.Combine(
                Path.GetTempPath(), $"echoplay-syncdir-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            return path;
        }
    }
}

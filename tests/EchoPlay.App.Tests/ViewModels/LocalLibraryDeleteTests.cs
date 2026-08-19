using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft das endgültige Löschen einer Serie von der Festplatte und das Umschalten der
    /// Überwachung in der lokalen Mediathek.
    /// </summary>
    /// <remarks>
    /// Das Löschen von der Platte ist der einzige Weg in EchoPlay, der Dateien des Anwenders
    /// unwiderruflich entfernt. Ohne die Rückfrage davor wäre ein Fehlklick nicht mehr
    /// gutzumachen — deshalb steht sie hier als Zusage und nicht als Absprache.
    ///
    /// Die Tests legen echte Ordner im Temp-Verzeichnis an.
    /// </remarks>
    public sealed class LocalLibraryDeleteTests
    {
        [Fact]
        public async Task DeleteFromDisk_WhenDeclined_KeepsSeriesAndFolder()
        {
            string folder = CreateTempFolder();
            try
            {
                FakeSeriesDataService seriesService = new();
                Series series = new() { Title = "TKKG", LocalFolderPath = folder };
                await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

                LocalLibraryViewModel sut = BuildViewModel(seriesService, confirm: false);

                await sut.Actions.DeleteSeriesFromDiskAsync(series.Id, folder);

                Assert.True(Directory.Exists(folder));
                Assert.NotNull(await seriesService.GetByIdAsync(series.Id, TestContext.Current.CancellationToken));
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }

        [Fact]
        public async Task DeleteFromDisk_WhenConfirmed_RemovesSeriesAndFolder()
        {
            string folder = CreateTempFolder();
            try
            {
                FakeSeriesDataService seriesService = new();
                Series series = new() { Title = "TKKG", LocalFolderPath = folder };
                await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

                LocalLibraryViewModel sut = BuildViewModel(seriesService, confirm: true);

                await sut.Actions.DeleteSeriesFromDiskAsync(series.Id, folder);

                Assert.False(Directory.Exists(folder));
                Assert.Null(await seriesService.GetByIdAsync(series.Id, TestContext.Current.CancellationToken));
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }

        [Fact]
        public async Task DeleteFromDisk_WithoutFolder_StillRemovesTheSeries()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Nur online" };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            LocalLibraryViewModel sut = BuildViewModel(seriesService, confirm: true);

            await sut.Actions.DeleteSeriesFromDiskAsync(series.Id, folderPath: null);

            Assert.Null(await seriesService.GetByIdAsync(series.Id, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ToggleWatch_WithoutWatchService_ChangesNothing()
        {
            LocalLibraryViewModel sut = BuildViewModel(new FakeSeriesDataService(), confirm: true);

            await sut.Actions.ToggleWatchAsync(Guid.Empty, watch: true);
        }

        [Fact]
        public async Task ToggleWatch_WithWatchService_ReachesTheService()
        {
            FakeWatchToggleService watchToggle = new();
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG", LocalFolderPath = @"D:\Media\TKKG" };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            LocalLibraryViewModel sut = BuildViewModel(seriesService, confirm: true, watchToggle);

            await sut.Actions.ToggleWatchAsync(series.Id, watch: true);

            Assert.Equal([(series.Id, true)], watchToggle.Calls);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static string CreateTempFolder()
        {
            string path = Path.Combine(
                Path.GetTempPath(), $"echoplay-delete-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "track01.mp3"), []);
            return path;
        }

        private static LocalLibraryViewModel BuildViewModel(
            FakeSeriesDataService seriesService,
            bool confirm,
            FakeWatchToggleService? watchToggle = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ITrackTitleResolver>(_ => new FakeTrackTitleResolver());
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            IClock clock = new FakeClock();

            StatusBarViewModel statusBar = new(
                scopeFactory, new FakeThemeService(), new TaskbarProgressService(), clock);

            LocalLibraryViewModelContext context = new(
                scopeFactory,
                new FakeSyncService(),
                new FakePlayerService(),
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(confirm),
                statusBar,
                new FakeLocalCoverLoader(),
                new FakeScanEventService(),
                new FakeCoverSearchService(),
                new FakeOnlineAccessGuard(),
                new FakeOnlineEpisodeChecker(),
                clock,
                WatchToggleService: watchToggle);

            return new LocalLibraryViewModel(context);
        }
    }
}

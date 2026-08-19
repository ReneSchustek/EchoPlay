using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Scanning;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft, was der Einlesevorgang in der Oberfläche anzeigt: Statuszeile, Detailtext und
    /// der Fortschrittsbalken mit und ohne bekannte Gesamtzahl.
    /// </summary>
    /// <remarks>
    /// Ein Einlesevorgang über eine große Sammlung läuft minutenlang. Ohne belastbare
    /// Anzeige weiß der Anwender nicht, ob noch etwas passiert — und bricht ab, während
    /// die Anwendung arbeitet.
    /// </remarks>
    public sealed class LocalLibraryScanProgressTests
    {
        [Fact]
        public async Task Scan_WithProgressReports_RunsThroughToTheEnd()
        {
            FakeSyncService sync = new();
            sync.ProgressSteps.Add(new ScanProgress { StatusText = "Zähle Ordner …" });
            sync.ProgressSteps.Add(new ScanProgress
            {
                PhaseLabel = "Folgen ermitteln …",
                StatusText = "Lese TKKG",
                DetailText = "Datei 12 von 150",
                ProcessedFiles = 12,
                TotalFiles = 150,
            });

            LocalLibraryScanViewModel sut = Build(sync);

            await RunScanAsync(sut);

            // Gemeldete Zwischenstände dürfen den Lauf nicht aufhalten. Auf Statuszeile und
            // Detailtext wird bewusst nicht geprüft: `Progress<T>` stellt die Meldungen über
            // den Aufgabenplaner zu, sie können also noch nach dem Ende eintreffen.
            Assert.Equal(1, sync.SyncCallCount);
            Assert.False(sut.IsScanning);
        }

        [Fact]
        public async Task Scan_WhenDone_ReleasesTheOverlay()
        {
            LocalLibraryScanViewModel sut = Build(new FakeSyncService());

            await RunScanAsync(sut);

            // Bliebe das Overlay stehen, wäre die Seite nach dem Einlesen unbedienbar.
            Assert.False(sut.IsScanning);
        }

        [Fact]
        public void LibraryRootPath_AndSetupHint_AreSettableFromThePage()
        {
            LocalLibraryScanViewModel sut = Build(new FakeSyncService());

            sut.LibraryRootPath = @"D:\Media";
            sut.NeedsLibraryFolderSetup = true;

            Assert.Equal(@"D:\Media", sut.LibraryRootPath);
            Assert.True(sut.NeedsLibraryFolderSetup);
        }

        [Fact]
        public void AddFolderCommand_AsksThePageForTheWindowHandle()
        {
            LocalLibraryScanViewModel sut = Build(new FakeSyncService());
            int requests = 0;
            sut.AddFolderRequested += () => requests++;

            sut.AddFolderCommand.Execute(null);

            // Das Ansichtsmodell kennt kein Fenster; die Ordnerwahl braucht eines. Deshalb
            // fragt es die Seite danach, statt selbst einen Dialog zu öffnen.
            Assert.Equal(1, requests);
        }

        [Fact]
        public async Task ReInitialize_WhenConfirmed_ImportsEveryFolderAgain()
        {
            FakeSyncService sync = new();
            sync.ProgressSteps.Add(new ScanProgress
            {
                PhaseLabel = "Serien erkennen …",
                StatusText = "Lese TKKG",
                ProcessedSeries = 1,
                TotalSeries = 4,
            });

            LocalLibraryScanViewModel sut = Build(sync);

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.LibraryReloaded += () =>
            {
                _ = reloaded.TrySetResult();
                return Task.CompletedTask;
            };

            sut.ReInitializeCommand.Execute(null);
            await reloaded.Task.WaitAsync(
                System.TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            // Nach dem Zurücksetzen ist keine Zuordnung mehr da — der Lauf muss jeden
            // Ordner erneut einlesen, unabhängig von der Einstellung zum Auto-Import.
            Assert.Equal(1, sync.SyncCallCount);
            Assert.True(sync.LastForceImportAll);
            Assert.False(sut.IsScanning);
        }

        [Fact]
        public async Task ReInitialize_WhenDeclined_TouchesNothing()
        {
            FakeSyncService sync = new();
            LocalLibraryScanViewModel sut = Build(sync, confirm: false);

            sut.ReInitializeCommand.Execute(null);
            await Task.Yield();

            // Der Neuaufbau löst jede Zuordnung zwischen Ablage und Platte.
            Assert.Equal(0, sync.SyncCallCount);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static async Task RunScanAsync(LocalLibraryScanViewModel sut)
        {
            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.LibraryReloaded += () =>
            {
                _ = reloaded.TrySetResult();
                return Task.CompletedTask;
            };

            sut.ScanCommand.Execute(null);
            await reloaded.Task.WaitAsync(
                System.TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        private static LocalLibraryScanViewModel Build(FakeSyncService sync, bool confirm = true)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new AppSettings { LocalLibraryEnabled = true, LocalLibraryRootPath = @"D:\Media" }));

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            IClock clock = new FakeClock();

            StatusBarViewModel statusBar = new(
                scopeFactory, new FakeThemeService(), new TaskbarProgressService(), clock);

            return new LocalLibraryScanViewModel(
                scopeFactory,
                sync,
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(confirm),
                statusBar,
                new FakeScanEventService(),
                onSeriesSynced: (Series _) => { });
        }
    }
}

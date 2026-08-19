using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft das Einlesen der lokalen Mediathek: Scan, Neuaufbau und den Ereignisstrom,
    /// über den während des Laufs schon Kacheln erscheinen.
    /// </summary>
    /// <remarks>
    /// Der Neuaufbau löst jede Zuordnung zwischen Datenbank und Platte — deshalb steht die
    /// Rückfrage hier unter Test. Die zweite Zusage ist unscheinbarer und wiegt schwerer:
    /// Ist die Datenbank leer, muss der Lauf alles einlesen, auch wenn in den Einstellungen
    /// das automatische Einlesen abgeschaltet ist. Ohne diese Ausnahme bliebe die Mediathek
    /// nach einem Zurücksetzen dauerhaft leer.
    /// </remarks>
    public sealed class LocalLibraryScanViewModelTests
    {
        // ── Ereignisstrom ────────────────────────────────────────────────────────

        [Fact]
        public async Task Activate_LetsSeriesAppearWhileTheScanRuns()
        {
            Harness harness = await Harness.BuildAsync();
            harness.ViewModel.Activate();

            harness.ScanEvents.RaiseSeriesSynced(new Series { Title = "Die drei Fragezeichen" });

            // Bei zweitausend Ordnern dauert ein Lauf Minuten. Erschienen die Kacheln erst
            // am Ende, sähe die Mediathek die ganze Zeit leer aus.
            Assert.Equal(["Die drei Fragezeichen"], harness.SyncedTitles);
        }

        [Fact]
        public async Task Deactivate_StopsTheStream()
        {
            Harness harness = await Harness.BuildAsync();
            harness.ViewModel.Activate();
            harness.ViewModel.Deactivate();

            harness.ScanEvents.RaiseSeriesSynced(new Series { Title = "Kommt nicht an" });

            Assert.Empty(harness.SyncedTitles);
        }

        [Fact]
        public async Task Dispose_StopsTheStream()
        {
            Harness harness = await Harness.BuildAsync();
            harness.ViewModel.Activate();

            harness.ViewModel.Dispose();
            harness.ScanEvents.RaiseSeriesSynced(new Series { Title = "Kommt nicht an" });

            // Der Ereignisdienst lebt so lange wie die Anwendung. Bliebe ein Rückruf einer
            // verlassenen Ansicht hängen, hielte er sie samt Kacheln im Speicher.
            Assert.Empty(harness.SyncedTitles);
        }

        // ── Anzeigezustand ───────────────────────────────────────────────────────

        [Fact]
        public async Task BeforeAnyScan_TheOverlayIsHidden()
        {
            Harness harness = await Harness.BuildAsync();

            Assert.True(harness.ViewModel.IsNotScanning);
            Assert.Equal(Visibility.Collapsed, harness.ViewModel.IsScanningVisibility);
            Assert.Equal(Visibility.Collapsed, harness.ViewModel.ScanDetailVisibility);
        }

        // ── Scan ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Scan_WithEmptyDatabase_ReadsEverything()
        {
            Harness harness = await Harness.BuildAsync();

            await harness.RunScanAsync();

            // Nach einem Zurücksetzen ist die Datenbank leer. Würde der Lauf hier auf die
            // Einstellung hören, bliebe die Mediathek leer und niemand fände den Grund.
            Assert.True(harness.SyncService.LastForceImportAll);
        }

        [Fact]
        public async Task Scan_WithExistingSeries_LeavesTheDecisionToTheSettings()
        {
            Harness harness = await Harness.BuildAsync(existingSeries: [new Series { Title = "Schon da" }]);

            await harness.RunScanAsync();

            Assert.False(harness.SyncService.LastForceImportAll);
        }

        [Fact]
        public async Task Scan_ClearsTheListBeforeItStarts()
        {
            Harness harness = await Harness.BuildAsync();

            await harness.RunScanAsync();

            // Alte Kacheln während des Laufs stehen zu lassen hieße, dass der Anwender
            // minutenlang auf einen Stand blickt, der gerade ersetzt wird.
            Assert.Equal(1, harness.ScanStartingCount);
        }

        [Fact]
        public async Task Scan_ReportsWhatItCreated()
        {
            Harness harness = await Harness.BuildAsync();

            await harness.RunScanAsync();

            // Die Zahlen sind der einzige Beleg, dass der Lauf etwas getan hat. „Fertig"
            // allein ließe offen, ob er den Ordner überhaupt gefunden hat.
            Assert.Contains("12", harness.ViewModel.SyncStatusText, StringComparison.Ordinal);
            Assert.Contains("3", harness.ViewModel.SyncStatusText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Scan_HidesTheProgressBarBeforeTheListReloads()
        {
            Harness harness = await Harness.BuildAsync();

            await harness.RunScanAsync();

            // Der Balken verschwindet vor dem Nachladen der Ansicht. Bliebe er stehen,
            // sähe die Ladezeit der Liste aus wie ein hängender Lauf.
            Assert.Equal(string.Empty, harness.ViewModel.ScanDetailText);
            Assert.True(harness.ViewModel.IsNotScanning);
        }

        [Fact]
        public async Task Scan_WhenItFails_ShowsAnErrorAndClearsTheOverlay()
        {
            Harness harness = await Harness.BuildAsync(
                syncFailure: new InvalidOperationException("Laufwerk nicht bereit"));

            harness.ViewModel.ScanCommand.Execute(null);
            (string Title, string Message) shown = await harness.ErrorDialog.FirstDialogShown;

            Assert.Equal("Laufwerk nicht bereit", shown.Message);
            Assert.Equal(string.Empty, harness.ViewModel.SyncStatusText);
            Assert.True(harness.ViewModel.IsNotScanning);
        }

        // ── Neuaufbau ────────────────────────────────────────────────────────────

        [Fact]
        public async Task ReInitialize_WhenUserDeclines_TouchesNothing()
        {
            Harness harness = await Harness.BuildAsync(confirmResult: false);

            harness.ViewModel.ReInitializeCommand.Execute(null);
            await Task.Yield();

            // Der Neuaufbau löst jede Zuordnung zwischen Datenbank und Platte. Wer im
            // Dialog ablehnt, behält seinen Stand — samt der Ordner, die er von Hand
            // zugewiesen hat.
            Assert.Equal(0, harness.SyncService.SyncCallCount);
            Assert.Equal(0, harness.ScanStartingCount);
        }

        [Fact]
        public async Task ReInitialize_WhenConfirmed_ReadsEverythingAgain()
        {
            Harness harness = await Harness.BuildAsync(existingSeries: [new Series { Title = "Schon da" }]);

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.ViewModel.LibraryReloaded += () =>
            {
                _ = reloaded.TrySetResult();
                return Task.CompletedTask;
            };

            harness.ViewModel.ReInitializeCommand.Execute(null);
            await reloaded.Task;

            // Nach dem Auflösen aller Zuordnungen muss alles neu eingelesen werden — auch
            // hier zählt die Einstellung nicht, sonst bliebe der Bestand ohne Dateien.
            Assert.Equal(1, harness.SyncService.SyncCallCount);
            Assert.True(harness.SyncService.LastForceImportAll);
        }

        [Fact]
        public async Task ReInitialize_WhenItFails_ShowsAnErrorAndClearsTheOverlay()
        {
            Harness harness = await Harness.BuildAsync(
                syncFailure: new InvalidOperationException("Datenbank gesperrt"));

            harness.ViewModel.ReInitializeCommand.Execute(null);
            (string Title, string Message) shown = await harness.ErrorDialog.FirstDialogShown;

            Assert.Equal("Datenbank gesperrt", shown.Message);
            Assert.True(harness.ViewModel.IsNotScanning);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Baut das Ansichtsmodell samt Umfeld und stellt einen Wartepunkt für den Lauf
        /// bereit — die Befehle selbst liefern keinen, an dem ein Test hängen könnte.
        /// </summary>
        private sealed class Harness
        {
            public LocalLibraryScanViewModel ViewModel { get; private set; } = null!;

            public required FakeSyncService SyncService { get; init; }

            public required FakeScanEventService ScanEvents { get; init; }

            public required FakeErrorDialogService ErrorDialog { get; init; }

            public List<string> SyncedTitles { get; } = [];

            public int ScanStartingCount { get; private set; }

            /// <summary>
            /// Startet den Lauf und wartet, bis die Ansicht neu geladen wird.
            /// </summary>
            public async Task RunScanAsync()
            {
                TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
                ViewModel.LibraryReloaded += () =>
                {
                    _ = reloaded.TrySetResult();
                    return Task.CompletedTask;
                };

                ViewModel.ScanCommand.Execute(null);
                await reloaded.Task;
            }

            public static async Task<Harness> BuildAsync(
                IReadOnlyList<Series>? existingSeries = null,
                bool confirmResult = true,
                Exception? syncFailure = null)
            {
                FakeSyncService syncService = new(
                    new SyncResult { TracksCreated = 12, EpisodesUpdated = 3 }, syncFailure);
                FakeScanEventService scanEvents = new();
                FakeErrorDialogService errorDialog = new();

                FakeSeriesDataService seriesService = new();
                foreach (Series entry in existingSeries ?? [])
                {
                    await seriesService.AddAsync(entry, TestContext.Current.CancellationToken);
                }

                ServiceCollection services = new();
                _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
                _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
                _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
                _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
                _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
                _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());

                ServiceProvider provider = services.BuildServiceProvider();
                IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

                StatusBarViewModel statusBar = new(
                    scopeFactory, new FakeThemeService(), new TaskbarProgressService(), new FakeClock());

                Harness harness = new()
                {
                    SyncService = syncService,
                    ScanEvents = scanEvents,
                    ErrorDialog = errorDialog,
                };

                harness.ViewModel = new LocalLibraryScanViewModel(
                    scopeFactory,
                    syncService,
                    errorDialog,
                    new FakeConfirmationDialogService(confirmResult),
                    statusBar,
                    scanEvents,
                    series => harness.SyncedTitles.Add(series.Title));

                harness.ViewModel.ScanStarting += () => harness.ScanStartingCount++;

                return harness;
            }
        }
    }
}

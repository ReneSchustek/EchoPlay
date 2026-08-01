using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="LocalLibraryScanViewModel"/> – das Sub-ViewModel, das den
    /// Bibliotheks-Scan steuert. Ein Scan läuft minutenlang und blockiert die Oberfläche
    /// halb; entsprechend wichtig sind Doppelklick-Schutz, das Zurücksetzen der Zustände
    /// im Fehlerfall und die Meldung an das übergeordnete ViewModel.
    /// </summary>
    public sealed class LocalLibraryScanViewModelTests
    {
        private static (LocalLibraryScanViewModel ViewModel, FakeSyncService Sync, FakeErrorDialogService Errors)
            Build(
                FakeSyncService? syncService = null,
                bool confirmReset = true,
                params Series[] existingSeries)
        {
            FakeSeriesDataService seriesService = new();

            foreach (Series series in existingSeries)
            {
                seriesService.AddAsync(series).GetAwaiter().GetResult();
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

            FakeSyncService sync = syncService ?? new FakeSyncService();
            FakeErrorDialogService errors = new();

            StatusBarViewModel statusBar = new(
                scopeFactory,
                new FakeThemeService(),
                new TaskbarProgressService(),
                new FakeClock());

            LocalLibraryScanViewModel viewModel = new(
                scopeFactory,
                sync,
                errors,
                new FakeConfirmationDialogService(confirmReset),
                statusBar,
                new FakeScanEventService(),
                _ => { });

            return (viewModel, sync, errors);
        }

        [Fact]
        public async Task ScanCommand_RunsSyncAndResetsScanningFlag()
        {
            (LocalLibraryScanViewModel viewModel, FakeSyncService sync, _) = Build();

            viewModel.ScanCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel,
                () => !viewModel.IsScanning && sync.SyncCallCount > 0,
                "Scan läuft durch und setzt das Lauf-Flag zurück");

            Assert.Equal(1, sync.SyncCallCount);
            Assert.False(viewModel.IsScanning);
            Assert.True(viewModel.IsNotScanning);
        }

        [Fact]
        public async Task ScanCommand_WithEmptyLibrary_ForcesFullImport()
        {
            // Nach „Bibliothek zurücksetzen" ist die Datenbank leer. Dann muss der Scan
            // alles neu importieren, egal was in den Einstellungen steht.
            (LocalLibraryScanViewModel viewModel, FakeSyncService sync, _) = Build();

            viewModel.ScanCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel, () => sync.SyncCallCount > 0, "Scan ruft den Sync-Dienst");

            Assert.True(sync.LastForceImportAll);
        }

        [Fact]
        public async Task ScanCommand_WithExistingSeries_DoesNotForceFullImport()
        {
            (LocalLibraryScanViewModel viewModel, FakeSyncService sync, _) = Build(
                syncService: null,
                confirmReset: true,
                new Series { Title = "Bereits vorhanden" });

            viewModel.ScanCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel, () => sync.SyncCallCount > 0, "Scan ruft den Sync-Dienst");

            Assert.False(sync.LastForceImportAll);
        }

        [Fact]
        public async Task ScanCommand_ReportsResultInStatusText()
        {
            FakeSyncService sync = new(new SyncResult { TracksCreated = 12, EpisodesUpdated = 3 });
            (LocalLibraryScanViewModel viewModel, _, _) = Build(sync);

            viewModel.ScanCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel,
                () => viewModel.SyncStatusText.Contains("12", StringComparison.Ordinal),
                "Statuszeile nennt die Zahl der angelegten Tracks");

            Assert.Contains("12", viewModel.SyncStatusText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ScanCommand_WhenSyncFails_ShowsDialogAndClearsState()
        {
            // Ein defekter Ordner darf die Oberfläche nicht im Scan-Zustand hängen lassen.
            FakeSyncService sync = new(exception: new InvalidOperationException("Laufwerk nicht erreichbar"));
            (LocalLibraryScanViewModel viewModel, _, FakeErrorDialogService errors) = Build(sync);

            viewModel.ScanCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel, () => !viewModel.IsScanning && errors.ShownDialogs.Count > 0, "Fehlerdialog erscheint");

            _ = Assert.Single(errors.ShownDialogs);
            Assert.False(viewModel.IsScanning);
            Assert.Equal(string.Empty, viewModel.ScanDetailText);
        }

        [Fact]
        public async Task ScanCommand_RaisesLibraryReloaded()
        {
            // Ohne dieses Signal zeigt die Mediathek nach dem Scan veraltete Zähler.
            (LocalLibraryScanViewModel viewModel, FakeSyncService sync, _) = Build();

            bool reloaded = false;
            viewModel.LibraryReloaded += () =>
            {
                reloaded = true;
                return Task.CompletedTask;
            };

            viewModel.ScanCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel, () => !viewModel.IsScanning && sync.SyncCallCount > 0, "Scan endet");

            Assert.True(reloaded);
        }

        [Fact]
        public async Task ScanCommand_RaisesScanStartingBeforeWork()
        {
            (LocalLibraryScanViewModel viewModel, FakeSyncService sync, _) = Build();

            bool starting = false;
            viewModel.ScanStarting += () => starting = true;

            viewModel.ScanCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel, () => sync.SyncCallCount > 0, "Scan startet");

            Assert.True(starting);
        }

        [Fact]
        public void IsScanning_RaisesPropertyChangedForDependentFlag()
        {
            // IsNotScanning steuert die Bedienbarkeit der Schaltflächen und muss mitlaufen.
            (LocalLibraryScanViewModel viewModel, _, _) = Build();

            List<string> changed = [];
            viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

            viewModel.ScanCommand.Execute(null);

            Assert.Contains(nameof(LocalLibraryScanViewModel.IsScanning), changed);
            Assert.Contains(nameof(LocalLibraryScanViewModel.IsNotScanning), changed);
        }

        [Fact]
        public async Task ReInitializeCommand_WhenDeclined_DoesNothing()
        {
            (LocalLibraryScanViewModel viewModel, FakeSyncService sync, _) = Build(confirmReset: false);

            viewModel.ReInitializeCommand.Execute(null);
            await Task.Yield();

            Assert.Equal(0, sync.SyncCallCount);
        }

        [Fact]
        public async Task ReInitializeCommand_WhenConfirmed_ClearsAndRescans()
        {
            (LocalLibraryScanViewModel viewModel, FakeSyncService sync, _) = Build(
                syncService: null,
                confirmReset: true,
                new Series { Title = "Nur lokal", LocalFolderPath = @"C:\Hörspiele\Test" });

            viewModel.ReInitializeCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel,
                () => !viewModel.IsScanning && sync.SyncCallCount > 0,
                "Neuinitialisierung startet den Scan");

            Assert.Equal(1, sync.SyncCallCount);
        }
    }
}

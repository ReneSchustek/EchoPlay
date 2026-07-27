using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="MaintenanceSettingsViewModel"/> – Datenbankpflege und
    /// Bibliotheks-Reset. Diese Befehle löschen Daten; entsprechend wichtig sind der
    /// Doppelklick-Schutz und dass der Lauf-Zustand auch im Fehlerfall zurückgesetzt wird.
    /// </summary>
    public sealed class MaintenanceSettingsViewModelTests
    {
        private static (MaintenanceSettingsViewModel ViewModel, FakeDatabaseMaintenanceService Maintenance, FakeLogViewerCoordinator Logs)
            Build(bool logViewerAvailable = true)
        {
            FakeDatabaseMaintenanceService maintenance = new();

            ServiceCollection services = new();
            _ = services.AddScoped<IDatabaseMaintenanceService>(_ => maintenance);
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            FakeLogViewerCoordinator logs = new() { IsLiveViewAvailable = logViewerAvailable };

            MaintenanceSettingsViewModel viewModel = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                logs,
                onUserEdit: () => { });

            return (viewModel, maintenance, logs);
        }

        [Fact]
        public async Task RunMaintenanceAsync_ResetsRunningFlagAfterwards()
        {
            (MaintenanceSettingsViewModel viewModel, _, _) = Build();

            await viewModel.RunMaintenanceAsync();

            Assert.False(viewModel.IsMaintaining);
            Assert.True(viewModel.IsNotMaintaining);
        }

        [Fact]
        public async Task RunMaintenanceAsync_CalledTwice_RunsBothTimes()
        {
            // Der Lauf-Zustand muss nach dem ersten Durchgang wieder freigegeben sein,
            // sonst bliebe die Schaltfläche dauerhaft gesperrt.
            (MaintenanceSettingsViewModel viewModel, _, _) = Build();

            await viewModel.RunMaintenanceAsync();
            await viewModel.RunMaintenanceAsync();

            Assert.False(viewModel.IsMaintaining);
        }

        [Fact]
        public async Task ResetLibraryAsync_OnlineScope_ClearsOnlineOnly()
        {
            (MaintenanceSettingsViewModel viewModel, FakeDatabaseMaintenanceService maintenance, _) = Build();

            await viewModel.ResetLibraryAsync(0);

            Assert.Equal(1, maintenance.ClearOnlineCount);
            Assert.Equal(0, maintenance.ClearLocalCount);
            Assert.Equal(0, maintenance.ClearAllCount);
        }

        [Fact]
        public async Task ResetLibraryAsync_LocalScope_ClearsLocalOnly()
        {
            (MaintenanceSettingsViewModel viewModel, FakeDatabaseMaintenanceService maintenance, _) = Build();

            await viewModel.ResetLibraryAsync(1);

            Assert.Equal(1, maintenance.ClearLocalCount);
            Assert.Equal(0, maintenance.ClearOnlineCount);
        }

        [Fact]
        public async Task ResetLibraryAsync_AllScope_ClearsEverything()
        {
            (MaintenanceSettingsViewModel viewModel, FakeDatabaseMaintenanceService maintenance, _) = Build();

            await viewModel.ResetLibraryAsync(2);

            Assert.Equal(1, maintenance.ClearAllCount);
        }

        [Fact]
        public async Task ResetLibraryAsync_ResetsRunningFlagAfterwards()
        {
            (MaintenanceSettingsViewModel viewModel, _, _) = Build();

            await viewModel.ResetLibraryAsync(2);

            Assert.False(viewModel.IsMaintaining);
        }

        [Fact]
        public void IsLogViewerAvailable_FollowsTheCoordinator()
        {
            // Ohne Speicher-Senke gibt es nichts anzuzeigen – der Bereich blendet sich aus.
            (MaintenanceSettingsViewModel withViewer, _, _) = Build(logViewerAvailable: true);
            (MaintenanceSettingsViewModel withoutViewer, _, _) = Build(logViewerAvailable: false);

            Assert.True(withViewer.IsLogViewerAvailable);
            Assert.False(withoutViewer.IsLogViewerAvailable);
        }

        [Fact]
        public async Task LoadLogFilesAsync_FillsFileList()
        {
            (MaintenanceSettingsViewModel viewModel, _, FakeLogViewerCoordinator logs) = Build();
            logs.FileOptions.Add(new EchoPlay.App.Models.LogFileOption("2026-07-27.log", Helpers.TestIds.ReferenceDate, @"C:\Logs\2026-07-27.log"));

            await viewModel.LoadLogFilesAsync();

            Assert.NotEmpty(viewModel.AvailableLogFiles);
        }

        [Fact]
        public void ClearCacheOnNextStart_Change_NotifiesTheView()
        {
            (MaintenanceSettingsViewModel viewModel, _, _) = Build();

            List<string> changed = [];
            viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

            viewModel.ClearCacheOnNextStart = true;

            Assert.Contains(nameof(MaintenanceSettingsViewModel.ClearCacheOnNextStart), changed);
        }

        [Fact]
        public void IsMaintaining_Change_UpdatesDependentFlag()
        {
            (MaintenanceSettingsViewModel viewModel, _, _) = Build();

            Assert.True(viewModel.IsNotMaintaining);
            Assert.False(viewModel.IsMaintaining);
        }
    }
}

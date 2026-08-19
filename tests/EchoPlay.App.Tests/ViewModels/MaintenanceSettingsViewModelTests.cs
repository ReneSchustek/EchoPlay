using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
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
            Build(bool logViewerAvailable = true, System.Action? onUserEdit = null)
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
                onUserEdit: onUserEdit ?? (() => { }));

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

        [Fact]
        public void LoadFrom_TakesAllMaintenanceValues()
        {
            (MaintenanceSettingsViewModel viewModel, _, _) = Build();

            viewModel.LoadFrom(new AppSettings
            {
                DbPurgeDays = 45,
                ClearCacheOnNextStart = true,
                DbBackupEnabled = true,
                DbBackupRetentionCount = 7,
            });

            Assert.Equal(45, viewModel.DbPurgeDays);
            Assert.True(viewModel.ClearCacheOnNextStart);
            Assert.True(viewModel.DbBackupEnabled);
            Assert.Equal(7, viewModel.DbBackupRetentionCount);
        }

        [Fact]
        public void LoadFrom_IsNotReportedAsUserEdit()
        {
            int edits = 0;
            (MaintenanceSettingsViewModel viewModel, _, _) = Build(onUserEdit: () => edits++);

            viewModel.LoadFrom(new AppSettings { DbPurgeDays = 45, DbBackupRetentionCount = 7 });

            // Das Laden ist keine Eingabe. Zählte es als solche, stünde der Speichern-Knopf
            // nach jedem Öffnen der Einstellungen auf „geändert".
            Assert.Equal(0, edits);
        }

        [Fact]
        public void WriteTo_NegativePurgeDays_AreClampedToZero()
        {
            (MaintenanceSettingsViewModel viewModel, _, _) = Build();
            viewModel.DbPurgeDays = -5;

            AppSettings settings = new();
            viewModel.WriteTo(settings);

            // Eine negative Aufbewahrung ergibt keinen Sinn. Null ist der zulässige
            // Randfall und heißt: sofort bereinigen.
            Assert.Equal(0, settings.DbPurgeDays);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(50, 20)]
        [InlineData(7, 7)]
        public void WriteTo_BackupCount_StaysWithinTheAllowedRange(int configured, int expected)
        {
            (MaintenanceSettingsViewModel viewModel, _, _) = Build();
            viewModel.DbBackupRetentionCount = configured;

            AppSettings settings = new();
            viewModel.WriteTo(settings);

            // Ohne Grenze stünde entweder gar keine Sicherung zur Verfügung oder es blieben
            // Dutzende Kopien der Datenbank liegen.
            Assert.Equal(expected, settings.DbBackupRetentionCount);
        }

        [Fact]
        public void WriteTo_WithoutSettings_Throws()
        {
            (MaintenanceSettingsViewModel viewModel, _, _) = Build();

            _ = Assert.Throws<ArgumentNullException>(() => viewModel.WriteTo(null!));
        }
        [Fact]
        public async Task RunMaintenanceAsync_BereinigtMitDenEingestelltenTagenUndKompaktiert()
        {
            // Der eingestellte Aufbewahrungszeitraum muss beim Dienst ankommen — sonst
            // löscht die Wartung mehr oder weniger, als der Nutzer gewählt hat.
            (MaintenanceSettingsViewModel viewModel, FakeDatabaseMaintenanceService maintenance, _) = Build();
            viewModel.DbPurgeDays = 14;

            await viewModel.RunMaintenanceAsync();

            Assert.Equal(1, maintenance.PurgeCount);
            Assert.Equal(14, maintenance.LastPurgeRetentionDays);
            Assert.Equal(1, maintenance.VacuumCount);
        }

        [Fact]
        public async Task RunMaintenanceAsync_ReichtKeineNegativenTageWeiter()
        {
            (MaintenanceSettingsViewModel viewModel, FakeDatabaseMaintenanceService maintenance, _) = Build();
            viewModel.DbPurgeDays = -5;

            await viewModel.RunMaintenanceAsync();

            Assert.Equal(0, maintenance.LastPurgeRetentionDays);
        }

        [Fact]
        public async Task RunMaintenanceAsync_ZeigtDenFehlerUndGibtDieSchaltflaecheFrei()
        {
            // Ein gesperrtes SQLite-File darf die Wartungsschaltfläche nicht dauerhaft blockieren.
            (MaintenanceSettingsViewModel viewModel, FakeDatabaseMaintenanceService maintenance, _) = Build();
            maintenance.FailureMessage = "Datenbank ist gesperrt";

            await viewModel.RunMaintenanceAsync();

            Assert.False(viewModel.IsMaintaining);
            Assert.Contains("Datenbank ist gesperrt", viewModel.MaintenanceStatusText, StringComparison.Ordinal);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, viewModel.MaintenanceStatusVisibility);
        }

        [Fact]
        public async Task ResetLibraryAsync_ZeigtDenFehlerUndGibtDieSchaltflaecheFrei()
        {
            (MaintenanceSettingsViewModel viewModel, FakeDatabaseMaintenanceService maintenance, _) = Build();
            maintenance.FailureMessage = "Datei in Benutzung";

            await viewModel.ResetLibraryAsync(2);

            Assert.False(viewModel.IsMaintaining);
            Assert.Contains("Datei in Benutzung", viewModel.MaintenanceStatusText, StringComparison.Ordinal);
        }

        [Fact]
        public void Statustext_BleibtOhneInhaltUnsichtbar()
        {
            (MaintenanceSettingsViewModel viewModel, _, _) = Build();

            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.MaintenanceStatusVisibility);
        }

        [Fact]
        public void Sicherungseinstellungen_GeltenAlsNutzeraenderung()
        {
            int edits = 0;
            (MaintenanceSettingsViewModel viewModel, _, _) = Build(onUserEdit: () => edits++);

            viewModel.DbBackupEnabled = !viewModel.DbBackupEnabled;
            viewModel.DbBackupRetentionCount = 7;

            Assert.Equal(2, edits);
        }

        [Fact]
        public async Task LoadLogFilesAsync_BehaeltDieWahlDesNutzers()
        {
            // Nach jedem Neuladen wieder auf "Live" zu springen würde die gerade
            // geöffnete Datei unter dem Nutzer wegziehen.
            (MaintenanceSettingsViewModel viewModel, _, FakeLogViewerCoordinator logs) = Build();
            logs.FileOptions.Add(new EchoPlay.App.Models.LogFileOption("gestern.log", DateTime.UnixEpoch, "C:/logs/gestern.log"));
            logs.FileContents["C:/logs/gestern.log"] = ["erste Zeile", "zweite Zeile"];

            await viewModel.LoadLogFilesAsync();
            viewModel.SelectedLogFile = viewModel.AvailableLogFiles[1];
            await viewModel.LoadLogFilesAsync();

            Assert.Equal("gestern.log", viewModel.SelectedLogFile?.FileName);
        }

        [Fact]
        public async Task DateiWahl_ZeigtDenInhaltDerDatei()
        {
            (MaintenanceSettingsViewModel viewModel, _, FakeLogViewerCoordinator logs) = Build();
            logs.FileOptions.Add(new EchoPlay.App.Models.LogFileOption("gestern.log", DateTime.UnixEpoch, "C:/logs/gestern.log"));
            logs.FileContents["C:/logs/gestern.log"] = ["erste Zeile", "zweite Zeile"];
            await viewModel.LoadLogFilesAsync();

            viewModel.SelectedLogFile = viewModel.AvailableLogFiles[1];
            await Task.Yield();

            Assert.Equal(["erste Zeile", "zweite Zeile"], viewModel.LogEntries);
        }

        [Fact]
        public async Task RueckkehrZurLiveAnsicht_ZeigtWiederDenPuffer()
        {
            (MaintenanceSettingsViewModel viewModel, _, FakeLogViewerCoordinator logs) = Build();
            logs.AddLiveEntry(new EchoPlay.Logger.Models.LogEntry(
                new DateTime(2026, 8, 18, 10, 0, 0, DateTimeKind.Utc),
                EchoPlay.Logger.Models.LogLevel.Information,
                "Live-Eintrag",
                "Test",
                []));
            logs.FileOptions.Add(new EchoPlay.App.Models.LogFileOption("gestern.log", DateTime.UnixEpoch, "C:/logs/gestern.log"));
            logs.FileContents["C:/logs/gestern.log"] = ["Datei-Zeile"];
            await viewModel.LoadLogFilesAsync();

            viewModel.SelectedLogFile = viewModel.AvailableLogFiles[1];
            await Task.Yield();
            viewModel.SelectedLogFile = viewModel.AvailableLogFiles[0];
            await Task.Yield();

            Assert.Contains(viewModel.LogEntries, line => line.Contains("Live-Eintrag", StringComparison.Ordinal));
        }

        [Fact]
        public void Dispose_LaeuftAuchOhneLaufendenZeitgeber()
        {
            (MaintenanceSettingsViewModel viewModel, _, _) = Build();

            viewModel.Dispose();

            Assert.False(viewModel.IsLiveViewActive);
        }
    }
}

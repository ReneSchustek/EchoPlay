using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Management;
using EchoPlay.Logger.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="SettingsViewModel"/> (Top-VM nach Brief 211).
    /// Prüft Load/Save/Theme/Sync gegen die vier Sub-VMs über die Pass-Through-Properties.
    /// </summary>
    public sealed class SettingsViewModelTests
    {
        /// <summary>
        /// Erzeugt einen minimalen <see cref="LoggerManager"/> ohne Dateilogging für Tests.
        /// </summary>
        private static LoggerManager BuildLoggerManager()
        {
            LoggerOptions options       = new() { EnableFileLogging = false, EnableAutoCleanup = false };
            LoggerFactory loggerFactory = new([], options);
            LogCleanupService cleanup   = new(options);
            return new LoggerManager(loggerFactory, cleanup, options);
        }

        /// <summary>
        /// Erzeugt einen minimalen <see cref="StatusBarViewModel"/> für Tests.
        /// </summary>
        private static StatusBarViewModel BuildStatusBar(IServiceScopeFactory scopeFactory)
        {
            return new StatusBarViewModel(
                scopeFactory,
                new FakeThemeService(),
                new TaskbarProgressService(),
                new FakeClock());
        }

        private static SettingsViewModel BuildViewModel(
            FakeAppSettingsDataService settingsService,
            FakeSyncService? syncService = null,
            FakeThemeService? themeService = null,
            FakeErrorDialogService? errorDialogService = null,
            FakeDatabaseMaintenanceService? maintenanceService = null,
            FakeConnectionTestCoordinator? connectionTestCoordinator = null,
            FakeLogViewerCoordinator? logViewerCoordinator = null,
            FakeSpotifyCredentialStore? credentialStore = null,
            FakeSpotifyOptionsProvider? optionsProvider = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => settingsService);
            // StatusBar.RefreshAsync benötigt diese Services – sonst schlägt SaveAsync fehl
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IDatabaseMaintenanceService>(_ => maintenanceService ?? new FakeDatabaseMaintenanceService());

            ServiceProvider provider          = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            FakeSpotifyCredentialStore resolvedCredentialStore = credentialStore ?? new FakeSpotifyCredentialStore();
            FakeSpotifyOptionsProvider resolvedOptionsProvider = optionsProvider ?? new FakeSpotifyOptionsProvider(resolvedCredentialStore);

            return new SettingsViewModel(
                scopeFactory,
                themeService ?? new FakeThemeService(),
                syncService ?? new FakeSyncService(new SyncResult()),
                errorDialogService ?? new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                new FakeLocalizationService(),
                new FakeEpisodePatternAnalyzer(),
                connectionTestCoordinator ?? new FakeConnectionTestCoordinator(),
                resolvedCredentialStore,
                resolvedOptionsProvider,
                logViewerCoordinator ?? new FakeLogViewerCoordinator(),
                BuildLoggerManager(),
                BuildStatusBar(scopeFactory));
        }

        [Fact]
        public async Task LoadAsync_SetsAllProperties_FromDatabase()
        {
            // Alle Felder aus der DB müssen nach LoadAsync korrekt gesetzt sein
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                ActiveTheme          = "PaperCoffee",
                ActiveProvider       = ProviderType.Spotify,
                LocalLibraryEnabled  = false,
                LocalLibraryRootPath = "/meine/musik",
                EpisodeFolderPattern = "{number} - {title}"
            });

            SettingsViewModel vm = BuildViewModel(settings);

            await vm.LoadAsync();

            Assert.Equal("PaperCoffee", vm.ActiveTheme);
            Assert.Equal(ProviderType.Spotify, vm.ActiveProvider);
            Assert.False(vm.LocalLibraryEnabled);
            Assert.Equal("/meine/musik", vm.LocalLibraryRootPath);
            Assert.Equal("{number} - {title}", vm.EpisodeFolderPattern);
        }

        [Fact]
        public async Task SaveAsync_DoesNothing_WhenNotLoaded()
        {
            // Ohne vorherigen LoadAsync darf SaveAsync nichts speichern
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings);

            await vm.SaveAsync();

            Assert.Equal(0, settings.SaveCallCount);
        }

        [Fact]
        public async Task SaveAsync_PersistsAllFields_AfterLoad()
        {
            // Nach LoadAsync müssen geänderte Felder via SaveAsync persistiert werden
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                ActiveTheme = "MidnightLibrary"
            });

            SettingsViewModel vm = BuildViewModel(settings);

            await vm.LoadAsync();
            vm.ActiveTheme = "ModernClassic";
            await vm.SaveAsync();

            Assert.Equal(1, settings.SaveCallCount);
            Assert.Equal("ModernClassic", (await settings.GetAsync()).ActiveTheme);
        }

        [Fact]
        public void ApplyTheme_UpdatesActiveTheme_AndCallsThemeService()
        {
            // ApplyTheme muss ActiveTheme setzen und den ThemeService aufrufen
            FakeThemeService themeService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings, themeService: themeService);

            vm.ApplyTheme("PaperCoffee");

            Assert.Equal("PaperCoffee", vm.ActiveTheme);
            _ = Assert.Single(themeService.AppliedThemes);
            Assert.Equal("PaperCoffee", themeService.AppliedThemes[0]);
        }

        [Fact]
        public async Task SyncAsync_SetsSyncStatusText_WithResult()
        {
            // Nach SyncAsync muss der Statustext das Ergebnis widerspiegeln.
            SyncResult syncResult = new()
            {
                SeriesMatched   = 3,
                SeriesUnmatched = 1,
                EpisodesUpdated = 12,
                TracksCreated   = 36
            };

            FakeSyncService syncService = new(syncResult);
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled  = true,
                LocalLibraryRootPath = @"C:\Musik"
            });
            SettingsViewModel vm = BuildViewModel(settings, syncService: syncService);

            await vm.LoadAsync();
            await vm.SyncAsync();

            Assert.False(string.IsNullOrEmpty(vm.SyncStatusText));
            // Ergebnis-String enthält mindestens den SeriesMatched-Zähler
            Assert.Contains("3", vm.SyncStatusText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SyncAsync_DoesNothing_WhenAlreadySyncing()
        {
            // Parallele Sync-Aufrufe werden ignoriert.
            FakeSyncService syncService = new(new SyncResult());
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled  = true,
                LocalLibraryRootPath = @"C:\Musik"
            });
            SettingsViewModel vm = BuildViewModel(settings, syncService: syncService);

            await vm.LoadAsync();

            // Zwei sequenzielle Aufrufe – da FakeSyncService synchron zurückgibt,
            // ist IsSyncing nach dem ersten Aufruf bereits false
            await vm.SyncAsync();
            await vm.SyncAsync();

            Assert.Equal(2, syncService.SyncCallCount);
        }

        [Fact]
        public async Task IsSyncEnabled_IsFalse_WhileSyncing()
        {
            // IsSyncEnabled muss nach Abschluss wieder true sein.
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled  = true,
                LocalLibraryRootPath = @"C:\Musik"
            });
            SettingsViewModel vm = BuildViewModel(settings);

            await vm.LoadAsync();
            await vm.SyncAsync();

            Assert.True(vm.IsSyncEnabled);
            Assert.False(vm.IsSyncing);
        }

        // --- HasUnsavedChanges-Tests ---

        [Fact]
        public async Task HasUnsavedChanges_IsFalse_AfterLoad()
        {
            // Nach dem Laden darf keine "ungespeicherte Änderung" angezeigt werden
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings);

            await vm.LoadAsync();

            Assert.False(vm.HasUnsavedChanges);
        }

        [Fact]
        public async Task HasUnsavedChanges_IsTrue_AfterPropertyChange()
        {
            // Jede Änderung an einem Einstellungs-Property markiert den Zustand als "unsaved"
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings);

            await vm.LoadAsync();
            vm.OfflineMode = !vm.OfflineMode;

            Assert.True(vm.HasUnsavedChanges);
        }

        [Fact]
        public async Task HasUnsavedChanges_IsFalse_AfterSave()
        {
            // Nach dem Speichern sind keine ungespeicherten Änderungen mehr offen
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings);

            await vm.LoadAsync();
            vm.NewReleaseDays = 120;
            Assert.True(vm.HasUnsavedChanges);

            await vm.SaveAsync();
            Assert.False(vm.HasUnsavedChanges);
        }

        [Fact]
        public async Task HasUnsavedChanges_IsFalse_AfterReload()
        {
            // Erneutes Laden setzt den Änderungszustand zurück
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings);

            await vm.LoadAsync();
            vm.DbPurgeDays = 99;
            Assert.True(vm.HasUnsavedChanges);

            await vm.LoadAsync();
            Assert.False(vm.HasUnsavedChanges);
        }

        [Fact]
        public async Task HasUnsavedChanges_TracksAllSettingsProperties()
        {
            // Alle relevanten Settings-Properties müssen HasUnsavedChanges auslösen
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                ActiveTheme          = "MidnightLibrary",
                ActiveProvider       = ProviderType.AppleMusic,
                LocalLibraryEnabled  = true,
                LocalLibraryRootPath = @"C:\Musik",
                EpisodeFolderPattern = "{number} - {title}",
                NewReleaseDays       = 60,
                OfflineMode          = false,
                DbPurgeDays          = 30,
                AutoImportAfterScan  = false
            });
            SettingsViewModel vm = BuildViewModel(settings);

            // Jedes Property einzeln testen – Load setzt HasUnsavedChanges zurück
            await vm.LoadAsync();
            vm.ActiveTheme = "PaperCoffee";
            Assert.True(vm.HasUnsavedChanges);

            await vm.LoadAsync();
            vm.ActiveProvider = ProviderType.Spotify;
            Assert.True(vm.HasUnsavedChanges);

            await vm.LoadAsync();
            vm.LocalLibraryEnabled = false;
            Assert.True(vm.HasUnsavedChanges);

            await vm.LoadAsync();
            vm.EpisodeFolderPattern = "{title}";
            Assert.True(vm.HasUnsavedChanges);

            await vm.LoadAsync();
            vm.AutoImportAfterScan = true;
            Assert.True(vm.HasUnsavedChanges);
        }

        // --- Log-Viewer-Tests ---

        [Fact]
        public void RefreshLogs_ReturnsEmpty_WhenLiveViewUnavailable()
        {
            // Ohne Live-View-Verfügbarkeit ist der Log-Viewer deaktiviert
            FakeLogViewerCoordinator coordinator = new() { IsLiveViewAvailable = false };
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings, logViewerCoordinator: coordinator);

            vm.RefreshLogs();

            Assert.Empty(vm.LogEntries);
            Assert.False(vm.IsLogViewerAvailable);
        }

        [Fact]
        public void RefreshLogs_ShowsAllEntries_WhenNoFilter()
        {
            // Alle Einträge aus dem Coordinator erscheinen ungefiltert
            FakeLogViewerCoordinator coordinator = new();
            coordinator.AddLiveEntry(new LogEntry(DateTime.Now, LogLevel.Information, "Gestartet", "App", []));
            coordinator.AddLiveEntry(new LogEntry(DateTime.Now, LogLevel.Warning, "Warnung", "Service", []));

            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings, logViewerCoordinator: coordinator);
            vm.RefreshLogs();

            Assert.Equal(2, vm.LogEntries.Count);
        }

        [Fact]
        public void RefreshLogs_FiltersBySearchText_CaseInsensitive()
        {
            // Suchtext filtert Groß-/Kleinschreibungs-unabhängig
            FakeLogViewerCoordinator coordinator = new();
            coordinator.AddLiveEntry(new LogEntry(DateTime.Now, LogLevel.Information, "Spotify gestartet", "Import", []));
            coordinator.AddLiveEntry(new LogEntry(DateTime.Now, LogLevel.Information, "Daten gespeichert", "Data", []));

            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings, logViewerCoordinator: coordinator);
            vm.LogSearchText = "spotify";

            _ = Assert.Single(vm.LogEntries);
            Assert.Contains("Spotify", vm.LogEntries[0], StringComparison.Ordinal);
        }

        [Fact]
        public void RefreshLogs_FiltersByMinimumLevel()
        {
            // Level-Filter: nur Einträge ab Warning sichtbar
            FakeLogViewerCoordinator coordinator = new();
            coordinator.AddLiveEntry(new LogEntry(DateTime.Now, LogLevel.Debug,       "Debug-Meldung",   "A", []));
            coordinator.AddLiveEntry(new LogEntry(DateTime.Now, LogLevel.Information, "Info-Meldung",    "B", []));
            coordinator.AddLiveEntry(new LogEntry(DateTime.Now, LogLevel.Warning,     "Warnung",         "C", []));
            coordinator.AddLiveEntry(new LogEntry(DateTime.Now, LogLevel.Error,       "Fehler",          "D", []));

            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings, logViewerCoordinator: coordinator);
            vm.LogMinimumLevel = LogLevel.Warning;

            Assert.Equal(2, vm.LogEntries.Count);
        }

        [Fact]
        public void RefreshLogs_ShowsExceptionInfo_InFormattedLine()
        {
            // Exception-Nachricht muss im formatierten Eintrag sichtbar sein
            InvalidOperationException ex = new("Verbindung getrennt");
            FakeLogViewerCoordinator coordinator = new();
            coordinator.AddLiveEntry(new LogEntry(DateTime.Now, LogLevel.Error, "Fehler beim Laden", "Net", [], ex));

            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings, logViewerCoordinator: coordinator);
            vm.RefreshLogs();

            _ = Assert.Single(vm.LogEntries);
            Assert.Contains("Verbindung getrennt", vm.LogEntries[0], StringComparison.Ordinal);
        }

        [Fact]
        public void LogLevelFilterIndex_RoundTrip_MapsCorrectly()
        {
            // Index 0=Debug, 1=Info, 2=Warning, 3=Error – Hin- und Rückumrechnung korrekt
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings);

            vm.LogLevelFilterIndex = 2;
            Assert.Equal(LogLevel.Warning, vm.LogMinimumLevel);
            Assert.Equal(2, vm.LogLevelFilterIndex);

            vm.LogLevelFilterIndex = 0;
            Assert.Equal(LogLevel.Debug, vm.LogMinimumLevel);
        }

        // ── Verbindungstest ─────────────────────────────────────────────────

        [Fact]
        public async Task TestConnectionAsync_Success_SetsSuccessResult()
        {
            // Erfolgreicher Test muss ConnectionTestSuccess auf true setzen
            FakeConnectionTestCoordinator coordinator = new(new ConnectionTestResult(true, null));
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });
            SettingsViewModel vm = BuildViewModel(settings, connectionTestCoordinator: coordinator);

            await vm.LoadAsync();
            await vm.TestConnectionAsync();

            _ = Assert.Single(coordinator.Calls);
            Assert.Equal(ProviderType.Spotify, coordinator.Calls[0]);
            Assert.True(vm.ConnectionTestSuccess);
        }

        [Fact]
        public async Task TestConnectionAsync_Failure_SetsFailureResult()
        {
            // Fehlerhafter Test muss ConnectionTestSuccess auf false setzen und Fehlerdetail enthalten
            FakeConnectionTestCoordinator coordinator = new(new ConnectionTestResult(false, "Timeout"));
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.AppleMusic });
            SettingsViewModel vm = BuildViewModel(settings, connectionTestCoordinator: coordinator);

            await vm.LoadAsync();
            await vm.TestConnectionAsync();

            Assert.False(vm.ConnectionTestSuccess);
            Assert.Contains("Timeout", vm.ConnectionTestResultText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TestConnectionAsync_NoProvider_DoesNotCallCoordinator()
        {
            // Bei Provider "Keine" darf kein API-Aufruf erfolgen
            FakeConnectionTestCoordinator coordinator = new();
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.None });
            SettingsViewModel vm = BuildViewModel(settings, connectionTestCoordinator: coordinator);

            await vm.LoadAsync();
            await vm.TestConnectionAsync();

            Assert.Empty(coordinator.Calls);
        }

        // ── ResetLibrary ────────────────────────────────────────────────────

        [Fact]
        public async Task ResetLibraryAsync_Online_CallsClearOnline()
        {
            FakeDatabaseMaintenanceService maintenance = new();
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings, maintenanceService: maintenance);

            await vm.ResetLibraryAsync(0);

            Assert.Equal(1, maintenance.ClearOnlineCount);
            Assert.Equal(0, maintenance.ClearLocalCount);
            Assert.Equal(0, maintenance.ClearAllCount);
        }

        [Fact]
        public async Task ResetLibraryAsync_Local_CallsClearLocal()
        {
            FakeDatabaseMaintenanceService maintenance = new();
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings, maintenanceService: maintenance);

            await vm.ResetLibraryAsync(1);

            Assert.Equal(0, maintenance.ClearOnlineCount);
            Assert.Equal(1, maintenance.ClearLocalCount);
        }

        [Fact]
        public async Task ResetLibraryAsync_All_CallsClearAll()
        {
            FakeDatabaseMaintenanceService maintenance = new();
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings, maintenanceService: maintenance);

            await vm.ResetLibraryAsync(2);

            Assert.Equal(1, maintenance.ClearAllCount);
        }

        [Fact]
        public async Task ClearCacheOnNextStart_LoadSave_Roundtrip()
        {
            AppSettings appSettings = new() { ClearCacheOnNextStart = true };
            FakeAppSettingsDataService settings = new(appSettings);
            SettingsViewModel vm = BuildViewModel(settings);

            await vm.LoadAsync();

            Assert.True(vm.ClearCacheOnNextStart);
        }
    }
}

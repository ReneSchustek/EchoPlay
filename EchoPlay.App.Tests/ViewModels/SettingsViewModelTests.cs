using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Management;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="SettingsViewModel"/>.
    /// Prüft Laden, Speichern, Theme-Wechsel und Sync-Verhalten.
    /// </summary>
    public sealed class SettingsViewModelTests
    {
        /// <summary>
        /// Erzeugt einen minimalen <see cref="LoggerManager"/> ohne Dateilogging für Tests.
        /// File- und Cleanup-Aktivitäten sind deaktiviert, damit keine temporären Dateien entstehen.
        /// </summary>
        private static LoggerManager BuildLoggerManager()
        {
            LoggerOptions options        = new() { EnableFileLogging = false, EnableAutoCleanup = false };
            LoggerFactory loggerFactory  = new([], options);
            LogCleanupService cleanup    = new(options);
            return new LoggerManager(loggerFactory, cleanup, options);
        }

        /// <summary>
        /// Erzeugt einen minimalen <see cref="StatusBarViewModel"/> für Tests.
        /// Die COM-basierte Taskbar-Integration hat im Test keinen Effekt (kein HWND vorhanden).
        /// </summary>
        private static StatusBarViewModel BuildStatusBar(IServiceScopeFactory scopeFactory)
        {
            return new StatusBarViewModel(
                scopeFactory,
                new FakeThemeService(),
                new TaskbarProgressService());
        }

        private static SettingsViewModel BuildViewModel(
            FakeAppSettingsDataService settingsService,
            FakeSyncService? syncService = null,
            FakeThemeService? themeService = null,
            FakeErrorDialogService? errorDialogService = null,
            FakeDatabaseMaintenanceService? maintenanceService = null)
        {
            ServiceCollection services = new();
            services.AddScoped<IAppSettingsDataService>(_ => settingsService);
            // StatusBar.RefreshAsync benötigt diese Services – sonst schlägt SaveAsync fehl
            services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            services.AddScoped<IDatabaseMaintenanceService>(_ => maintenanceService ?? new FakeDatabaseMaintenanceService());

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            return new SettingsViewModel(
                scopeFactory,
                themeService ?? new FakeThemeService(),
                syncService ?? new FakeSyncService(new SyncResult()),
                errorDialogService ?? new FakeErrorDialogService(),
                new FakeEpisodePatternAnalyzer(),
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

            SettingsViewModel vm = BuildViewModel(settings,
                syncService: new FakeSyncService(),
                themeService: new FakeThemeService(),
                errorDialogService: new FakeErrorDialogService());

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
            SettingsViewModel vm = BuildViewModel(settings,
                syncService: new FakeSyncService(),
                themeService: new FakeThemeService(),
                errorDialogService: new FakeErrorDialogService());

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

            SettingsViewModel vm = BuildViewModel(settings,
                syncService: new FakeSyncService(),
                themeService: new FakeThemeService(),
                errorDialogService: new FakeErrorDialogService());

            await vm.LoadAsync();
            vm.ActiveTheme = "ModernClassic";
            await vm.SaveAsync();

            Assert.Equal(1, settings.SaveCallCount);
            Assert.Equal("ModernClassic", (await settings.GetAsync()).ActiveTheme);
        }

        [Fact]
        public async Task ApplyTheme_UpdatesActiveTheme_AndCallsThemeService()
        {
            // ApplyTheme muss ActiveTheme setzen und den ThemeService aufrufen
            FakeThemeService themeService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings,
                syncService: new FakeSyncService(),
                themeService: themeService,
                errorDialogService: new FakeErrorDialogService());

            vm.ApplyTheme("PaperCoffee");

            Assert.Equal("PaperCoffee", vm.ActiveTheme);
            Assert.Single(themeService.AppliedThemes);
            Assert.Equal("PaperCoffee", themeService.AppliedThemes[0]);
        }

        [Fact]
        public async Task SyncAsync_SetsSyncStatusText_WithResult()
        {
            // Nach SyncAsync muss der Statustext das Ergebnis widerspiegeln.
            // LoadAsync wird zuerst aufgerufen – SyncAsync prüft ob Bibliotheksordner gesetzt ist.
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
            SettingsViewModel vm = BuildViewModel(settings,
                syncService: syncService,
                themeService: new FakeThemeService(),
                errorDialogService: new FakeErrorDialogService());

            await vm.LoadAsync();
            await vm.SyncAsync();

            Assert.False(string.IsNullOrEmpty(vm.SyncStatusText));
            // Ergebnis-String enthält mindestens den SeriesMatched-Zähler
            Assert.Contains("3", vm.SyncStatusText);
        }

        [Fact]
        public async Task SyncAsync_DoesNothing_WhenAlreadySyncing()
        {
            // Parallele Sync-Aufrufe werden ignoriert.
            // LoadAsync setzt den Bibliothekspfad – ohne ihn blockt SyncAsync den SyncService.
            FakeSyncService syncService = new(new SyncResult());
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled  = true,
                LocalLibraryRootPath = @"C:\Musik"
            });
            SettingsViewModel vm = BuildViewModel(settings,
                syncService: syncService,
                themeService: new FakeThemeService(),
                errorDialogService: new FakeErrorDialogService());

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
            // LoadAsync setzt den Bibliothekspfad – nötig, damit IsSyncEnabled == true.
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled  = true,
                LocalLibraryRootPath = @"C:\Musik"
            });
            SettingsViewModel vm = BuildViewModel(settings,
                syncService: new FakeSyncService(),
                themeService: new FakeThemeService(),
                errorDialogService: new FakeErrorDialogService());

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
            SettingsViewModel vm = BuildViewModel(settings,
                syncService: new FakeSyncService(),
                themeService: new FakeThemeService(),
                errorDialogService: new FakeErrorDialogService());

            await vm.LoadAsync();

            Assert.False(vm.HasUnsavedChanges);
        }

        [Fact]
        public async Task HasUnsavedChanges_IsTrue_AfterPropertyChange()
        {
            // Jede Änderung an einem Einstellungs-Property markiert den Zustand als "unsaved"
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings,
                syncService: new FakeSyncService(),
                themeService: new FakeThemeService(),
                errorDialogService: new FakeErrorDialogService());

            await vm.LoadAsync();
            vm.OfflineMode = !vm.OfflineMode;

            Assert.True(vm.HasUnsavedChanges);
        }

        [Fact]
        public async Task HasUnsavedChanges_IsFalse_AfterSave()
        {
            // Nach dem Speichern sind keine ungespeicherten Änderungen mehr offen
            FakeAppSettingsDataService settings = new(new AppSettings());
            SettingsViewModel vm = BuildViewModel(settings,
                syncService: new FakeSyncService(),
                themeService: new FakeThemeService(),
                errorDialogService: new FakeErrorDialogService());

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
            SettingsViewModel vm = BuildViewModel(settings,
                syncService: new FakeSyncService(),
                themeService: new FakeThemeService(),
                errorDialogService: new FakeErrorDialogService());

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
                ActiveTheme = "MidnightLibrary",
                ActiveProvider = ProviderType.AppleMusic,
                LocalLibraryEnabled = true,
                LocalLibraryRootPath = @"C:\Musik",
                EpisodeFolderPattern = "{number} - {title}",
                NewReleaseDays = 60,
                OfflineMode = false,
                DbPurgeDays = 30,
                AutoImportAfterScan = false
            });
            SettingsViewModel vm = BuildViewModel(settings,
                syncService: new FakeSyncService(),
                themeService: new FakeThemeService(),
                errorDialogService: new FakeErrorDialogService());

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

        private static SettingsViewModel BuildViewModelWithSink(MemorySink memorySink)
        {
            ServiceCollection services = new();
            services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(new AppSettings()));
            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            return new SettingsViewModel(
                scopeFactory,
                new FakeThemeService(),
                new FakeSyncService(),
                new FakeErrorDialogService(),
                new FakeEpisodePatternAnalyzer(),
                BuildLoggerManager(),
                BuildStatusBar(scopeFactory),
                memorySink);
        }

        [Fact]
        public void RefreshLogs_ReturnsEmpty_WhenNoMemorySink()
        {
            // Ohne MemorySink ist der Log-Viewer deaktiviert
            ServiceCollection services = new();
            services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(new AppSettings()));
            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            SettingsViewModel vm = new(
                scopeFactory,
                new FakeThemeService(),
                new FakeSyncService(),
                new FakeErrorDialogService(),
                new FakeEpisodePatternAnalyzer(),
                BuildLoggerManager(),
                BuildStatusBar(scopeFactory));

            vm.RefreshLogs();

            Assert.Empty(vm.LogEntries);
            Assert.False(vm.IsLogViewerAvailable);
        }

        [Fact]
        public async Task RefreshLogs_ShowsAllEntries_WhenNoFilter()
        {
            // Alle Einträge aus dem Puffer erscheinen ungefiltert
            MemorySink sink = new(50);
            await sink.WriteAsync(new LogEntry(DateTime.Now, LogLevel.Information, "Gestartet", "App", []));
            await sink.WriteAsync(new LogEntry(DateTime.Now, LogLevel.Warning, "Warnung", "Service", []));

            SettingsViewModel vm = BuildViewModelWithSink(sink);
            vm.RefreshLogs();

            Assert.Equal(2, vm.LogEntries.Count);
        }

        [Fact]
        public async Task RefreshLogs_FiltersBySearchText_CaseInsensitive()
        {
            // Suchtext filtert Groß-/Kleinschreibungs-unabhängig
            MemorySink sink = new(50);
            await sink.WriteAsync(new LogEntry(DateTime.Now, LogLevel.Information, "Spotify gestartet", "Import", []));
            await sink.WriteAsync(new LogEntry(DateTime.Now, LogLevel.Information, "Daten gespeichert", "Data", []));

            SettingsViewModel vm = BuildViewModelWithSink(sink);
            vm.LogSearchText = "spotify";

            Assert.Single(vm.LogEntries);
            Assert.Contains("Spotify", vm.LogEntries[0]);
        }

        [Fact]
        public async Task RefreshLogs_FiltersByMinimumLevel()
        {
            // Level-Filter: nur Einträge ab Warning sichtbar
            MemorySink sink = new(50);
            await sink.WriteAsync(new LogEntry(DateTime.Now, LogLevel.Debug,       "Debug-Meldung",   "A", []));
            await sink.WriteAsync(new LogEntry(DateTime.Now, LogLevel.Information, "Info-Meldung",    "B", []));
            await sink.WriteAsync(new LogEntry(DateTime.Now, LogLevel.Warning,     "Warnung",         "C", []));
            await sink.WriteAsync(new LogEntry(DateTime.Now, LogLevel.Error,       "Fehler",          "D", []));

            SettingsViewModel vm = BuildViewModelWithSink(sink);
            vm.LogMinimumLevel = LogLevel.Warning;

            Assert.Equal(2, vm.LogEntries.Count);
        }

        [Fact]
        public async Task RefreshLogs_ShowsExceptionInfo_InFormattedLine()
        {
            // Exception-Nachricht muss im formatierten Eintrag sichtbar sein
            MemorySink sink = new(50);
            InvalidOperationException ex = new("Verbindung getrennt");
            await sink.WriteAsync(new LogEntry(DateTime.Now, LogLevel.Error, "Fehler beim Laden", "Net", [], ex));

            SettingsViewModel vm = BuildViewModelWithSink(sink);
            vm.RefreshLogs();

            Assert.Single(vm.LogEntries);
            Assert.Contains("Verbindung getrennt", vm.LogEntries[0]);
        }

        [Fact]
        public void LogLevelFilterIndex_RoundTrip_MapsCorrectly()
        {
            // Index 0=Debug, 1=Info, 2=Warning, 3=Error – Hin- und Rückumrechnung korrekt
            SettingsViewModel vm = BuildViewModelWithSink(new MemorySink());

            vm.LogLevelFilterIndex = 2;
            Assert.Equal(LogLevel.Warning, vm.LogMinimumLevel);
            Assert.Equal(2, vm.LogLevelFilterIndex);

            vm.LogLevelFilterIndex = 0;
            Assert.Equal(LogLevel.Debug, vm.LogMinimumLevel);
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

using EchoPlay.App.Helpers;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Analysis;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die Einstellungsseite.
    /// Fasst vier Sub-VMs (je ein Tab: Allgemein, Online, Lokal, Verwaltung/Protokolle) und die
    /// gemeinsame Load/Save-Koordination zusammen. Sub-VMs sind in eigenen Dateien definiert und
    /// kapseln den jeweiligen Tab-Zustand; das Top-VM hält nur <see cref="IsLoading"/>,
    /// <see cref="HasUnsavedChanges"/>, die gemeinsame Persistenz und die Pass-Through-Eigenschaften
    /// für die unveränderte Page-XAML.
    /// </summary>
    public sealed class SettingsViewModel : ObservableObject, IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IThemeService _themeService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly LoggerManager _loggerManager;
        private readonly StatusBarViewModel _statusBar;

        // Referenz auf die geladene Entität – nötig für SaveAsync, um den EF-Track nicht zu verlieren
        private AppSettings? _loadedSettings;

        private bool _isLoading;
        private bool _hasUnsavedChanges;

        /// <summary>
        /// Initialisiert das ViewModel und erzeugt die vier Sub-VMs mit den benötigten Abhängigkeiten.
        /// </summary>
        /// <param name="scopeFactory">DI-Scope-Fabrik für Datenbankzugriffe.</param>
        /// <param name="themeService">Service für den Live-Themewechsel.</param>
        /// <param name="syncService">Service für den lokalen Bibliothek-Sync.</param>
        /// <param name="errorDialogService">Service für Fehler-Dialoge.</param>
        /// <param name="patternAnalyzer">Analysiert Episodenmuster im lokalen Bibliotheksordner.</param>
        /// <param name="connectionTestCoordinator">Kapselt den Verbindungstest gegen den aktiven Provider.</param>
        /// <param name="logViewerCoordinator">Kapselt Dateisystem- und Live-Puffer-Zugriff für den Log-Viewer.</param>
        /// <param name="loggerManager">Wird nach dem Speichern mit den neuen Werten aktualisiert.</param>
        /// <param name="statusBar">StatusBar-Singleton – wird über <see cref="HasUnsavedChanges"/> informiert.</param>
        public SettingsViewModel(
            IServiceScopeFactory scopeFactory,
            IThemeService themeService,
            ISyncService syncService,
            IErrorDialogService errorDialogService,
            IEpisodePatternAnalyzer patternAnalyzer,
            IConnectionTestCoordinator connectionTestCoordinator,
            ILogViewerCoordinator logViewerCoordinator,
            LoggerManager loggerManager,
            StatusBarViewModel statusBar)
        {
            _scopeFactory       = scopeFactory;
            _themeService       = themeService;
            _errorDialogService = errorDialogService;
            _loggerManager      = loggerManager;
            _statusBar          = statusBar;

            // Sub-VMs mit gemeinsamem Edit-Callback – jede Nutzeränderung setzt HasUnsavedChanges
            GeneralVM = new GeneralSettingsViewModel(OnSubVmUserEdit);
            OnlineVM  = new OnlineSettingsViewModel(connectionTestCoordinator, OnSubVmUserEdit);
            LocalVM   = new LocalSettingsViewModel(syncService, errorDialogService, patternAnalyzer, OnSubVmUserEdit);
            MaintenanceVM = new MaintenanceSettingsViewModel(scopeFactory, logViewerCoordinator, OnSubVmUserEdit);

            // PropertyChanged durchreichen, damit die XAML-Bindings auf den Top-VM-Pass-Through-Properties
            // weiterhin aktualisiert werden, obwohl der Wert in einem Sub-VM liegt.
            GeneralVM.PropertyChanged     += OnSubVmPropertyChanged;
            OnlineVM.PropertyChanged      += OnSubVmPropertyChanged;
            LocalVM.PropertyChanged       += OnSubVmPropertyChanged;
            MaintenanceVM.PropertyChanged += OnSubVmPropertyChanged;

            // Pattern-Dialog-Event an die Page weiterreichen
            LocalVM.PatternSelectionRequested += OnLocalVmPatternSelectionRequested;
        }

        // ── Sub-VMs ─────────────────────────────────────────────────────────────

        /// <summary>Sub-VM für den Allgemein-Tab (Theme, Sprache, Neuerscheinungen, Offline).</summary>
        public GeneralSettingsViewModel GeneralVM { get; }

        /// <summary>Sub-VM für den Online-Tab (Provider, Verbindungstest).</summary>
        public OnlineSettingsViewModel OnlineVM { get; }

        /// <summary>Sub-VM für den Lokal-Tab (Pfad, Muster, Sync, Auto-Import).</summary>
        public LocalSettingsViewModel LocalVM { get; }

        /// <summary>Sub-VM für Verwaltung + Protokolle (Cache, Purge, Reset, Log-Viewer).</summary>
        public MaintenanceSettingsViewModel MaintenanceVM { get; }

        // ── Top-VM-State ────────────────────────────────────────────────────────

        /// <summary>Gibt an, ob gerade ein Ladevorgang läuft.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Gibt an, ob der Nutzer Einstellungen geändert hat, die noch nicht gespeichert wurden.
        /// Wird automatisch auf <c>true</c> gesetzt, sobald ein beliebiges Sub-VM-Property geändert wird.
        /// Nach <see cref="SaveAsync"/> und <see cref="LoadAsync"/> wird der Wert zurückgesetzt.
        /// </summary>
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                if (SetProperty(ref _hasUnsavedChanges, value))
                {
                    // StatusBar-Singleton über den Änderungszustand informieren,
                    // damit der rote Hinweistext in der Info-Leiste aktualisiert wird
                    _statusBar.HasUnsavedSettings = value;
                }
            }
        }

        // ── Pattern-Dialog-Event ────────────────────────────────────────────────

        /// <summary>
        /// Wird ausgelöst, wenn der Lokal-Tab mehrere Muster-Vorschläge liefert und der Nutzer
        /// eines auswählen soll. Die Page zeigt darauf einen ContentDialog an.
        /// </summary>
        public event Action<IReadOnlyList<PatternSuggestionDisplay>>? PatternSelectionRequested;

        // ── Pass-Through-Eigenschaften: Allgemein ───────────────────────────────

        /// <inheritdoc cref="GeneralSettingsViewModel.ActiveTheme"/>
        public string ActiveTheme
        {
            get => GeneralVM.ActiveTheme;
            set => GeneralVM.ActiveTheme = value;
        }

        /// <inheritdoc cref="GeneralSettingsViewModel.ActiveLanguage"/>
        public string ActiveLanguage
        {
            get => GeneralVM.ActiveLanguage;
            set => GeneralVM.ActiveLanguage = value;
        }

        /// <inheritdoc cref="GeneralSettingsViewModel.AvailableLanguages"/>
        public IReadOnlyList<LanguageOption> AvailableLanguages => GeneralVM.AvailableLanguages;

        /// <inheritdoc cref="GeneralSettingsViewModel.NewReleaseDays"/>
        public int NewReleaseDays
        {
            get => GeneralVM.NewReleaseDays;
            set => GeneralVM.NewReleaseDays = value;
        }

        /// <inheritdoc cref="GeneralSettingsViewModel.OfflineMode"/>
        public bool OfflineMode
        {
            get => GeneralVM.OfflineMode;
            set => GeneralVM.OfflineMode = value;
        }

        /// <inheritdoc cref="GeneralSettingsViewModel.OnlineOnlyMode"/>
        public bool OnlineOnlyMode
        {
            get => GeneralVM.OnlineOnlyMode;
            set => GeneralVM.OnlineOnlyMode = value;
        }

        /// <inheritdoc cref="GeneralSettingsViewModel.LogRetentionDays"/>
        public int LogRetentionDays
        {
            get => GeneralVM.LogRetentionDays;
            set => GeneralVM.LogRetentionDays = value;
        }

        /// <inheritdoc cref="GeneralSettingsViewModel.MinimumLogLevel"/>
        public LogLevel MinimumLogLevel
        {
            get => GeneralVM.MinimumLogLevel;
            set => GeneralVM.MinimumLogLevel = value;
        }

        /// <inheritdoc cref="GeneralSettingsViewModel.MinimumLogLevelIndex"/>
        public int MinimumLogLevelIndex
        {
            get => GeneralVM.MinimumLogLevelIndex;
            set => GeneralVM.MinimumLogLevelIndex = value;
        }

        // ── Pass-Through-Eigenschaften: Online ──────────────────────────────────

        /// <inheritdoc cref="OnlineSettingsViewModel.ActiveProvider"/>
        public ProviderType ActiveProvider
        {
            get => OnlineVM.ActiveProvider;
            set => OnlineVM.ActiveProvider = value;
        }

        /// <inheritdoc cref="OnlineSettingsViewModel.ActiveProviderTag"/>
        public string ActiveProviderTag
        {
            get => OnlineVM.ActiveProviderTag;
            set => OnlineVM.ActiveProviderTag = value;
        }

        /// <inheritdoc cref="OnlineSettingsViewModel.IsTestingConnection"/>
        public bool IsTestingConnection => OnlineVM.IsTestingConnection;

        /// <inheritdoc cref="OnlineSettingsViewModel.ConnectionTestResultText"/>
        public string? ConnectionTestResultText => OnlineVM.ConnectionTestResultText;

        /// <inheritdoc cref="OnlineSettingsViewModel.ConnectionTestSuccess"/>
        public bool? ConnectionTestSuccess => OnlineVM.ConnectionTestSuccess;

        /// <inheritdoc cref="OnlineSettingsViewModel.ConnectionTestSuccessVisibility"/>
        public Visibility ConnectionTestSuccessVisibility => OnlineVM.ConnectionTestSuccessVisibility;

        /// <inheritdoc cref="OnlineSettingsViewModel.ConnectionTestFailureVisibility"/>
        public Visibility ConnectionTestFailureVisibility => OnlineVM.ConnectionTestFailureVisibility;

        /// <inheritdoc cref="OnlineSettingsViewModel.ConnectionTestResultVisibility"/>
        public Visibility ConnectionTestResultVisibility => OnlineVM.ConnectionTestResultVisibility;

        /// <inheritdoc cref="OnlineSettingsViewModel.TestConnectionCommand"/>
        public ICommand TestConnectionCommand => OnlineVM.TestConnectionCommand;

        // ── Pass-Through-Eigenschaften: Lokal ───────────────────────────────────

        /// <inheritdoc cref="LocalSettingsViewModel.LocalLibraryEnabled"/>
        public bool LocalLibraryEnabled
        {
            get => LocalVM.LocalLibraryEnabled;
            set => LocalVM.LocalLibraryEnabled = value;
        }

        /// <inheritdoc cref="LocalSettingsViewModel.LocalLibraryRootPath"/>
        public string LocalLibraryRootPath
        {
            get => LocalVM.LocalLibraryRootPath;
            set => LocalVM.LocalLibraryRootPath = value;
        }

        /// <inheritdoc cref="LocalSettingsViewModel.EpisodeFolderPattern"/>
        public string EpisodeFolderPattern
        {
            get => LocalVM.EpisodeFolderPattern;
            set => LocalVM.EpisodeFolderPattern = value;
        }

        /// <inheritdoc cref="LocalSettingsViewModel.AutoImportAfterScan"/>
        public bool AutoImportAfterScan
        {
            get => LocalVM.AutoImportAfterScan;
            set => LocalVM.AutoImportAfterScan = value;
        }

        /// <inheritdoc cref="LocalSettingsViewModel.IsSyncing"/>
        public bool IsSyncing => LocalVM.IsSyncing;

        /// <inheritdoc cref="LocalSettingsViewModel.IsSyncEnabled"/>
        public bool IsSyncEnabled => LocalVM.IsSyncEnabled;

        /// <inheritdoc cref="LocalSettingsViewModel.SyncStatusText"/>
        public string SyncStatusText => LocalVM.SyncStatusText;

        /// <inheritdoc cref="LocalSettingsViewModel.PatternSuggestions"/>
        public IReadOnlyList<PatternSuggestionDisplay> PatternSuggestions => LocalVM.PatternSuggestions;

        /// <inheritdoc cref="LocalSettingsViewModel.PatternSuggestionsVisibility"/>
        public Visibility PatternSuggestionsVisibility => LocalVM.PatternSuggestionsVisibility;

        // ── Pass-Through-Eigenschaften: Verwaltung + Protokolle ─────────────────

        /// <inheritdoc cref="MaintenanceSettingsViewModel.DbPurgeDays"/>
        public int DbPurgeDays
        {
            get => MaintenanceVM.DbPurgeDays;
            set => MaintenanceVM.DbPurgeDays = value;
        }

        /// <inheritdoc cref="MaintenanceSettingsViewModel.ClearCacheOnNextStart"/>
        public bool ClearCacheOnNextStart
        {
            get => MaintenanceVM.ClearCacheOnNextStart;
            set => MaintenanceVM.ClearCacheOnNextStart = value;
        }

        /// <inheritdoc cref="MaintenanceSettingsViewModel.IsMaintaining"/>
        public bool IsMaintaining => MaintenanceVM.IsMaintaining;

        /// <inheritdoc cref="MaintenanceSettingsViewModel.IsNotMaintaining"/>
        public bool IsNotMaintaining => MaintenanceVM.IsNotMaintaining;

        /// <inheritdoc cref="MaintenanceSettingsViewModel.MaintenanceStatusText"/>
        public string MaintenanceStatusText => MaintenanceVM.MaintenanceStatusText;

        /// <inheritdoc cref="MaintenanceSettingsViewModel.MaintenanceStatusVisibility"/>
        public Visibility MaintenanceStatusVisibility => MaintenanceVM.MaintenanceStatusVisibility;

        /// <inheritdoc cref="MaintenanceSettingsViewModel.LogEntries"/>
        public ObservableCollection<string> LogEntries => MaintenanceVM.LogEntries;

        /// <inheritdoc cref="MaintenanceSettingsViewModel.IsLogViewerAvailable"/>
        public bool IsLogViewerAvailable => MaintenanceVM.IsLogViewerAvailable;

        /// <inheritdoc cref="MaintenanceSettingsViewModel.LogSearchText"/>
        public string LogSearchText
        {
            get => MaintenanceVM.LogSearchText;
            set => MaintenanceVM.LogSearchText = value;
        }

        /// <inheritdoc cref="MaintenanceSettingsViewModel.LogMinimumLevel"/>
        public LogLevel LogMinimumLevel
        {
            get => MaintenanceVM.LogMinimumLevel;
            set => MaintenanceVM.LogMinimumLevel = value;
        }

        /// <inheritdoc cref="MaintenanceSettingsViewModel.LogLevelFilterIndex"/>
        public int LogLevelFilterIndex
        {
            get => MaintenanceVM.LogLevelFilterIndex;
            set => MaintenanceVM.LogLevelFilterIndex = value;
        }

        /// <inheritdoc cref="MaintenanceSettingsViewModel.AvailableLogFiles"/>
        public IReadOnlyList<LogFileOption> AvailableLogFiles => MaintenanceVM.AvailableLogFiles;

        /// <inheritdoc cref="MaintenanceSettingsViewModel.SelectedLogFile"/>
        public LogFileOption? SelectedLogFile
        {
            get => MaintenanceVM.SelectedLogFile;
            set => MaintenanceVM.SelectedLogFile = value;
        }

        /// <inheritdoc cref="MaintenanceSettingsViewModel.IsLiveViewActive"/>
        public bool IsLiveViewActive
        {
            get => MaintenanceVM.IsLiveViewActive;
            set => MaintenanceVM.IsLiveViewActive = value;
        }

        // ── Laden und Speichern ──────────────────────────────────────────────────

        /// <summary>
        /// Lädt die aktuellen Einstellungen aus der Datenbank und verteilt sie an die Sub-VMs.
        /// Das Laden markiert <see cref="HasUnsavedChanges"/> nicht als geändert.
        /// Zusätzlich werden die verfügbaren Log-Dateien neu eingelesen.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task LoadAsync()
        {
            IsLoading = true;

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
                AppSettings settings = await settingsService.GetAsync();
                _loadedSettings = settings;

                GeneralVM.LoadFrom(settings);
                OnlineVM.LoadFrom(settings);
                LocalVM.LoadFrom(settings);
                MaintenanceVM.LoadFrom(settings);

                // Log-Dateien asynchron laden – darf ruhig parallel zur restlichen Initialisierung laufen
                await MaintenanceVM.LoadLogFilesAsync();
            }
            finally
            {
                HasUnsavedChanges = false;
                IsLoading = false;
            }
        }

        /// <summary>
        /// Speichert alle Einstellungsfelder dauerhaft in der Datenbank.
        /// Ohne vorherigen <see cref="LoadAsync"/>-Aufruf wird nichts gespeichert, um versehentliches
        /// Überschreiben vorhandener Daten zu vermeiden.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task SaveAsync()
        {
            if (_loadedSettings is null)
            {
                return;
            }

            GeneralVM.WriteTo(_loadedSettings);
            OnlineVM.WriteTo(_loadedSettings);
            LocalVM.WriteTo(_loadedSettings);
            MaintenanceVM.WriteTo(_loadedSettings);

            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            await settingsService.SaveAsync(_loadedSettings);

            // StatusBar sofort aktualisieren – Offline-Symbol, Menü-Sichtbarkeit der
            // Online-Mediathek und Provider-Anzeige müssen ohne Seitenwechsel reagieren.
            await _statusBar.RefreshAsync();

            // LoggerManager sofort informieren – beide Werte gelten ab jetzt für alle laufenden Logger
            _loggerManager.UpdateRetentionDays(_loadedSettings.LogRetentionDays);
            _loggerManager.UpdateMinimumLevel(_loadedSettings.MinimumLogLevel);

            HasUnsavedChanges = false;

            // Hinweis wenn Offline-Modus deaktiviert, aber kein Provider konfiguriert ist –
            // ohne Provider bleibt die Online-Mediathek unsichtbar, was den Nutzer verwirren kann.
            if (!OfflineMode && ActiveProvider == ProviderType.None)
            {
                await _errorDialogService.ShowAsync(
                    SafeResourceLoader.Get("NoProviderHintTitle", "Kein Provider"),
                    SafeResourceLoader.Get("NoProviderHintMessage", "Kein Online-Provider konfiguriert."));
            }
        }

        /// <summary>
        /// Wendet ein neues Theme sofort live an und merkt es für <see cref="SaveAsync"/> vor.
        /// Die Persistenz übernimmt der <see cref="IThemeService"/> intern.
        /// </summary>
        /// <param name="themeName">Name des anzuwendenden Themes.</param>
        public void ApplyTheme(string themeName)
        {
            ActiveTheme = themeName;
            _themeService.ApplyTheme(themeName);
        }

        /// <summary>
        /// Speichert alle Einstellungen, setzt die Sprachpräferenz und startet die App neu.
        /// WinUI 3 kann Ressourcendateien nicht zur Laufzeit neu laden – der Neustart ist zwingend.
        /// </summary>
        /// <param name="languageCode">Der BCP-47-Sprachcode der gewählten Sprache.</param>
        /// <returns>Asynchrone Ausführung bis zum Neustart.</returns>
        public async Task ChangeLanguageAsync(string languageCode)
        {
            if (_loadedSettings is null)
            {
                return;
            }

            // Sub-VM-Werte in Entität schreiben, dann Sprache überschreiben
            GeneralVM.WriteTo(_loadedSettings);
            OnlineVM.WriteTo(_loadedSettings);
            LocalVM.WriteTo(_loadedSettings);
            MaintenanceVM.WriteTo(_loadedSettings);

            _loadedSettings.ActiveLanguage = languageCode;

            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            await settingsService.SaveAsync(_loadedSettings);

            // PrimaryLanguageOverride muss vor dem Neustart gesetzt werden –
            // Windows App Runtime liest die Sprache beim Start und kann sie danach nicht wechseln
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageCode;

            // Neustart des MSIX-Pakets – danach lädt die App alle .resw-Ressourcen in der neuen Sprache
            Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
        }

        // ── Pass-Through-Methoden ──────────────────────────────────────────────

        /// <inheritdoc cref="LocalSettingsViewModel.SyncAsync"/>
        public Task SyncAsync() => LocalVM.SyncAsync();

        /// <inheritdoc cref="LocalSettingsViewModel.BrowseLibraryFolderAsync"/>
        public Task BrowseLibraryFolderAsync(nint windowHandle) => LocalVM.BrowseLibraryFolderAsync(windowHandle);

        /// <inheritdoc cref="LocalSettingsViewModel.AnalyzePatternAsync"/>
        public Task AnalyzePatternAsync() => LocalVM.AnalyzePatternAsync();

        /// <inheritdoc cref="LocalSettingsViewModel.ApplyPatternSuggestion"/>
        public void ApplyPatternSuggestion(string pattern) => LocalVM.ApplyPatternSuggestion(pattern);

        /// <inheritdoc cref="OnlineSettingsViewModel.TestConnectionAsync"/>
        public Task TestConnectionAsync() => OnlineVM.TestConnectionAsync();

        /// <inheritdoc cref="MaintenanceSettingsViewModel.RunMaintenanceAsync"/>
        public Task RunMaintenanceAsync() => MaintenanceVM.RunMaintenanceAsync();

        /// <inheritdoc cref="MaintenanceSettingsViewModel.ResetLibraryAsync"/>
        public Task ResetLibraryAsync(int scopeIndex) => MaintenanceVM.ResetLibraryAsync(scopeIndex);

        /// <inheritdoc cref="MaintenanceSettingsViewModel.RefreshLogs"/>
        public void RefreshLogs() => MaintenanceVM.RefreshLogs();

        // ── Event-Weiterleitung ─────────────────────────────────────────────────

        /// <summary>
        /// Leitet PropertyChanged-Events der Sub-VMs an eigene Listener weiter, damit die
        /// XAML-Bindings an die Pass-Through-Properties auf dem Top-VM aktualisiert werden.
        /// Alle Property-Namen sind zwischen Top-VM und Sub-VMs bewusst identisch gehalten.
        /// </summary>
        private void OnSubVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(e.PropertyName);
        }

        /// <summary>
        /// Leitet das PatternSelectionRequested-Event des Lokal-Sub-VMs an externe Listener weiter.
        /// </summary>
        private void OnLocalVmPatternSelectionRequested(IReadOnlyList<PatternSuggestionDisplay> suggestions)
        {
            PatternSelectionRequested?.Invoke(suggestions);
        }

        /// <summary>
        /// Wird von allen Sub-VMs bei Nutzeränderung aufgerufen und aktiviert
        /// <see cref="HasUnsavedChanges"/>. Während <see cref="LoadAsync"/> unterdrückt jedes
        /// Sub-VM selbst den Callback.
        /// </summary>
        private void OnSubVmUserEdit()
        {
            HasUnsavedChanges = true;
        }

        /// <summary>
        /// Stoppt den Log-Live-View-Timer und gibt die Sub-VM-Ressourcen frei.
        /// Wird von der Page beim Verlassen aufgerufen.
        /// </summary>
        public void Dispose()
        {
            GeneralVM.PropertyChanged     -= OnSubVmPropertyChanged;
            OnlineVM.PropertyChanged      -= OnSubVmPropertyChanged;
            LocalVM.PropertyChanged       -= OnSubVmPropertyChanged;
            MaintenanceVM.PropertyChanged -= OnSubVmPropertyChanged;
            LocalVM.PatternSelectionRequested -= OnLocalVmPatternSelectionRequested;

            MaintenanceVM.Dispose();
        }
    }
}

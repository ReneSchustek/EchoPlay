using EchoPlay.App.Services;
using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Analysis;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using EchoPlay.Spotify.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.ApplicationModel.Resources;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Repräsentiert eine Log-Datei in der Datei-Auswahl des Log-Viewers.
    /// </summary>
    /// <param name="FileName">Angezeigter Dateiname ohne Pfad.</param>
    /// <param name="Date">Datum der Log-Datei.</param>
    /// <param name="FilePath">Vollständiger Dateipfad. <see langword="null"/> für den Live-Modus.</param>
    public sealed record LogFileOption(string FileName, DateTime Date, string? FilePath);

    /// <summary>
    /// ViewModel für die Einstellungsseite.
    /// Lädt und speichert <see cref="AppSettings"/>, koordiniert den Live-Themewechsel
    /// über den <see cref="ThemeService"/> und den Sprachwechsel mit App-Neustart.
    /// Der Online-Tab unterstützt zusätzlich einen Verbindungstest gegen den aktiven Provider.
    /// </summary>
    public sealed class SettingsViewModel : ObservableObject, IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IThemeService _themeService;
        private readonly ISyncService _syncService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly IEpisodePatternAnalyzer _patternAnalyzer;
        private readonly LoggerManager _loggerManager;
        private readonly StatusBarViewModel _statusBar;
        private readonly MemorySink? _memorySink;
        private readonly RelayCommand _testConnectionCommand;

        // DispatcherTimer für Live-View – aktualisiert den Log-Puffer alle 2 Sekunden
        private Microsoft.UI.Xaml.DispatcherTimer? _liveViewTimer;

        // Referenz auf die geladene Entität – nötig für SaveAsync, um den EF-Track nicht zu verlieren
        private AppSettings? _loadedSettings;

        private string _activeTheme = "MidnightLibrary";
        private string _activeLanguage = "de";
        private ProviderType _activeProvider = ProviderType.AppleMusic;
        private bool _localLibraryEnabled = true;
        private string? _localLibraryRootPath;
        private string _episodeFolderPattern = "{number:000} - {title}";
        private bool _isLoading;
        private bool _isSyncing;
        private string _syncStatusText = string.Empty;
        private IReadOnlyList<PatternSuggestion> _patternSuggestions = [];
        private IReadOnlyList<LogFileOption> _availableLogFiles = [];
        private LogFileOption? _selectedLogFile;
        private bool _isLiveViewActive;
        private int _logRetentionDays = 30;
        private LogLevel _minimumLogLevel = LogLevel.Information;
        private string _logSearchText = string.Empty;
        private LogLevel _logMinimumLevel = LogLevel.Debug;
        private bool _isTestingConnection;
        private string? _connectionTestResultText;
        private bool? _connectionTestSuccess;
        private bool _autoImportAfterScan;
        private int _newReleaseDays = 60;
        private bool _offlineMode;
        private bool _onlineOnlyMode;
        private int _dbPurgeDays = 30;
        private bool _clearCacheOnNextStart;
        private bool _isMaintaining;
        private string _maintenanceStatusText = string.Empty;
        private bool _hasUnsavedChanges;

        /// <summary>
        /// Guard-Flag: verhindert, dass <see cref="LoadAsync"/> die Properties als "geändert" markiert.
        /// Ohne dieses Flag würde jeder Setter-Aufruf während des Ladens <see cref="HasUnsavedChanges"/>
        /// auf <c>true</c> setzen, obwohl der Nutzer noch nichts geändert hat.
        /// </summary>
        private bool _isBatchLoadingSettings;

        /// <summary>
        /// Initialisiert das ViewModel mit den benötigten Abhängigkeiten.
        /// </summary>
        /// <param name="scopeFactory">DI-Scope-Fabrik für Datenbankzugriffe.</param>
        /// <param name="themeService">Service für den Live-Themewechsel.</param>
        /// <param name="syncService">Service für den lokalen Bibliothek-Sync.</param>
        /// <param name="errorDialogService">Service für Fehler-Dialoge.</param>
        /// <param name="patternAnalyzer">Analysiert Episodenmuster im lokalen Bibliotheksordner.</param>
        /// <param name="loggerManager">Für das Aktualisieren der Aufbewahrungszeit nach Einstellungsänderung.</param>
        /// <param name="statusBar">StatusBar-Singleton – wird über <see cref="HasUnsavedChanges"/> informiert.</param>
        /// <param name="memorySink">
        /// Optionaler In-Memory-Log-Puffer. Ist er <see langword="null"/>, wird der
        /// Log-Viewer-Abschnitt in der UI deaktiviert.
        /// </param>
        public SettingsViewModel(
            IServiceScopeFactory scopeFactory,
            IThemeService themeService,
            ISyncService syncService,
            IErrorDialogService errorDialogService,
            IEpisodePatternAnalyzer patternAnalyzer,
            LoggerManager loggerManager,
            StatusBarViewModel statusBar,
            MemorySink? memorySink = null)
        {
            _scopeFactory       = scopeFactory;
            _themeService       = themeService;
            _syncService        = syncService;
            _errorDialogService = errorDialogService;
            _patternAnalyzer    = patternAnalyzer;
            _loggerManager      = loggerManager;
            _statusBar          = statusBar;
            _memorySink         = memorySink;
            LogEntries          = [];

            _testConnectionCommand = new RelayCommand(() => _ = TestConnectionAsync());
        }

        /// <summary>Name des aktiven Farbthemas.</summary>
        public string ActiveTheme
        {
            get => _activeTheme;
            set
            {
                if (SetProperty(ref _activeTheme, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// BCP-47-Sprachcode der aktiven Benutzeroberflächen-Sprache, z.B. <c>"de"</c> oder <c>"en"</c>.
        /// </summary>
        public string ActiveLanguage
        {
            get => _activeLanguage;
            set
            {
                if (SetProperty(ref _activeLanguage, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Liste der verfügbaren Sprachen für die UI-Auswahl.
        /// Wird einmalig beim Start der Klasse initialisiert – keine Datenbankabfrage nötig.
        /// </summary>
        public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
        [
            new("de", "Deutsch"),
            new("en", "English")
        ];

        /// <summary>Aktiver Metadaten-Anbieter.</summary>
        public ProviderType ActiveProvider
        {
            get => _activeProvider;
            set
            {
                if (SetProperty(ref _activeProvider, value))
                {
                    // Testbutton sofort reagieren lassen – bei "Keine" ist kein Test sinnvoll
                    _testConnectionCommand.SetEnabled(!_isTestingConnection && value != ProviderType.None);
                    MarkAsChanged();
                }
            }
        }

        /// <summary>Gibt an, ob die lokale Bibliothek aktiv ist.</summary>
        public bool LocalLibraryEnabled
        {
            get => _localLibraryEnabled;
            set
            {
                if (SetProperty(ref _localLibraryEnabled, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Pfad zur lokalen Bibliothek.
        /// Gibt <see cref="string.Empty"/> zurück, wenn kein Pfad gesetzt ist.
        /// Leere oder nur aus Leerzeichen bestehende Eingaben werden als null gespeichert.
        /// Aktualisiert <see cref="IsSyncEnabled"/>, da der Sync ohne Pfad blockiert ist.
        /// </summary>
        public string LocalLibraryRootPath
        {
            get => _localLibraryRootPath ?? string.Empty;
            set
            {
                if (SetProperty(ref _localLibraryRootPath, string.IsNullOrWhiteSpace(value) ? null : value))
                {
                    OnPropertyChanged(nameof(IsSyncEnabled));
                    MarkAsChanged();
                }
            }
        }

        /// <summary>Muster für die Benennung von Episodenordnern.</summary>
        public string EpisodeFolderPattern
        {
            get => _episodeFolderPattern;
            set
            {
                if (SetProperty(ref _episodeFolderPattern, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>Gibt an, ob gerade ein Ladevorgang läuft.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        /// <summary>Gibt an, ob gerade ein Sync-Vorgang läuft.</summary>
        public bool IsSyncing
        {
            get => _isSyncing;
            private set
            {
                if (SetProperty(ref _isSyncing, value))
                {
                    OnPropertyChanged(nameof(IsSyncEnabled));
                }
            }
        }

        /// <summary>
        /// Gibt an, ob der Sync-Button aktiviert werden soll.
        /// Falsch während ein Sync läuft oder solange kein Bibliothekspfad konfiguriert ist.
        /// Ein leerer Pfad würde den Sync sofort lautlos abbrechen – der Button signalisiert das vorab.
        /// </summary>
        public bool IsSyncEnabled => !_isSyncing && !string.IsNullOrWhiteSpace(_localLibraryRootPath);

        /// <summary>
        /// Statustext des letzten oder laufenden Sync-Vorgangs.
        /// Leer solange noch kein Sync ausgelöst wurde.
        /// </summary>
        public string SyncStatusText
        {
            get => _syncStatusText;
            private set => SetProperty(ref _syncStatusText, value);
        }

        /// <summary>
        /// Schwellwert für automatische Übernahme eines Musters ohne Nutzerrückfrage.
        /// Liegt die Trefferquote bei mindestens 85 %, ist das Ergebnis eindeutig genug.
        /// </summary>
        private const double HighConfidenceThreshold = 0.85;

        // ── Mustererkennung (Lokal-Tab) ──────────────────────────────────────────

        /// <summary>
        /// Wird ausgelöst, wenn mehrere Muster-Vorschläge vorliegen oder das beste Ergebnis
        /// unter dem Konfidenz-Schwellwert liegt. Die Page zeigt dann einen Auswahl-Dialog.
        /// </summary>
        public event Action<IReadOnlyList<PatternSuggestion>>? PatternSelectionRequested;

        /// <summary>
        /// Vorschläge für das Episodenordner-Muster, ermittelt durch den <see cref="IEpisodePatternAnalyzer"/>.
        /// Leer solange noch keine Analyse durchgeführt wurde oder kein Muster passt.
        /// </summary>
        public IReadOnlyList<PatternSuggestion> PatternSuggestions
        {
            get => _patternSuggestions;
            private set
            {
                if (SetProperty(ref _patternSuggestions, value))
                {
                    OnPropertyChanged(nameof(PatternSuggestionsVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit der Vorschlagsliste – nur wenn mindestens ein Vorschlag vorhanden ist.
        /// </summary>
        public Visibility PatternSuggestionsVisibility =>
            _patternSuggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Übernimmt den angegebenen Vorschlag als aktives Episodenmuster.
        /// </summary>
        /// <param name="pattern">Das zu übernehmende Muster-Pattern.</param>
        public void ApplyPatternSuggestion(string pattern)
        {
            EpisodeFolderPattern = pattern;
        }

        /// <summary>
        /// Analysiert den konfigurierten Bibliotheksordner und befüllt <see cref="PatternSuggestions"/>.
        /// Ist kein Pfad gesetzt, wird die Vorschlagsliste geleert.
        /// Bei einem einzigen Treffer mit hoher Konfidenz wird das Muster direkt übernommen.
        /// Andernfalls wird <see cref="PatternSelectionRequested"/> ausgelöst, damit die Page
        /// einen Auswahl-Dialog anzeigen kann.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task AnalyzePatternAsync()
        {
            if (string.IsNullOrWhiteSpace(_localLibraryRootPath))
            {
                PatternSuggestions = [];
                return;
            }

            IReadOnlyList<PatternSuggestion> suggestions = await _patternAnalyzer.AnalyzeAsync(_localLibraryRootPath);
            PatternSuggestions = suggestions;

            if (suggestions.Count == 1 && suggestions[0].MatchPercentage >= HighConfidenceThreshold)
            {
                // Eindeutiges Ergebnis – direkt übernehmen, kein Dialog nötig
                ApplyPatternSuggestion(suggestions[0].Pattern);
            }
            else if (suggestions.Count > 0)
            {
                // Mehrere Treffer oder unsicheres Ergebnis – Nutzer entscheiden lassen
                PatternSelectionRequested?.Invoke(suggestions);
            }
        }

        // ── Verbindungstest (Online-Tab) ─────────────────────────────────────────

        /// <summary>
        /// Gibt an, ob gerade ein Verbindungstest läuft.
        /// Während des Tests ist der Testbutton deaktiviert.
        /// </summary>
        public bool IsTestingConnection
        {
            get => _isTestingConnection;
            private set
            {
                if (SetProperty(ref _isTestingConnection, value))
                {
                    // Button über RelayCommand steuern – bei "Keine" bleibt er dauerhaft deaktiviert
                    _testConnectionCommand.SetEnabled(!value && _activeProvider != ProviderType.None);
                }
            }
        }

        /// <summary>
        /// Statustext des letzten Verbindungstests.
        /// Null, solange noch kein Test durchgeführt wurde.
        /// </summary>
        public string? ConnectionTestResultText
        {
            get => _connectionTestResultText;
            private set => SetProperty(ref _connectionTestResultText, value);
        }

        /// <summary>
        /// Ergebnis des letzten Verbindungstests.
        /// <see langword="null"/> = kein Test durchgeführt,
        /// <see langword="true"/> = Verbindung erfolgreich,
        /// <see langword="false"/> = Verbindung fehlgeschlagen.
        /// </summary>
        public bool? ConnectionTestSuccess
        {
            get => _connectionTestSuccess;
            private set
            {
                if (SetProperty(ref _connectionTestSuccess, value))
                {
                    OnPropertyChanged(nameof(ConnectionTestSuccessVisibility));
                    OnPropertyChanged(nameof(ConnectionTestFailureVisibility));
                    OnPropertyChanged(nameof(ConnectionTestResultVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Erfolgs-Icons beim Verbindungstest.
        /// Eingeblendet wenn <see cref="ConnectionTestSuccess"/> <see langword="true"/> ist.
        /// </summary>
        public Visibility ConnectionTestSuccessVisibility =>
            _connectionTestSuccess == true ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des Fehler-Icons beim Verbindungstest.
        /// Eingeblendet wenn <see cref="ConnectionTestSuccess"/> <see langword="false"/> ist.
        /// </summary>
        public Visibility ConnectionTestFailureVisibility =>
            _connectionTestSuccess == false ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des gesamten Ergebnisbereichs.
        /// Eingeblendet sobald mindestens ein Test durchgeführt wurde.
        /// </summary>
        public Visibility ConnectionTestResultVisibility =>
            _connectionTestSuccess.HasValue ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Befehl zum Starten des Verbindungstests gegen den aktiven Provider.
        /// Während der Test läuft, ist das Command deaktiviert.
        /// </summary>
        public ICommand TestConnectionCommand => _testConnectionCommand;

        // ── Laden und Speichern ──────────────────────────────────────────────────

        /// <summary>
        /// Lädt die aktuellen Einstellungen aus der Datenbank.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task LoadAsync()
        {
            IsLoading = true;
            _isBatchLoadingSettings = true;

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
                AppSettings settings = await settingsService.GetAsync();
                _loadedSettings = settings;

                // Felder einzeln setzen, damit PropertyChanged die x:Bind-Bindungen aktualisiert
                ActiveTheme          = settings.ActiveTheme;
                ActiveLanguage       = settings.ActiveLanguage;
                ActiveProvider       = settings.ActiveProvider;
                LocalLibraryEnabled  = settings.LocalLibraryEnabled;
                LocalLibraryRootPath = settings.LocalLibraryRootPath ?? string.Empty;
                EpisodeFolderPattern  = settings.EpisodeFolderPattern;
                LogRetentionDays  = settings.LogRetentionDays;
                MinimumLogLevel   = settings.MinimumLogLevel;
                AutoImportAfterScan   = settings.AutoImportAfterScan;
                NewReleaseDays        = settings.NewReleaseDays;
                OfflineMode           = settings.OfflineMode;
                OnlineOnlyMode        = settings.OnlineOnlyMode;
                DbPurgeDays           = settings.DbPurgeDays;
                ClearCacheOnNextStart = settings.ClearCacheOnNextStart;

                // Log-Dateien asynchron laden – darf ruhig parallel zur restlichen Initialisierung laufen
                await LoadLogFilesAsync();
            }
            finally
            {
                _isBatchLoadingSettings = false;
                HasUnsavedChanges = false;
                IsLoading = false;
            }
        }

        /// <summary>
        /// Speichert alle Einstellungsfelder dauerhaft in der Datenbank.
        /// Ohne vorherigen <see cref="LoadAsync"/>-Aufruf wird nichts gespeichert,
        /// um versehentliches Überschreiben vorhandener Daten zu vermeiden.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task SaveAsync()
        {
            if (_loadedSettings is null)
            {
                return;
            }

            _loadedSettings.ActiveTheme          = ActiveTheme;
            _loadedSettings.ActiveLanguage       = ActiveLanguage;
            _loadedSettings.ActiveProvider       = ActiveProvider;
            _loadedSettings.LocalLibraryEnabled  = LocalLibraryEnabled;
            _loadedSettings.LocalLibraryRootPath = string.IsNullOrWhiteSpace(LocalLibraryRootPath) ? null : LocalLibraryRootPath;
            _loadedSettings.EpisodeFolderPattern  = EpisodeFolderPattern;
            // Mindestwert 1 sicherstellen – 0 Tage würde alle Logs sofort löschen
            _loadedSettings.LogRetentionDays  = Math.Max(1, LogRetentionDays);
            _loadedSettings.MinimumLogLevel   = MinimumLogLevel;
            _loadedSettings.AutoImportAfterScan   = AutoImportAfterScan;
            // Mindestwert 7, Maximum 365 – unter einer Woche sind Neuerscheinungen kaum sinnvoll
            _loadedSettings.NewReleaseDays        = Math.Clamp(NewReleaseDays, 7, 365);
            _loadedSettings.OfflineMode           = OfflineMode;
            _loadedSettings.OnlineOnlyMode        = OnlineOnlyMode;
            // 0 ist erlaubt – bedeutet sofortige Bereinigung aller soft-gelöschten Einträge
            _loadedSettings.DbPurgeDays           = Math.Max(0, DbPurgeDays);
            _loadedSettings.ClearCacheOnNextStart = ClearCacheOnNextStart;

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
            if (!OfflineMode && ActiveProvider == Data.Entities.Settings.ProviderType.None)
            {
                await _errorDialogService.ShowAsync(
                    GetLocalizedString("NoProviderHintTitle", "Kein Provider"),
                    GetLocalizedString("NoProviderHintMessage", "Kein Online-Provider konfiguriert."));
            }
        }

        /// <summary>
        /// Wendet ein neues Theme sofort live an und merkt es für <see cref="SaveAsync"/> vor.
        /// Die Persistenz übernimmt der <see cref="ThemeService"/> intern.
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
        /// <param name="languageCode">Der BCP-47-Sprachcode der gewählten Sprache, z.B. <c>"en"</c>.</param>
        /// <returns>Asynchrone Ausführung bis zum Neustart.</returns>
        public async Task ChangeLanguageAsync(string languageCode)
        {
            if (_loadedSettings is null)
            {
                return;
            }

            // Alle aktuellen ViewModel-Werte übernehmen, damit beim Neustart der vollständige Zustand geladen wird
            _loadedSettings.ActiveTheme          = ActiveTheme;
            _loadedSettings.ActiveLanguage       = languageCode;
            _loadedSettings.ActiveProvider       = ActiveProvider;
            _loadedSettings.LocalLibraryEnabled  = LocalLibraryEnabled;
            _loadedSettings.LocalLibraryRootPath = string.IsNullOrWhiteSpace(LocalLibraryRootPath) ? null : LocalLibraryRootPath;
            _loadedSettings.EpisodeFolderPattern  = EpisodeFolderPattern;
            _loadedSettings.LogRetentionDays  = Math.Max(1, LogRetentionDays);
            _loadedSettings.MinimumLogLevel   = MinimumLogLevel;
            _loadedSettings.AutoImportAfterScan   = AutoImportAfterScan;
            _loadedSettings.OfflineMode           = OfflineMode;
            _loadedSettings.OnlineOnlyMode        = OnlineOnlyMode;
            _loadedSettings.DbPurgeDays           = Math.Max(0, DbPurgeDays);
            _loadedSettings.ClearCacheOnNextStart = ClearCacheOnNextStart;

            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            await settingsService.SaveAsync(_loadedSettings);

            // PrimaryLanguageOverride muss vor dem Neustart gesetzt werden –
            // Windows App Runtime liest die Sprache beim Start und kann sie danach nicht wechseln
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageCode;

            // Neustart des MSIX-Pakets – danach lädt die App alle .resw-Ressourcen in der neuen Sprache
            Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
        }

        /// <summary>
        /// Startet den Sync der lokalen Bibliothek mit der Datenbank.
        /// Aktualisiert <see cref="SyncStatusText"/> während des Vorgangs und zeigt das Ergebnis an.
        /// Läuft ein Sync bereits, wird der Aufruf ignoriert.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task SyncAsync()
        {
            if (IsSyncing)
            {
                return;
            }

            // Vor dem Start prüfen – SyncService würde sonst lautlos ein leeres SyncResult zurückgeben,
            // was für den Nutzer wie ein Fehler aussieht, ohne den Grund zu nennen
            if (!LocalLibraryEnabled)
            {
                SyncStatusText = GetLocalizedString("SyncDisabledHint", "Lokale Bibliothek ist deaktiviert.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_localLibraryRootPath))
            {
                SyncStatusText = GetLocalizedString("SyncNoPathHint", "Kein Bibliotheksordner konfiguriert.");
                return;
            }

            IsSyncing = true;
            SyncStatusText = GetLocalizedString("SyncRunning", "Sync läuft …");

            try
            {
                Progress<ScanProgress> progress = new(p => SyncStatusText = p.StatusText);
                SyncResult result = await _syncService.SyncAsync(progress);
                SyncStatusText = result.ToString();
            }
            catch (Exception ex)
            {
                string syncFailed = GetLocalizedString("SyncFailed", "Sync fehlgeschlagen");
                SyncStatusText = $"{syncFailed}: {ex.Message}";
                await _errorDialogService.ShowAsync(syncFailed, ex.Message);
            }
            finally
            {
                IsSyncing = false;
            }
        }

        /// <summary>
        /// Testet die Verbindung zum aktiven Metadaten-Anbieter mit einer minimalen API-Anfrage.
        /// Setzt <see cref="ConnectionTestSuccess"/> und <see cref="ConnectionTestResultText"/> als Ergebnis.
        /// Läuft bereits ein Test, wird der Aufruf ignoriert.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task TestConnectionAsync()
        {
            if (_isTestingConnection || _activeProvider == ProviderType.None)
            {
                return;
            }

            IsTestingConnection   = true;
            ConnectionTestSuccess = null;
            ConnectionTestResultText = null;

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();

                if (ActiveProvider == ProviderType.Spotify)
                {
                    // Minimalanfrage – eine Künstlersuche reicht, um Token-Fluss und Netzwerk zu prüfen
                    ISpotifyApiClient client = scope.ServiceProvider.GetRequiredService<ISpotifyApiClient>();
                    await client.SearchArtistsAsync("test", 1);
                }
                else
                {
                    // iTunes Search API ist öffentlich – kein Token nötig, aber Netzwerk muss erreichbar sein
                    IAppleMusicSearchClient client = scope.ServiceProvider.GetRequiredService<IAppleMusicSearchClient>();
                    await client.SearchArtistsAsync("test", 1);
                }

                ConnectionTestSuccess    = true;
                ConnectionTestResultText = GetLocalizedString("ConnectionSuccess", "Verbindung erfolgreich");
            }
            catch (Exception ex)
            {
                ConnectionTestSuccess    = false;
                ConnectionTestResultText = $"{GetLocalizedString("ConnectionFailed", "Verbindung fehlgeschlagen")}: {ex.Message}";
            }
            finally
            {
                IsTestingConnection = false;
            }
        }

        // ── Log-Viewer ───────────────────────────────────────────────────────────

        /// <summary>
        /// Die gefilterten und formatierten Log-Einträge für den Log-Viewer.
        /// Leer wenn kein MemorySink verfügbar ist oder noch kein Refresh durchgeführt wurde.
        /// </summary>
        public ObservableCollection<string> LogEntries { get; }

        /// <summary>
        /// Gibt an, ob der Log-Viewer verfügbar ist.
        /// Hängt davon ab, ob der MemorySink beim Start registriert wurde.
        /// </summary>
        public bool IsLogViewerAvailable => _memorySink is not null;

        /// <summary>
        /// Freitext-Suchfilter für den Log-Viewer.
        /// Jede Änderung löst sofort einen Refresh aus.
        /// </summary>
        public string LogSearchText
        {
            get => _logSearchText;
            set
            {
                if (SetProperty(ref _logSearchText, value))
                {
                    RefreshLogs();
                }
            }
        }

        /// <summary>
        /// Minimales Log-Level für den Log-Viewer-Filter.
        /// Jede Änderung löst sofort einen Refresh aus.
        /// </summary>
        public LogLevel LogMinimumLevel
        {
            get => _logMinimumLevel;
            set
            {
                if (SetProperty(ref _logMinimumLevel, value))
                {
                    RefreshLogs();
                }
            }
        }

        /// <summary>
        /// Index des ausgewählten Level-Filters in der ComboBox (0=Alle, 1=Info+, 2=Warnung+, 3=Fehler+).
        /// Ändert intern <see cref="LogMinimumLevel"/> und löst einen Refresh aus.
        /// </summary>
        public int LogLevelFilterIndex
        {
            get => _logMinimumLevel switch
            {
                LogLevel.Debug       => 0,
                LogLevel.Information => 1,
                LogLevel.Warning     => 2,
                _                    => 3
            };
            set => LogMinimumLevel = value switch
            {
                1    => LogLevel.Information,
                2    => LogLevel.Warning,
                3    => LogLevel.Error,
                _    => LogLevel.Debug
            };
        }

        /// <summary>
        /// Aufbewahrungszeit für Log-Dateien in Tagen.
        /// Werte unter 1 werden beim Speichern auf 1 begrenzt.
        /// </summary>
        public int LogRetentionDays
        {
            get => _logRetentionDays;
            set
            {
                if (SetProperty(ref _logRetentionDays, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Mindest-Log-Level das geschrieben werden soll.
        /// Einträge unterhalb dieses Levels landen weder in der Datei noch im Live-Puffer.
        /// Die Änderung wird beim Speichern sofort an den <see cref="LoggerManager"/> weitergegeben.
        /// </summary>
        public LogLevel MinimumLogLevel
        {
            get => _minimumLogLevel;
            set
            {
                if (SetProperty(ref _minimumLogLevel, value))
                {
                    OnPropertyChanged(nameof(MinimumLogLevelIndex));
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Index für die MinimumLogLevel-ComboBox (0=Trace, 1=Debug, 2=Information, 3=Warning, 4=Error).
        /// Mappt bidirektional auf <see cref="MinimumLogLevel"/>.
        /// </summary>
        public int MinimumLogLevelIndex
        {
            get => _minimumLogLevel switch
            {
                LogLevel.Trace       => 0,
                LogLevel.Debug       => 1,
                LogLevel.Information => 2,
                LogLevel.Warning     => 3,
                _                    => 4
            };
            set => MinimumLogLevel = value switch
            {
                0    => LogLevel.Trace,
                1    => LogLevel.Debug,
                3    => LogLevel.Warning,
                4    => LogLevel.Error,
                _    => LogLevel.Information
            };
        }

        /// <summary>
        /// Gibt an, ob erkannte Serien nach einem Scan automatisch importiert werden sollen.
        /// Bezieht sich auf <see cref="AppSettings.AutoImportAfterScan"/>.
        /// </summary>
        public bool AutoImportAfterScan
        {
            get => _autoImportAfterScan;
            set
            {
                if (SetProperty(ref _autoImportAfterScan, value))
                {
                    MarkAsChanged();
                }
            }
        }

        // ── Neuerscheinungen (Allgemein-Tab) ────────────────────────────────────

        /// <summary>
        /// Zeitfenster in Tagen für den Neuerscheinungen-Filter auf dem Dashboard.
        /// Folgen mit Erscheinungsdatum innerhalb von <c>LastAppStart - NewReleaseDays</c>
        /// werden als Neuerscheinung angezeigt. Gültiger Bereich: 7–365 Tage.
        /// </summary>
        public int NewReleaseDays
        {
            get => _newReleaseDays;
            set
            {
                if (SetProperty(ref _newReleaseDays, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Offline-Modus: deaktiviert alle Online-Abfragen (iTunes, Serien-Suche).
        /// Neuerscheinungen werden ausgeblendet, lokale Mediathek funktioniert normal.
        /// </summary>
        public bool OfflineMode
        {
            get => _offlineMode;
            set
            {
                if (SetProperty(ref _offlineMode, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Nur-Online-Modus: blendet die lokale Mediathek aus.
        /// Für Nutzer ohne lokale Hörspielsammlung – nur die Online-Bibliothek ist sichtbar.
        /// </summary>
        public bool OnlineOnlyMode
        {
            get => _onlineOnlyMode;
            set
            {
                if (SetProperty(ref _onlineOnlyMode, value))
                {
                    MarkAsChanged();
                }
            }
        }

        // ── Datenbankpflege (Lokal-Tab) ──────────────────────────────────────────

        /// <summary>
        /// Anzahl der Tage nach denen soft-gelöschte Einträge physisch aus der Datenbank entfernt werden.
        /// 0 bedeutet sofortige Bereinigung. Standard: 30 Tage.
        /// Wird als Teil der Einstellungen gespeichert.
        /// </summary>
        public int DbPurgeDays
        {
            get => _dbPurgeDays;
            set
            {
                if (SetProperty(ref _dbPurgeDays, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Wenn aktiviert, wird der Neuerscheinungen-Cache beim nächsten App-Start
        /// vollständig geleert und neu aufgebaut. Nützlich nach Datenänderungen
        /// oder wenn die Anzeige nicht mehr aktuell ist.
        /// </summary>
        public bool ClearCacheOnNextStart
        {
            get => _clearCacheOnNextStart;
            set
            {
                if (SetProperty(ref _clearCacheOnNextStart, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Gibt an, ob gerade ein Datenbankpflege-Vorgang läuft.
        /// Während der Bereinigung sind die Wartungsbuttons deaktiviert.
        /// </summary>
        public bool IsMaintaining
        {
            get => _isMaintaining;
            private set
            {
                if (SetProperty(ref _isMaintaining, value))
                {
                    OnPropertyChanged(nameof(IsNotMaintaining));
                }
            }
        }

        /// <summary>
        /// Invertierter Wartungszustand für die Button-<c>IsEnabled</c>-Bindung.
        /// WinUI 3 hat keinen eingebauten BoolNegation-Converter – dieses Property ersetzt ihn.
        /// </summary>
        public bool IsNotMaintaining => !_isMaintaining;

        /// <summary>
        /// Statustext des letzten oder laufenden Wartungsvorgangs.
        /// Leer solange noch keine Wartung ausgelöst wurde.
        /// </summary>
        public string MaintenanceStatusText
        {
            get => _maintenanceStatusText;
            private set
            {
                if (SetProperty(ref _maintenanceStatusText, value))
                {
                    OnPropertyChanged(nameof(MaintenanceStatusVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Wartungs-Statustexts.
        /// Eingeblendet sobald der erste Wartungsvorgang gestartet oder abgeschlossen wurde.
        /// </summary>
        public Visibility MaintenanceStatusVisibility =>
            string.IsNullOrEmpty(_maintenanceStatusText) ? Visibility.Collapsed : Visibility.Visible;

        // ── Änderungserkennung ──────────────────────────────────────────────────

        /// <summary>
        /// Gibt an, ob der Nutzer Einstellungen geändert hat, die noch nicht gespeichert wurden.
        /// Wird automatisch auf <c>true</c> gesetzt, sobald ein Einstellungs-Property geändert wird.
        /// Nach <see cref="SaveAsync"/> und <see cref="LoadAsync"/> wird der Wert zurückgesetzt.
        /// Das initiale Laden über <see cref="LoadAsync"/> markiert keine Änderung.
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

        /// <summary>
        /// Markiert eine Nutzeränderung – aber nur wenn gerade kein Batch-Laden läuft.
        /// Wird von den Einstellungs-Settern aufgerufen, um <see cref="HasUnsavedChanges"/> zu aktualisieren.
        /// </summary>
        private void MarkAsChanged()
        {
            if (!_isBatchLoadingSettings)
            {
                HasUnsavedChanges = true;
            }
        }

        // ── Log-Datei-Auswahl ────────────────────────────────────────────────────

        /// <summary>
        /// Liste aller verfügbaren Log-Dateien, absteigend nach Datum sortiert.
        /// An erster Stelle steht immer die "Aktuell (Live)"-Option (MemorySink-Puffer).
        /// </summary>
        public IReadOnlyList<LogFileOption> AvailableLogFiles
        {
            get => _availableLogFiles;
            private set => SetProperty(ref _availableLogFiles, value);
        }

        /// <summary>
        /// Die aktuell gewählte Log-Datei.
        /// <see cref="LogFileOption.FilePath"/> ist <see langword="null"/> im Live-Modus.
        /// Bei Auswahl einer Datei wird deren Inhalt direkt in <see cref="LogEntries"/> geladen.
        /// </summary>
        public LogFileOption? SelectedLogFile
        {
            get => _selectedLogFile;
            set
            {
                if (SetProperty(ref _selectedLogFile, value))
                {
                    _ = LoadLogContentAsync(value);
                }
            }
        }

        /// <summary>
        /// Liest alle .log-Dateien aus dem konfigurierten Log-Verzeichnis
        /// und befüllt <see cref="AvailableLogFiles"/> in absteigender Datumsreihenfolge.
        /// Der erste Eintrag ist immer die Live-Option.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task LoadLogFilesAsync()
        {
            // Relativen Pfad in absoluten umwandeln, da AppContext.BaseDirectory je nach Start-Kontext variiert
            string logDirectory = _loggerManager.LogDirectory;
            if (!Path.IsPathRooted(logDirectory))
            {
                logDirectory = Path.GetFullPath(logDirectory);
            }

            // Dateisystem-Zugriff in Thread-Pool auslagern, um den UI-Thread nicht zu blockieren
            List<LogFileOption> fileOptions = await Task.Run(() =>
            {
                List<LogFileOption> result = [];

                if (!Directory.Exists(logDirectory))
                {
                    return result;
                }

                foreach (string path in Directory.GetFiles(logDirectory, "*.log"))
                {
                    string fileName = Path.GetFileName(path);
                    DateTime lastWrite = File.GetLastWriteTime(path);
                    result.Add(new LogFileOption(fileName, lastWrite, path));
                }

                // Absteigend sortieren – neueste Datei steht oben in der ComboBox
                result.Sort((a, b) => b.Date.CompareTo(a.Date));
                return result;
            });

            // Live-Option immer an erster Stelle, damit sie der Default bleibt
            List<LogFileOption> allOptions = [new LogFileOption("Aktuell (Live)", DateTime.MaxValue, null), ..fileOptions];
            AvailableLogFiles = allOptions;

            // Nur beim ersten Laden auf Live setzen – danach Auswahl des Nutzers beibehalten
            if (_selectedLogFile is null && allOptions.Count > 0)
            {
                _selectedLogFile = allOptions[0];
                OnPropertyChanged(nameof(SelectedLogFile));
            }
        }

        /// <summary>
        /// Lädt den Inhalt der gewählten Log-Datei in <see cref="LogEntries"/>.
        /// Die Live-Option (<see cref="LogFileOption.FilePath"/> ist <see langword="null"/>)
        /// zeigt den MemorySink-Puffer – identisch mit dem normalen <see cref="RefreshLogs"/>.
        /// </summary>
        /// <param name="option">Die gewählte Option. <see langword="null"/> bedeutet Live-Modus.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        private async Task LoadLogContentAsync(LogFileOption? option)
        {
            if (option?.FilePath is null)
            {
                // Live-Modus: aus dem MemorySink-Puffer laden
                RefreshLogs();
                return;
            }

            LogEntries.Clear();

            try
            {
                string[] lines = await File.ReadAllLinesAsync(option.FilePath, System.Text.Encoding.UTF8);

                foreach (string line in lines)
                {
                    LogEntries.Add(line);
                }
            }
            catch (IOException)
            {
                // Datei möglicherweise gerade gesperrt – stilles Ignorieren, leere Liste bleibt
            }
        }

        // ── Live-Ansicht ─────────────────────────────────────────────────────────

        /// <summary>
        /// Gibt an, ob der Log-Viewer alle 2 Sekunden automatisch aktualisiert wird.
        /// Startet oder stoppt den internen <see cref="DispatcherTimer"/> entsprechend.
        /// Nur im Live-Modus sinnvoll (wenn keine Datei gewählt ist).
        /// </summary>
        public bool IsLiveViewActive
        {
            get => _isLiveViewActive;
            set
            {
                if (SetProperty(ref _isLiveViewActive, value))
                {
                    if (value)
                    {
                        StartLiveViewTimer();
                    }
                    else
                    {
                        _liveViewTimer?.Stop();
                    }
                }
            }
        }

        /// <summary>
        /// Erstellt beim ersten Aufruf den <see cref="DispatcherTimer"/> und startet ihn.
        /// Der Timer muss im UI-Thread erstellt werden – das ViewModel wird immer dort instanziiert.
        /// </summary>
        private void StartLiveViewTimer()
        {
            if (_liveViewTimer is null)
            {
                _liveViewTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _liveViewTimer.Tick += OnLiveViewTimerTick;
            }

            _liveViewTimer.Start();
        }

        /// <summary>
        /// Tick-Handler des Live-View-Timers – lädt den MemorySink-Puffer neu.
        /// Ist eine Datei gewählt, passiert nichts – historische Logs ändern sich nicht.
        /// </summary>
        /// <param name="sender">Der auslösende Timer.</param>
        /// <param name="e">Ereignisargumente (ungenutzt).</param>
        private void OnLiveViewTimerTick(object? sender, object e)
        {
            // Nur im Live-Modus aktualisieren – Datei-Logs sind statisch
            if (_selectedLogFile?.FilePath is null)
            {
                RefreshLogs();
            }
        }

        /// <summary>
        /// Liest alle gepufferten Einträge aus dem MemorySink und wendet Suchtext- und Level-Filter an.
        /// Neueste Einträge stehen am Ende der Liste.
        /// </summary>
        public void RefreshLogs()
        {
            LogEntries.Clear();

            if (_memorySink is null)
            {
                return;
            }

            IReadOnlyList<LogEntry> entries = _memorySink.GetEntries();

            foreach (LogEntry entry in entries)
            {
                // Level-Filter: Einträge unterhalb des Mindest-Levels werden übersprungen
                if (entry.Level < _logMinimumLevel)
                {
                    continue;
                }

                string line = FormatLogEntry(entry);

                // Suchfilter: Groß-/Kleinschreibung ignorieren
                if (!string.IsNullOrWhiteSpace(_logSearchText)
                    && !line.Contains(_logSearchText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                LogEntries.Add(line);
            }
        }

        /// <summary>
        /// Formatiert einen <see cref="LogEntry"/> für die einzeilige Darstellung im Log-Viewer.
        /// </summary>
        /// <param name="entry">Der zu formatierende Eintrag.</param>
        /// <returns>Formatierter String im Format <c>HH:mm:ss [LEVEL] Kategorie: Nachricht</c>.</returns>
        private static string FormatLogEntry(LogEntry entry)
        {
            string levelTag = entry.Level switch
            {
                LogLevel.Trace       => "TRACE",
                LogLevel.Debug       => "DEBUG",
                LogLevel.Information => "INFO ",
                LogLevel.Warning     => "WARN ",
                LogLevel.Error       => "ERROR",
                LogLevel.Fatal       => "FATAL",
                _                    => "????? "
            };

            string time = entry.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            string line = $"{time} [{levelTag}] {entry.Category}: {entry.Message}";

            if (entry.Exception is not null)
            {
                line += $"  ({entry.Exception.GetType().Name}: {entry.Exception.Message})";
            }

            return line;
        }

        // ── Datenbankpflege-Aktionen ─────────────────────────────────────────────

        /// <summary>
        /// Bereinigt soft-gelöschte Einträge die älter als <see cref="DbPurgeDays"/> Tage sind
        /// und kompaktiert anschließend die SQLite-Datei mit VACUUM.
        /// Läuft bereits eine Wartung, wird der Aufruf ignoriert.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task RunMaintenanceAsync()
        {
            if (_isMaintaining)
            {
                return;
            }

            IsMaintaining       = true;
            MaintenanceStatusText = "Bereinigung läuft …";

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IDatabaseMaintenanceService maintenance =
                    scope.ServiceProvider.GetRequiredService<IDatabaseMaintenanceService>();

                await maintenance.PurgeAsync(Math.Max(0, DbPurgeDays));
                await maintenance.VacuumAsync();

                MaintenanceStatusText = "Datenbank erfolgreich bereinigt.";
            }
            catch (Exception ex)
            {
                MaintenanceStatusText = $"Fehler bei der Bereinigung: {ex.Message}";
            }
            finally
            {
                IsMaintaining = false;
            }
        }

        /// <summary>
        /// Löscht alle Bibliotheksdaten (Serien, Episoden, Wiedergabestände, lokale Tracks)
        /// aus der Datenbank. Einstellungen bleiben erhalten.
        /// Die Sicherheitsabfrage muss durch die aufrufende Page erfolgen.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        /// <summary>
        /// Setzt die Bibliothek je nach Scope zurück.
        /// 0 = Online (nur online-importierte Serien), 1 = Lokal (nur lokale Verknüpfungen),
        /// 2 = Alle (kompletter Reset).
        /// </summary>
        /// <param name="scopeIndex">0 = Online, 1 = Lokal, 2 = Alle.</param>
        public async Task ResetLibraryAsync(int scopeIndex)
        {
            IsMaintaining = true;
            MaintenanceStatusText = GetLocalizedString("ResetRunning", "Bibliothek wird zurückgesetzt …");

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IDatabaseMaintenanceService maintenance =
                    scope.ServiceProvider.GetRequiredService<IDatabaseMaintenanceService>();

                switch (scopeIndex)
                {
                    case 0:
                        await maintenance.ClearOnlineLibraryAsync();
                        MaintenanceStatusText = GetLocalizedString("ResetOnlineDone", "Online-Bibliothek wurde zurückgesetzt.");
                        break;
                    case 1:
                        await maintenance.ClearLocalLibraryAsync();
                        MaintenanceStatusText = GetLocalizedString("ResetLocalDone", "Lokale Bibliothek wurde zurückgesetzt.");
                        break;
                    default:
                        await maintenance.ClearLibraryAsync();
                        MaintenanceStatusText = GetLocalizedString("ResetAllDone", "Gesamte Bibliothek wurde zurückgesetzt.");
                        break;
                }
            }
            catch (Exception ex)
            {
                MaintenanceStatusText = $"{GetLocalizedString("SyncFailed", "Fehler")}: {ex.Message}";
            }
            finally
            {
                IsMaintaining = false;
            }
        }

        /// <summary>
        /// Öffnet einen System-FolderPicker und setzt den Bibliothekspfad auf die gewählte Auswahl.
        /// Bricht der Benutzer ab, bleibt der bisherige Pfad erhalten.
        /// </summary>
        /// <param name="windowHandle">
        /// HWND des Hauptfensters – der WinRT-FolderPicker muss zwingend
        /// mit <c>InitializeWithWindow</c> an ein Fenster gebunden werden.
        /// </param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task BrowseLibraryFolderAsync(nint windowHandle)
        {
            FolderPicker picker = new();

            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;

            // FileTypeFilter ist auch beim FolderPicker Pflicht – Wildcard genügt
            picker.FileTypeFilter.Add("*");

            StorageFolder? folder = await picker.PickSingleFolderAsync();

            if (folder is not null)
            {
                LocalLibraryRootPath = folder.Path;
            }
        }

        /// <summary>
        /// Stoppt den Live-View-Timer und gibt ihn frei.
        /// Wird von <see cref="EchoPlay.App.Pages.SettingsPage"/> beim Verlassen der Seite aufgerufen.
        /// </summary>
        public void Dispose()
        {
            if (_liveViewTimer is not null)
            {
                _liveViewTimer.Stop();
                _liveViewTimer.Tick -= OnLiveViewTimerTick;
                _liveViewTimer = null;
            }
        }

        /// <summary>
        /// Lädt einen lokalisierten String aus den Ressourcen.
        /// In Unit-Tests ohne WinUI-Runtime wird der Fallback zurückgegeben,
        /// da <c>ResourceLoader</c> einen nativen Crash verursachen kann.
        /// </summary>
        /// <param name="key">Der Resource-Key.</param>
        /// <param name="fallback">Fallback-Wert für Tests ohne WinUI-Runtime.</param>
        /// <returns>Der lokalisierte String oder der Fallback.</returns>
        private static string GetLocalizedString(string key, string fallback = "")
        {
            try
            {
                // Prüfung ob WinUI-Runtime verfügbar ist – in Tests gibt es keine App-Instanz.
                // Application.Current wirft in manchen Test-Hosts eine COMException.
                if (Microsoft.UI.Xaml.Application.Current is null)
                {
                    return fallback;
                }

                return ResourceLoader.GetForViewIndependentUse().GetString(key);
            }
            catch
            {
                // Nativer COM-Fehler oder fehlende WinUI-Runtime → Fallback
                return fallback;
            }
        }
    }
}

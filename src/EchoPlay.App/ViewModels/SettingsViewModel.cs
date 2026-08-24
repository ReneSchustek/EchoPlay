using EchoPlay.App.Helpers;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Core.Models;
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
    public sealed class SettingsViewModel : ObservableObject, IDisposable, INavigationGuard
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IThemeService _themeService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly ILocalizationService _localizationService;
        private readonly LoggerManager _loggerManager;
        private readonly StatusBarViewModel _statusBar;
        private readonly ILanguageSwitchService? _languageSwitchService;

        // Referenz auf die geladene Entität – nötig für SaveAsync, um den EF-Track nicht zu verlieren
        private AppSettings? _loadedSettings;

        private bool _isLoading;
        private bool _hasUnsavedChanges;

        /// <summary>
        /// Initialisiert das ViewModel und erzeugt die vier Sub-VMs mit den benötigten Abhängigkeiten.
        /// </summary>
        /// <param name="context">Bündelt alle per DI aufgelösten Dienste.</param>
        internal SettingsViewModel(SettingsViewModelContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            _languageSwitchService = context.LanguageSwitchService;
            _scopeFactory = context.ScopeFactory;
            _themeService = context.ThemeService;
            _errorDialogService = context.ErrorDialogService;
            _confirmationDialogService = context.ConfirmationDialogService;
            _localizationService = context.LocalizationService;
            _loggerManager = context.LoggerManager;
            _statusBar = context.StatusBar;

            // Sub-VMs mit gemeinsamem Edit-Callback – jede Nutzeränderung setzt HasUnsavedChanges
            GeneralVM = new GeneralSettingsViewModel(OnSubVmUserEdit);
            OnlineVM = new OnlineSettingsViewModel(
                context.ConnectionTestCoordinator, context.CredentialStore, context.OptionsProvider, OnSubVmUserEdit);
            LocalVM = new LocalSettingsViewModel(
                context.SyncService, context.ErrorDialogService, context.PatternAnalyzer, OnSubVmUserEdit);
            MaintenanceVM = new MaintenanceSettingsViewModel(
                context.ScopeFactory, context.LogViewerCoordinator, OnSubVmUserEdit);

            // Ohne Änderungs-Rückruf: Ausblenden und Zurückholen wirken sofort und hängen
            // nicht am gemeinsamen Speichern-Knopf der Seite.
            HiddenDialogsVM = new HiddenDialogsViewModel(
                context.SuppressionService, context.LocalizationService, context.LoggerFactory);
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

        /// <summary>Sub-VM für die Liste der dauerhaft ausgeblendeten Hinweise.</summary>
        public HiddenDialogsViewModel HiddenDialogsVM { get; }

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


        // ── Laden und Speichern ──────────────────────────────────────────────────

        /// <summary>
        /// Lädt die aktuellen Einstellungen aus der Datenbank und verteilt sie an die Sub-VMs.
        /// Das Laden markiert <see cref="HasUnsavedChanges"/> nicht als geändert.
        /// Zusätzlich werden die verfügbaren Log-Dateien neu eingelesen.
        /// </summary>
        /// <returns>Der Task ist abgeschlossen, wenn alle Sub-ViewModels befüllt sind.</returns>
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

                // Spotify-Verknüpfungsstatus aus dem Credential-Store laden
                OnlineVM.LoadSpotifyStatus();

                // Log-Dateien asynchron laden – darf ruhig parallel zur restlichen Initialisierung laufen
                await MaintenanceVM.LoadLogFilesAsync();

                await HiddenDialogsVM.LoadAsync();
            }
            finally
            {
                HasUnsavedChanges = false;
                IsLoading = false;
            }
        }

        /// <summary>
        /// Prüft, ob die Einstellungsseite verlassen werden darf. Bei ungespeicherten
        /// Änderungen wird der Nutzer gefragt, ob gespeichert werden soll. Navigiert wird
        /// in jedem Fall (bei „Nein" werden Änderungen verworfen) – lediglich der
        /// Speichern-Schritt ist optional.
        /// </summary>
        public async Task<bool> CanLeaveAsync()
        {
            if (!HasUnsavedChanges)
            {
                return true;
            }

            bool shouldSave = await _confirmationDialogService.ConfirmAsync(
                _localizationService.Get("UnsavedSettingsDialogTitle"),
                _localizationService.Get("UnsavedSettingsDialogMessage"),
                DialogKey.UnsavedSettings);

            if (shouldSave)
            {
                // SaveAsync aktualisiert intern die StatusBar – Provider- und Offline-Änderungen
                // wirken sich sofort auf die Nav-Leiste aus.
                await SaveAsync();
            }

            // Auch bei „Nein" darf navigiert werden – Änderungen werden dann verworfen
            return true;
        }

        /// <summary>
        /// Speichert alle Einstellungsfelder dauerhaft in der Datenbank.
        /// Ohne vorherigen <see cref="LoadAsync"/>-Aufruf wird nichts gespeichert, um versehentliches
        /// Überschreiben vorhandener Daten zu vermeiden.
        /// </summary>
        /// <returns>Der Task ist abgeschlossen, wenn alle Felder in der Datenbank stehen.</returns>
        public async Task SaveAsync()
        {
            if (_loadedSettings is null)
            {
                return;
            }

            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("SettingsSave");

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
            if (!GeneralVM.OfflineMode && OnlineVM.ActiveProvider == ProviderType.None)
            {
                await _errorDialogService.ShowAsync(
                    SafeResourceLoader.Get("NoProviderHintTitle", "Kein Provider"),
                    SafeResourceLoader.Get("NoProviderHintMessage", "Kein Online-Provider konfiguriert."),
                    DialogKey.NoProviderHint);
            }
        }

        /// <summary>
        /// Wendet ein neues Theme sofort live an und merkt es für <see cref="SaveAsync"/> vor.
        /// Die Persistenz übernimmt der <see cref="IThemeService"/> intern.
        /// </summary>
        /// <param name="themeName">Name des anzuwendenden Themes.</param>
        public void ApplyTheme(string themeName)
        {
            GeneralVM.ActiveTheme = themeName;
            _themeService.ApplyTheme(themeName);
        }

        /// <summary>
        /// Speichert alle Einstellungen und wechselt die Oberflächensprache.
        /// WinUI 3 kann Ressourcendateien nicht zur Laufzeit neu laden – der Neustart ist zwingend.
        /// Der Nutzer bestätigt ihn vorher, damit ungespeicherte Arbeit nicht überrascht verschwindet.
        /// </summary>
        /// <param name="languageCode">Der BCP-47-Sprachcode der gewählten Sprache.</param>
        /// <returns>Der Task ist abgeschlossen, wenn gespeichert wurde und der Neustart angestoßen ist.</returns>
        public async Task ChangeLanguageAsync(string languageCode)
        {
            if (_loadedSettings is null || string.IsNullOrWhiteSpace(languageCode))
            {
                return;
            }

            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("SettingsLanguageChange");

            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                _localizationService.Get("LanguageRestartTitle"),
                _localizationService.Get("LanguageRestartMessage"),
                DialogKey.LanguageRestart);

            if (!confirmed)
            {
                return;
            }

            // Sub-VM-Werte in die Entität schreiben, damit der Neustart keine offenen
            // Änderungen der Einstellungsseite verwirft.
            GeneralVM.WriteTo(_loadedSettings);
            OnlineVM.WriteTo(_loadedSettings);
            LocalVM.WriteTo(_loadedSettings);
            MaintenanceVM.WriteTo(_loadedSettings);
            _loadedSettings.ActiveLanguage = languageCode;

            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
                await settingsService.SaveAsync(_loadedSettings);
            }

            HasUnsavedChanges = false;

            if (_languageSwitchService is null)
            {
                return;
            }

            bool restarted = await _languageSwitchService.ChangeLanguageAsync(languageCode);

            if (!restarted)
            {
                // Sprache ist persistiert, nur der Neustart kam nicht zustande – der Nutzer
                // darf nicht im Glauben bleiben, die Auswahl sei verloren.
                await _errorDialogService.ShowAsync(
                    _localizationService.Get("LanguageRestartTitle"),
                    _localizationService.Get("LanguageRestartManualMessage"),
                    DialogKey.LanguageRestartManual);
            }
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
        public void Dispose() => MaintenanceVM.Dispose();
    }
}

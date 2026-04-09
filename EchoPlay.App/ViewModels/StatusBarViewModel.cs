using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;


namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die untere Info-Leiste des Hauptfensters.
    /// Zeigt Statistiken (abonnierte Serien, gehörte/offene/neue Folgen),
    /// ermöglicht den Theme-Wechsel per Flyout und den Sprachwechsel mit App-Neustart.
    ///
    /// Als Singleton registriert, damit die Statistiken App-weit konsistent sind
    /// und von anderen ViewModels per <see cref="RefreshAsync"/> aktualisiert werden können.
    /// </summary>
    public sealed class StatusBarViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IThemeService _themeService;
        private readonly TaskbarProgressService _taskbar;

        private int _subscribedSeriesCount;
        private int _finishedEpisodesCount;
        private int _unfinishedEpisodesCount;
        private int _newEpisodesCount;
        private string _activeTheme = "MidnightLibrary";
        private string _activeLanguage = "de";
        private string _activeProviderDisplay = string.Empty;
        private bool _isOnlineProviderActive = true;
        private bool _isOffline;
        private bool _hasUnsavedSettings;
        private bool _isTemporarilyOnline;
        private string _scanProgressText = string.Empty;
        private double _scanProgressValue;
        private bool _isScanActive;

        /// <summary>
        /// Initialisiert das ViewModel mit den benötigten Abhängigkeiten.
        /// </summary>
        /// <param name="scopeFactory">DI-Scope-Fabrik für Datenbankzugriffe.</param>
        /// <param name="themeService">Service für den Live-Themewechsel.</param>
        /// <param name="taskbar">Service für den Fortschrittsbalken in der Windows-Taskleiste.</param>
        public StatusBarViewModel(IServiceScopeFactory scopeFactory, IThemeService themeService, TaskbarProgressService taskbar)
        {
            _scopeFactory   = scopeFactory;
            _themeService   = themeService;
            _taskbar        = taskbar;

            // CommandParameter enthält den Theme-Namen bzw. den Sprachcode als string
            SwitchThemeCommand    = new ParameterizedRelayCommand(param => SwitchTheme(param as string ?? string.Empty));
            SwitchLanguageCommand = new ParameterizedRelayCommand(param => _ = ChangeLanguageAsync(param as string ?? string.Empty));
        }

        // ── Statistiken ──────────────────────────────────────────────────────────

        /// <summary>
        /// Anzahl der abonnierten Hörspielserien.
        /// </summary>
        public int SubscribedSeriesCount
        {
            get => _subscribedSeriesCount;
            private set
            {
                if (SetProperty(ref _subscribedSeriesCount, value))
                {
                    OnPropertyChanged(nameof(SubscribedSeriesText));
                }
            }
        }

        /// <summary>
        /// Anzahl der Episoden, die als vollständig gehört markiert wurden.
        /// </summary>
        public int FinishedEpisodesCount
        {
            get => _finishedEpisodesCount;
            private set
            {
                if (SetProperty(ref _finishedEpisodesCount, value))
                {
                    OnPropertyChanged(nameof(FinishedEpisodesText));
                }
            }
        }

        /// <summary>
        /// Anzahl der Episoden abonnierter Serien, die noch nicht vollständig gehört wurden.
        /// </summary>
        public int UnfinishedEpisodesCount
        {
            get => _unfinishedEpisodesCount;
            private set
            {
                if (SetProperty(ref _unfinishedEpisodesCount, value))
                {
                    OnPropertyChanged(nameof(UnfinishedEpisodesText));
                }
            }
        }

        /// <summary>
        /// Anzahl der Episoden, die bereits veröffentlicht (ReleaseDate ≤ heute) aber noch nicht gehört wurden.
        /// Gibt eine Orientierung, wie viel neues Material gerade verfügbar ist.
        /// </summary>
        public int NewEpisodesCount
        {
            get => _newEpisodesCount;
            private set
            {
                if (SetProperty(ref _newEpisodesCount, value))
                {
                    OnPropertyChanged(nameof(NewEpisodesText));
                    OnPropertyChanged(nameof(NewEpisodesVisibility));
                    OnPropertyChanged(nameof(NewEpisodesSeparatorVisibility));
                }
            }
        }

        // ── Berechnete Anzeige-Texte ─────────────────────────────────────────────

        /// <summary>Formatierter Anzeigetext für abonnierte Serien, z.B. "★ 12 Serien".</summary>
        public string SubscribedSeriesText => $"\u2605 {_subscribedSeriesCount} Serien";

        /// <summary>Formatierter Anzeigetext für gehörte Folgen, z.B. "✓ 234 gehört".</summary>
        public string FinishedEpisodesText => $"\u2713 {_finishedEpisodesCount} geh\u00f6rt";

        /// <summary>Formatierter Anzeigetext für offene Folgen, z.B. "○ 56 offen".</summary>
        public string UnfinishedEpisodesText => $"\u25cb {_unfinishedEpisodesCount} offen";

        /// <summary>Formatierter Anzeigetext für neue verfügbare Folgen, z.B. "🆕 3 neu".</summary>
        public string NewEpisodesText => $"\U0001f195 {_newEpisodesCount} neu";

        /// <summary>
        /// Sichtbarkeit des Neue-Folgen-Badges.
        /// Nur eingeblendet, wenn mindestens eine neue Folge verfügbar ist.
        /// </summary>
        public Visibility NewEpisodesVisibility =>
            _newEpisodesCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des Trennstrichs vor dem Neue-Folgen-Badge.
        /// Wird gemeinsam mit dem Badge ein- und ausgeblendet.
        /// </summary>
        public Visibility NewEpisodesSeparatorVisibility =>
            _newEpisodesCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        // ── Theme und Sprache ────────────────────────────────────────────────────

        /// <summary>
        /// Name des aktiven Farbthemas, z.B. "MidnightLibrary".
        /// </summary>
        public string ActiveTheme
        {
            get => _activeTheme;
            private set => SetProperty(ref _activeTheme, value);
        }

        /// <summary>
        /// BCP-47-Sprachcode der aktiven Sprache, z.B. "de" oder "en".
        /// </summary>
        public string ActiveLanguage
        {
            get => _activeLanguage;
            private set
            {
                if (SetProperty(ref _activeLanguage, value))
                {
                    OnPropertyChanged(nameof(ActiveLanguageDisplay));
                }
            }
        }

        /// <summary>
        /// Kurzbezeichnung der aktiven Sprache für die Info-Leiste, z.B. "DE" oder "EN".
        /// </summary>
        public string ActiveLanguageDisplay => _activeLanguage.ToUpperInvariant();

        /// <summary>
        /// Anzeigename des aktiven Metadaten-Anbieters für die Info-Leiste, z.B. "Spotify" oder "Apple Music".
        /// Leer solange noch kein Refresh durchgeführt wurde oder kein Provider gewählt ist.
        /// </summary>
        public string ActiveProviderDisplay
        {
            get => _activeProviderDisplay;
            private set => SetProperty(ref _activeProviderDisplay, value);
        }

        /// <summary>
        /// Sichtbarkeit des Menüeintrags "Online-Mediathek" in der NavigationView.
        /// Collapsed wenn kein Online-Anbieter konfiguriert ist (<see cref="ProviderType.None"/>).
        /// </summary>
        public Visibility OnlineMediathekVisibility =>
            _isOnlineProviderActive && !_isOffline ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Gibt an, ob der Offline-Modus aktiv ist.
        /// Steuert die Anzeige des Offline-Symbols in der Info-Leiste.
        /// </summary>
        public bool IsOffline
        {
            get => _isOffline;
            private set => SetProperty(ref _isOffline, value);
        }

        /// <summary>
        /// Temporärer Online-Status während einer einzelnen Aktion im Offline-Modus.
        /// Wird vom <see cref="Services.OnlineAccessGuard"/> gesetzt und nach Abschluss der Aktion
        /// automatisch zurückgesetzt. Ändert nur die visuelle Anzeige in der Info-Leiste –
        /// die <see cref="Data.Entities.Settings.AppSettings.OfflineMode"/>-Einstellung bleibt unberührt.
        /// </summary>
        public bool IsTemporarilyOnline
        {
            get => _isTemporarilyOnline;
            set
            {
                if (SetProperty(ref _isTemporarilyOnline, value))
                {
                    // Alle visuellen Online/Offline-Anzeigen aktualisieren
                    OnPropertyChanged(nameof(OfflineSymbolVisibility));
                    OnPropertyChanged(nameof(OnlineOfflineText));
                    OnPropertyChanged(nameof(OnlineOfflineGlyph));
                    OnPropertyChanged(nameof(OnlineOfflineBrush));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Offline-Symbols in der Info-Leiste.
        /// Visible wenn <see cref="IsOffline"/> aktiv ist.
        /// </summary>
        public Visibility OfflineSymbolVisibility =>
            _isOffline && !_isTemporarilyOnline ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Anzeigetext für den Online/Offline-Status in der Info-Leiste.
        /// "Online" wenn verbunden oder temporär online, "Offline" im Nur-lokal-Modus.
        /// </summary>
        public string OnlineOfflineText =>
            _isOffline && !_isTemporarilyOnline ? "Offline" : "Online";

        /// <summary>
        /// Glyph-Code für das Online/Offline-Symbol.
        /// Flugzeug-Icon (<c>E709</c>) für Offline, Wifi-Icon (<c>E701</c>) für Online.
        /// Temporärer Online-Status zeigt ebenfalls das Wifi-Icon.
        /// </summary>
        public string OnlineOfflineGlyph =>
            _isOffline && !_isTemporarilyOnline ? "\uE709" : "\uE701";

        /// <summary>
        /// Farbpinsel für das Online/Offline-Symbol und den zugehörigen Text.
        /// Grün (<c>#4CAF50</c>) wenn online oder temporär online, Grau (<c>TextFillColorSecondaryBrush</c>) wenn offline.
        /// Ein Brush statt Converter, weil das Projekt keine IValueConverter-Infrastruktur hat.
        /// </summary>
        public Brush OnlineOfflineBrush =>
            _isOffline && !_isTemporarilyOnline
                ? (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                : new SolidColorBrush(ColorHelper.FromArgb(255, 76, 175, 80));

        // ── Ungespeicherte Einstellungen ─────────────────────────────────────────

        /// <summary>
        /// Gibt an, ob auf der Einstellungsseite ungespeicherte Änderungen vorliegen.
        /// Wird vom <see cref="SettingsViewModel"/> gesetzt und steuert den roten Hinweistext
        /// in der Info-Leiste. Verschwindet nach Speichern oder Verwerfen der Änderungen.
        /// </summary>
        public bool HasUnsavedSettings
        {
            get => _hasUnsavedSettings;
            set
            {
                if (SetProperty(ref _hasUnsavedSettings, value))
                {
                    OnPropertyChanged(nameof(UnsavedSettingsVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Hinweistexts "Ungespeicherte Einstellungen" in der Info-Leiste.
        /// Visible nur solange <see cref="HasUnsavedSettings"/> aktiv ist.
        /// </summary>
        public Visibility UnsavedSettingsVisibility =>
            _hasUnsavedSettings ? Visibility.Visible : Visibility.Collapsed;

        // ── Scan-Fortschritt ─────────────────────────────────────────────────────

        /// <summary>
        /// Fortschrittstext des laufenden Bibliotheks-Scans, z.B. "Scanne TKKG …".
        /// Leer wenn kein Scan aktiv ist.
        /// </summary>
        public string ScanProgressText
        {
            get => _scanProgressText;
            private set => SetProperty(ref _scanProgressText, value);
        }

        /// <summary>
        /// Numerischer Fortschritt des laufenden Scans (0–100).
        /// 0 bedeutet, dass die Gesamtanzahl noch unbekannt ist → Balken indeterministisch.
        /// </summary>
        public double ScanProgressValue
        {
            get => _scanProgressValue;
            private set
            {
                if (SetProperty(ref _scanProgressValue, value))
                {
                    OnPropertyChanged(nameof(IsScanIndeterminate));
                }
            }
        }

        /// <summary>
        /// Gibt an, ob gerade ein Bibliotheks-Scan läuft.
        /// Steuert die Sichtbarkeit der Fortschrittsanzeige in der Info-Leiste.
        /// </summary>
        public bool IsScanActive
        {
            get => _isScanActive;
            private set
            {
                if (SetProperty(ref _isScanActive, value))
                {
                    OnPropertyChanged(nameof(ScanProgressVisibility));
                    OnPropertyChanged(nameof(IsScanIndeterminate));
                }
            }
        }

        /// <summary>
        /// Gibt an, ob der Fortschrittsbalken indeterministisch dargestellt werden soll.
        /// True wenn ein Scan läuft, aber die Gesamtanzahl der Dateien noch unbekannt ist (PercentComplete = 0).
        /// </summary>
        public bool IsScanIndeterminate => _isScanActive && _scanProgressValue <= 0;

        /// <summary>
        /// Sichtbarkeit der Scan-Fortschrittsanzeige in der Info-Leiste.
        /// Nur eingeblendet, wenn ein Scan aktiv ist.
        /// </summary>
        public Visibility ScanProgressVisibility =>
            _isScanActive ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Aktualisiert Text und numerischen Fortschritt des laufenden Scans.
        /// Wird aus <see cref="MediathekLokalViewModel"/> im Progress-Callback aufgerufen.
        /// Aktualisiert zusätzlich den Fortschrittsbalken im Taskleisten-Symbol.
        /// </summary>
        /// <param name="progress">Aktueller Scan-Fortschritt mit Text und Prozentwert.</param>
        public void UpdateScanProgress(ScanProgress progress)
        {
            ScanProgressText  = progress.StatusText;
            ScanProgressValue = progress.PercentComplete;
            IsScanActive      = true;

            // Taskleisten-Fortschritt: indeterministisch solange Gesamtanzahl unbekannt (0 %)
            if (progress.PercentComplete > 0)
            {
                _taskbar.SetProgress(progress.PercentComplete);
            }
            else
            {
                _taskbar.SetIndeterminate();
            }
        }

        /// <summary>
        /// Setzt den Fortschrittstext und aktiviert die Anzeige in der Info-Leiste.
        /// Ohne numerischen Fortschritt – der Balken läuft indeterministisch.
        /// Geeignet für Vorgänge, bei denen kein Prozentwert bekannt ist (z.B. Import-Fortschritt).
        /// </summary>
        /// <param name="text">Der anzuzeigende Fortschrittstext.</param>
        public void SetScanProgress(string text)
        {
            ScanProgressText  = text;
            ScanProgressValue = 0;
            IsScanActive      = true;
            _taskbar.SetIndeterminate();
        }

        /// <summary>
        /// Leert den Fortschrittstext und blendet die Anzeige aus.
        /// Wird nach Abschluss des Scans aufgerufen, damit die Leiste immer verschwindet.
        /// </summary>
        public void ClearScanProgress()
        {
            ScanProgressText  = string.Empty;
            ScanProgressValue = 0;
            IsScanActive      = false;
            _taskbar.Clear();
        }

        // ── Commands ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Wechselt das aktive Theme. CommandParameter muss ein Theme-Name (string) sein.
        /// </summary>
        public ICommand SwitchThemeCommand { get; }

        /// <summary>
        /// Wechselt die Sprache und startet die App neu. CommandParameter muss ein Sprachcode (string) sein.
        /// </summary>
        public ICommand SwitchLanguageCommand { get; }

        // ── Laden und Aktualisieren ───────────────────────────────────────────────

        /// <summary>
        /// Lädt alle Statistiken und die aktuellen Einstellungen aus der Datenbank.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public Task LoadAsync() => RefreshAsync();

        /// <summary>
        /// Aktualisiert die Statistiken und kann von anderen ViewModels nach Statusänderungen aufgerufen werden.
        /// Öffnet einen eigenen DI-Scope, um die Singleton-Lifetime mit den Scoped-Services zu vereinbaren.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task RefreshAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();

            ISeriesDataService          seriesService   = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IEpisodeDataService         episodeService  = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IPlaybackStateDataService   playbackService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();
            IAppSettingsDataService     settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();

            // Einstellungen laden – gibt Theme, Sprache und aktiven Provider für die Anzeige
            AppSettings settings = await settingsService.GetAsync();
            ActiveTheme           = settings.ActiveTheme;
            ActiveLanguage        = settings.ActiveLanguage;
            ActiveProviderDisplay = settings.ActiveProvider switch
            {
                ProviderType.Spotify    => "Spotify",
                ProviderType.AppleMusic => "Apple Music",
                _                      => string.Empty
            };

            // Sichtbarkeit der Online-Mediathek: nur einblenden wenn ein Provider gewählt ist
            bool providerActive = settings.ActiveProvider != ProviderType.None;
            if (_isOnlineProviderActive != providerActive)
            {
                _isOnlineProviderActive = providerActive;
                OnPropertyChanged(nameof(OnlineMediathekVisibility));
            }

            // Offline-Modus: Symbol, Text, Farbe + Menü-Sichtbarkeit aktualisieren
            if (_isOffline != settings.OfflineMode)
            {
                IsOffline = settings.OfflineMode;
                OnPropertyChanged(nameof(OfflineSymbolVisibility));
                OnPropertyChanged(nameof(OnlineMediathekVisibility));
                OnPropertyChanged(nameof(OnlineOfflineText));
                OnPropertyChanged(nameof(OnlineOfflineGlyph));
                OnPropertyChanged(nameof(OnlineOfflineBrush));
            }

            // Abonnierte Serien zählen und als Basis für Episode-Iteration nutzen
            IReadOnlyList<Series> subscribedSeries = await seriesService.GetSubscribedAsync();
            SubscribedSeriesCount = subscribedSeries.Count;

            // Alle Wiedergabestände einmalig laden – vermeidet N+1-Queries beim Episoden-Abgleich
            IReadOnlyList<PlaybackState> allStates = await playbackService.GetAllAsync();
            HashSet<Guid> completedEpisodeIds = allStates
                .Where(s => s.IsCompleted)
                .Select(s => s.EpisodeId)
                .ToHashSet();

            // Alle Episoden der abonnierten Serien in einem einzigen Query laden –
            // vorher war das ein N+1-Problem (ein Query pro Serie).
            List<Guid> seriesIds = subscribedSeries.Select(s => s.Id).ToList();
            IReadOnlyList<Episode> allEpisodes = await episodeService.GetBySeriesIdsAsync(seriesIds);

            // Statistiken über alle Episoden berechnen
            int finished   = 0;
            int unfinished = 0;
            int newCount   = 0;
            DateTime today = DateTime.UtcNow.Date;

            foreach (Episode episode in allEpisodes)
            {
                if (completedEpisodeIds.Contains(episode.Id))
                {
                    finished++;
                }
                else
                {
                    unfinished++;

                    // "Neu" = bereits veröffentlicht, aber noch nicht gehört.
                    // Zukünftige Episoden (ReleaseDate > heute) werden hier nicht gezählt.
                    if (episode.ReleaseDate.HasValue && episode.ReleaseDate.Value.Date <= today)
                    {
                        newCount++;
                    }
                }
            }

            FinishedEpisodesCount   = finished;
            UnfinishedEpisodesCount = unfinished;
            NewEpisodesCount        = newCount;
        }

        // ── Theme-Wechsel ────────────────────────────────────────────────────────

        /// <summary>
        /// Wendet das angegebene Theme sofort an.
        /// Die Persistenz übernimmt der <see cref="ThemeService"/> intern.
        /// </summary>
        /// <param name="themeName">Name des zu aktivierenden Themes.</param>
        public void SwitchTheme(string themeName)
        {
            if (string.IsNullOrWhiteSpace(themeName))
            {
                return;
            }

            _themeService.ApplyTheme(themeName);
            ActiveTheme = themeName;
        }

        // ── Sprachwechsel ────────────────────────────────────────────────────────

        /// <summary>
        /// Speichert die neue Sprache in AppSettings und startet die App neu.
        /// WinUI 3 kann Ressourcendateien nicht zur Laufzeit nachladen – der Neustart ist zwingend.
        /// </summary>
        /// <param name="languageCode">BCP-47-Sprachcode der gewünschten Sprache, z.B. "de" oder "en".</param>
        /// <returns>Asynchrone Ausführung bis zum Neustart.</returns>
        public async Task ChangeLanguageAsync(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();

            AppSettings settings = await settingsService.GetAsync();
            settings.ActiveLanguage = languageCode;
            await settingsService.SaveAsync(settings);

            // PrimaryLanguageOverride muss vor dem Neustart gesetzt werden –
            // Windows App Runtime liest die Sprache beim Start und kann sie danach nicht wechseln
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageCode;

            // Neustart des MSIX-Pakets – danach lädt die App alle .resw-Ressourcen in der neuen Sprache
            Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
        }
    }
}

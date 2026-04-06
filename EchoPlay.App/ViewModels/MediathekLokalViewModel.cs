using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Parsing;
using EchoPlay.LocalLibrary.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Threading;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die lokale Mediathek mit dynamischem Akkordeon-Layout.
    /// Serien erscheinen als Cover-Kachelgrid. Bei Auswahl einer Serie klappt der Folgen-Bereich
    /// direkt nach der gewählten Kachelreihe auf. Bei Auswahl einer Folge erscheinen die Tracks
    /// in einer festen Spalte rechts neben den Folgen-Kacheln.
    /// Nur Serien und Folgen mit einem lokal gefundenen Ordner (<c>LocalFolderPath != null</c>)
    /// werden angezeigt – alles andere fehlt noch und soll per Scan gefunden werden.
    /// </summary>
    public sealed class MediathekLokalViewModel : ObservableObject, IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISyncService _syncService;
        private readonly IPlayerService _playerService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly StatusBarViewModel _statusBar;
        private readonly ILocalCoverLoader _coverLoader;
        private readonly IScanEventService _scanEventService;
        private readonly ICoverSearchService _coverSearchService;
        private readonly IOnlineAccessGuard _onlineAccessGuard;
        private readonly IOnlineEpisodeChecker _onlineEpisodeChecker;
        private readonly EchoPlay.App.Services.CoverService? _coverService;

        /// <summary>
        /// DispatcherQueue des UI-Threads – wird im Konstruktor auf dem UI-Thread erfasst
        /// und später genutzt, um Hintergrundthread-Callbacks auf den UI-Thread zu marshallen.
        /// Null in Unit-Tests, wo kein WinUI-3-Dispatcher verfügbar ist.
        /// </summary>
        private readonly DispatcherQueue? _dispatcherQueue;

        // Zwischengespeicherte Karte für die asynchrone Cover-Suche:
        // Das ViewModel feuert ein Event mit den Suchergebnissen, die Page zeigt den Dialog,
        // und ruft anschließend ApplySelectedSeriesCoverAsync / ApplySelectedEpisodeCoverAsync auf.
        // Ohne dieses Feld müsste die Card-Referenz über den Event-Args übertragen werden.
        private LocalArtistCardViewModel? _pendingSeriesCoverCard;
        private LocalEpisodeCardViewModel? _pendingEpisodeCoverCard;

        // Wiederverwendbarer HTTP-Client für Cover-Downloads – static verhindert Socket-Erschöpfung
        // bei häufigen Instanziierungen des Transient-ViewModels.
        private static readonly System.Net.Http.HttpClient _downloadClient = new();

        private string _libraryRootPath = string.Empty;
        private bool _isScanning;
        private bool _needsLibraryFolderSetup;
        private string _syncStatusText = string.Empty;
        private bool _isScanIndeterminate = true;
        private double _scanProgressPercent;
        private string _scanDetailText = string.Empty;
        private int _episodeSortIndex;
        private int _episodeFilterIndex;
        private int _episodeTabIndex;
        private IReadOnlyList<LocalArtistCardViewModel> _allArtists = [];
        private IReadOnlyList<LocalArtistCardViewModel> _artists = [];
        private string _localSearchText = string.Empty;
        private IReadOnlyList<LocalEpisodeCardViewModel> _episodes = [];
        private List<LocalEpisodeCardViewModel> _allEpisodes = [];
        private HashSet<Guid> _completedEpisodeIds = [];
        private HashSet<Guid> _inProgressEpisodeIds = [];
        private CancellationTokenSource? _coverCts;
        private IReadOnlyList<LocalTrackRowViewModel> _tracks = [];
        private LocalArtistCardViewModel? _selectedArtist;
        private LocalEpisodeCardViewModel? _selectedEpisode;
        private int _selectedArtistIndex = -1;

        /// <summary>
        /// Initialisiert das ViewModel mit den benötigten Services.
        /// </summary>
        /// <param name="scopeFactory">Für Datenbankzugriffe.</param>
        /// <param name="syncService">Für den Bibliotheks-Scan.</param>
        /// <param name="playerService">Für die Wiedergabe von Folgen aus der lokalen Mediathek.</param>
        /// <param name="errorDialogService">Für Fehler-Dialoge.</param>
        /// <param name="confirmationDialogService">Für Bestätigungs-Dialoge.</param>
        /// <param name="statusBar">
        /// Singleton der Info-Leiste – zeigt globalen Scan-Fortschritt im Hauptfenster.
        /// Als Singleton injiziert, daher kein Lifetime-Problem trotz Transient-ViewModel.
        /// </param>
        /// <param name="coverLoader">
        /// Lädt Cover-Bilder aus dem lokalen Dateisystem (cover.jpg oder ID3-Tag).
        /// Wird beim Auswählen einer Serie für jede Episodenkachel aufgerufen.
        /// </param>
        /// <param name="scanEventService">
        /// Singleton-Dienst für navigationsübergreifende Scan-Event-Benachrichtigungen.
        /// </param>
        /// <param name="coverSearchService">
        /// Sucht online nach Cover-Kandidaten wenn der Nutzer "Cover suchen" auswählt.
        /// </param>
        /// <param name="onlineAccessGuard">Prüft den Offline-Modus und zeigt bei Bedarf einen Bestätigungsdialog.</param>
        /// <param name="onlineEpisodeChecker">Prüft online (iTunes) ob neue Folgen verfügbar sind.</param>
        /// <param name="coverService">Zentraler Cover-Dienst für DB-basierte Cover. Nullable für Tests.</param>
        public MediathekLokalViewModel(
            IServiceScopeFactory scopeFactory,
            ISyncService syncService,
            IPlayerService playerService,
            IErrorDialogService errorDialogService,
            IConfirmationDialogService confirmationDialogService,
            StatusBarViewModel statusBar,
            ILocalCoverLoader coverLoader,
            IScanEventService scanEventService,
            ICoverSearchService coverSearchService,
            IOnlineAccessGuard onlineAccessGuard,
            IOnlineEpisodeChecker onlineEpisodeChecker,
            EchoPlay.App.Services.CoverService? coverService = null)
        {
            _scopeFactory              = scopeFactory;
            _syncService               = syncService;
            _playerService             = playerService;
            _errorDialogService        = errorDialogService;
            _confirmationDialogService = confirmationDialogService;
            _statusBar                 = statusBar;
            _coverLoader               = coverLoader;
            _scanEventService          = scanEventService;
            _coverSearchService        = coverSearchService;
            _onlineAccessGuard         = onlineAccessGuard;
            _onlineEpisodeChecker      = onlineEpisodeChecker;
            _coverService              = coverService;

            // DispatcherQueue beim Erstellen auf dem UI-Thread erfassen –
            // OnSeriesSynced wird später vom Hintergrundthread aufgerufen und braucht diesen Handle.
            // In Unit-Tests existiert kein WinUI-3-Dispatcher – dort bleibt _dispatcherQueue null.
            try
            {
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                _dispatcherQueue = null;
            }

            ScanCommand         = new RelayCommand(() => _ = ScanAsync());
            ReInitializeCommand = new RelayCommand(() => _ = ReInitializeAsync());

            // Befehle werden nur ausgelöst, wenn ein Pfad bekannt ist
            OpenAllSeriesTracksCommand  = new RelayCommand(() => OpenAllTracksByPath(_selectedArtist?.LocalFolderPath));
            OpenAllEpisodeTracksCommand = new RelayCommand(() => OpenAllTracksByPath(_selectedEpisode?.FolderPath));

            // AddFolder-Befehl benötigt das HWND des Hauptfensters – wird per Event aus der Page geholt
            AddFolderCommand = new RelayCommand(() => AddFolderRequested?.Invoke());

            // PlayEpisode ist initial inaktiv – wird via SetEnabled aktiviert sobald Tracks geladen sind
            PlayEpisodeCommand = new RelayCommand(() => PlayCurrentEpisode());
            PlayEpisodeCommand.SetEnabled(false);

            CheckAllSeriesCommand = new RelayCommand(() => _ = CheckAllSeriesAsync());
        }

        /// <summary>
        /// Aktiviert das ViewModel beim Navigieren zur Seite.
        /// Abonniert <see cref="IScanEventService.SeriesSynced"/>, damit laufende Scans
        /// auch nach einer Rücknavigation weiterhin live Kacheln einfügen.
        /// </summary>
        public void Activate()
        {
            _scanEventService.SeriesSynced += OnSeriesSynced;
        }

        /// <summary>
        /// Deaktiviert das ViewModel beim Verlassen der Seite.
        /// Deabonniert das Event, um Memory-Leaks durch den Singleton-Service zu verhindern.
        /// Ohne diesen Aufruf würde der IScanEventService eine Referenz auf das alte ViewModel halten.
        /// </summary>
        public void Deactivate()
        {
            _scanEventService.SeriesSynced -= OnSeriesSynced;
        }

        /// <summary>
        /// Handler für <see cref="IScanEventService.SeriesSynced"/>.
        /// Wird vom Singleton aufgerufen – auch nach einer Rücknavigation, wenn ein neues ViewModel aktiv ist.
        /// <see cref="ScanEventService.RaiseSeriesSynced"/> feuert synchron auf dem Hintergrundthread
        /// des SyncService, daher Dispatch auf den UI-Thread nötig: <see cref="AppendArtistCardAsync"/>
        /// erstellt WinRT-Objekte (<see cref="Windows.Storage.Streams.InMemoryRandomAccessStream"/>,
        /// <see cref="Microsoft.UI.Xaml.Media.Imaging.BitmapImage"/>), die den UI-Thread erfordern.
        /// </summary>
        /// <param name="series">Die synchronisierte Serie.</param>
        private void OnSeriesSynced(Series series)
        {
            if (_dispatcherQueue is not null)
            {
                _dispatcherQueue.TryEnqueue(async () => await AppendArtistCardAsync(series));
            }
            else
            {
                // In Unit-Tests: direkt aufrufen (kein Thread-Wechsel nötig)
                _ = AppendArtistCardAsync(series);
            }
        }

        // ── Bibliothek ───────────────────────────────────────────────────────────

        /// <summary>Pfad zur lokalen Hörspielbibliothek laut AppSettings.</summary>
        public string LibraryRootPath
        {
            get => _libraryRootPath;
            private set => SetProperty(ref _libraryRootPath, value);
        }

        /// <summary>
        /// Gibt an, ob noch kein Bibliotheksordner konfiguriert wurde.
        /// Steuert die Sichtbarkeit des Einrichtungshinweises.
        /// </summary>
        public bool NeedsLibraryFolderSetup
        {
            get => _needsLibraryFolderSetup;
            private set => SetProperty(ref _needsLibraryFolderSetup, value);
        }

        /// <summary>Gibt an, ob gerade ein Scan-Vorgang läuft.</summary>
        public bool IsScanning
        {
            get => _isScanning;
            private set
            {
                if (SetProperty(ref _isScanning, value))
                {
                    OnPropertyChanged(nameof(IsNotScanning));
                    OnPropertyChanged(nameof(IsScanningVisibility));
                }
            }
        }

        /// <summary>
        /// Gibt an, ob kein Scan läuft.
        /// Wird für <c>IsEnabled</c>-Bindungen der Scan-Schaltflächen verwendet,
        /// da WinUI 3 keine Negations-Konverter unterstützt.
        /// </summary>
        public bool IsNotScanning => !_isScanning;

        /// <summary>
        /// Sichtbarkeit des Scan-Overlays – eingeblendet während ein Scan oder eine Neu-Initialisierung läuft.
        /// Visibility-Property statt Konverter, da WinUI 3 kein direktes Bool-to-Visibility unterstützt.
        /// </summary>
        public Visibility IsScanningVisibility => _isScanning ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Gibt an, ob der Fortschrittsbalken im Scan-Overlay indeterministisch angezeigt werden soll.
        /// True solange die Gesamtanzahl der Dateien unbekannt ist oder die Vorbereitung läuft.
        /// </summary>
        public bool IsScanIndeterminate
        {
            get => _isScanIndeterminate;
            private set => SetProperty(ref _isScanIndeterminate, value);
        }

        /// <summary>
        /// Fortschritt in Prozent (0–100) für den deterministischen Fortschrittsbalken im Overlay.
        /// Nur relevant wenn <see cref="IsScanIndeterminate"/> <see langword="false"/> ist.
        /// </summary>
        public double ScanProgressPercent
        {
            get => _scanProgressPercent;
            private set => SetProperty(ref _scanProgressPercent, value);
        }

        /// <summary>
        /// Detailtext für das Scan-Overlay, z.B. "Datei 12 von 150".
        /// Leer wenn kein Detail bekannt ist.
        /// </summary>
        public string ScanDetailText
        {
            get => _scanDetailText;
            private set
            {
                if (SetProperty(ref _scanDetailText, value))
                {
                    OnPropertyChanged(nameof(ScanDetailVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Detail-Texts im Scan-Overlay.
        /// Nur eingeblendet wenn ein Detail-Text vorhanden ist.
        /// </summary>
        public Visibility ScanDetailVisibility =>
            !string.IsNullOrEmpty(_scanDetailText) ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Statusmeldung des laufenden oder abgeschlossenen Scans.</summary>
        public string SyncStatusText
        {
            get => _syncStatusText;
            private set => SetProperty(ref _syncStatusText, value);
        }

        // ── Akkordeon-Daten ──────────────────────────────────────────────────────

        /// <summary>
        /// Serien mit lokalem Ordner – linke Spalte.
        /// Wird bei jedem <see cref="LoadAsync"/>-Aufruf neu befüllt.
        /// </summary>
        public IReadOnlyList<LocalArtistCardViewModel> Artists
        {
            get => _artists;
            private set
            {
                if (SetProperty(ref _artists, value))
                {
                    OnPropertyChanged(nameof(ArtistsEmptyVisibility));
                }
            }
        }

        /// <summary>
        /// Freitext-Suchfilter für die Serien-Kacheln.
        /// Filtert clientseitig auf Titel – kein neuer DB-Query.
        /// Leerer String zeigt alle Serien.
        /// </summary>
        public string LocalSearchText
        {
            get => _localSearchText;
            set
            {
                if (SetProperty(ref _localSearchText, value))
                {
                    ApplyLocalSearchFilter();
                }
            }
        }

        /// <summary>
        /// Episoden der gewählten Serie mit lokalem Ordner – mittlere Spalte.
        /// Leer, solange keine Serie ausgewählt ist.
        /// </summary>
        public IReadOnlyList<LocalEpisodeCardViewModel> Episodes
        {
            get => _episodes;
            private set
            {
                if (SetProperty(ref _episodes, value))
                {
                    OnPropertyChanged(nameof(EpisodesEmptyVisibility));
                    OnPropertyChanged(nameof(EpisodesLoadedVisibility));
                }
            }
        }

        /// <summary>
        /// Tracks der gewählten Episode – rechte Spalte.
        /// Leer, solange keine Episode ausgewählt ist.
        /// </summary>
        public IReadOnlyList<LocalTrackRowViewModel> Tracks
        {
            get => _tracks;
            private set
            {
                if (SetProperty(ref _tracks, value))
                {
                    OnPropertyChanged(nameof(TracksEmptyVisibility));
                    OnPropertyChanged(nameof(TrackActionsVisibility));
                    // Play-Befehl aktivieren sobald Tracks und Folge vorhanden sind
                    PlayEpisodeCommand.SetEnabled(value.Count > 0 && _selectedEpisode is not null);
                }
            }
        }

        /// <summary>Aktuell gewählte Serie – steuert die mittlere Spalte.</summary>
        public LocalArtistCardViewModel? SelectedArtist
        {
            get => _selectedArtist;
            private set
            {
                if (SetProperty(ref _selectedArtist, value))
                {
                    OnPropertyChanged(nameof(SeriesActionsVisibility));
                }
            }
        }

        /// <summary>Aktuell gewählte Episode – steuert das Track-Panel im Akkordeon.</summary>
        public LocalEpisodeCardViewModel? SelectedEpisode
        {
            get => _selectedEpisode;
            private set
            {
                if (SetProperty(ref _selectedEpisode, value))
                {
                    OnPropertyChanged(nameof(TrackActionsVisibility));
                    OnPropertyChanged(nameof(TracksAccordionVisibility));
                    OnPropertyChanged(nameof(SelectedEpisodeTitle));
                    PlayEpisodeCommand.SetEnabled(_tracks.Count > 0 && value is not null);
                }
            }
        }

        /// <summary>
        /// Index der ausgewählten Serie in <see cref="Artists"/>. -1 wenn keine Serie gewählt.
        /// Wird von der Page genutzt, um die Serien-Liste an der richtigen Zeile aufzuteilen.
        /// </summary>
        public int SelectedArtistIndex
        {
            get => _selectedArtistIndex;
            private set
            {
                if (SetProperty(ref _selectedArtistIndex, value))
                {
                    OnPropertyChanged(nameof(EpisodesAccordionVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Folgen-Akkordeons – eingeblendet sobald eine Serie ausgewählt ist.
        /// </summary>
        public Visibility EpisodesAccordionVisibility =>
            _selectedArtistIndex >= 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des Track-Panels rechts neben den Folgen-Kacheln.
        /// Eingeblendet sobald eine Folge ausgewählt ist.
        /// </summary>
        public Visibility TracksAccordionVisibility =>
            _selectedEpisode is not null ? Visibility.Visible : Visibility.Collapsed;

        // ── Sichtbarkeits-Helfer ─────────────────────────────────────────────────

        /// <summary>
        /// Aktuell gewählte Sortierung der Episodenliste (0 = Nummer, 1 = Titel, 2 = Erscheinungsdatum).
        /// Eine Änderung sortiert die geladene Episode-Liste sofort neu.
        /// </summary>
        public int EpisodeSortIndex
        {
            get => _episodeSortIndex;
            set
            {
                if (SetProperty(ref _episodeSortIndex, value))
                {
                    ApplyFilterAndSort();
                }
            }
        }

        /// <summary>
        /// Aktuell gewählter Filter der Episodenliste.
        /// 0 = Alle, 1 = Ungehört, 2 = Gehört, 3 = Angefangen.
        /// Eine Änderung filtert und sortiert die geladene Episode-Liste sofort neu.
        /// </summary>
        public int EpisodeFilterIndex
        {
            get => _episodeFilterIndex;
            set
            {
                if (SetProperty(ref _episodeFilterIndex, value))
                {
                    ApplyFilterAndSort();
                }
            }
        }

        /// <summary>
        /// Aktiver Tab: 0 = Folgen (regulär), 1 = Sonderfolgen.
        /// Steuert welche Episoden in der mittleren Spalte angezeigt werden.
        /// </summary>
        public int EpisodeTabIndex
        {
            get => _episodeTabIndex;
            set
            {
                if (SetProperty(ref _episodeTabIndex, value))
                {
                    ApplyFilterAndSort();
                    OnPropertyChanged(nameof(HasSpecialEpisodes));
                }
            }
        }

        /// <summary>
        /// True wenn die aktuelle Serie Sonderfolgen hat – nur dann wird der Tab sichtbar.
        /// </summary>
        public bool HasSpecialEpisodes => _allEpisodes.Any(e => e.IsSpecialEpisode);

        /// <summary>
        /// Hebt die Serienauswahl auf – der gesamte Folgenbereich verschwindet.
        /// Entspricht dem Zustand vor der ersten Serienauswahl: keine Folgen, keine Tracks.
        /// Laufende Cover-Tasks werden abgebrochen.
        /// </summary>
        public void DeselectArtist()
        {
            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = null;

            // V-Indikator zurücksetzen
            foreach (LocalArtistCardViewModel a in _allArtists)
            {
                a.IsSelectedInAccordion = false;
            }

            SelectedArtist      = null;
            SelectedEpisode     = null;
            Tracks              = [];
            SelectedArtistIndex = -1;
            _allEpisodes        = [];
            Episodes            = [];
        }

        /// <summary>
        /// Anzahl der Sonderfolgen für die Tab-Beschriftung (z.B. "Sonderfolgen (35)").
        /// </summary>
        public int SpecialEpisodeCount => _allEpisodes.Count(e => e.IsSpecialEpisode);

        /// <summary>
        /// Sichtbarkeit des "Keine Serien"-Platzhalters.
        /// Erscheint wenn die Bibliothek leer ist oder noch kein Scan durchgeführt wurde.
        /// </summary>
        public Visibility ArtistsEmptyVisibility =>
            _artists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des "Folge wählen"-Platzhalters in der mittleren Spalte.
        /// Erscheint solange keine Serie gewählt ist.
        /// </summary>
        public Visibility EpisodesEmptyVisibility =>
            _episodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit der Sortier-ComboBox in der mittleren Spalte.
        /// Nur eingeblendet wenn Episoden geladen sind.
        /// </summary>
        public Visibility EpisodesLoadedVisibility =>
            _episodes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des "Track wählen"-Platzhalters in der rechten Spalte.
        /// Erscheint solange keine Episode gewählt ist.
        /// </summary>
        public Visibility TracksEmptyVisibility =>
            _tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des "Alle Tracks dieser Serie bearbeiten"-Buttons.
        /// Nur eingeblendet wenn eine Serie mit bekanntem Ordner gewählt ist.
        /// </summary>
        public Visibility SeriesActionsVisibility =>
            _selectedArtist?.LocalFolderPath is not null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit der Episoden-Aktionsleiste oben in der rechten Spalte.
        /// Nur eingeblendet wenn Tracks geladen sind.
        /// </summary>
        public Visibility TrackActionsVisibility =>
            _tracks.Count > 0 && _selectedEpisode is not null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Anzeigename der aktuell gewählten Episode.
        /// Leer wenn keine Episode ausgewählt ist.
        /// </summary>
        public string SelectedEpisodeTitle => _selectedEpisode?.Title ?? string.Empty;

        /// <summary>
        /// Metadaten-Zeile über dem Folgentitel im Track-Panel.
        /// Format: "Folge 229 · 8 Tracks · 1:12:34" oder "8 Tracks · 1:12:34" ohne Nummer.
        /// Leer wenn keine Episode ausgewählt ist.
        /// </summary>
        public string TrackPanelSubtitle { get; private set; } = string.Empty;

        // ── Befehle ──────────────────────────────────────────────────────────────

        /// <summary>Startet einen neuen Scan der lokalen Bibliothek.</summary>
        public ICommand ScanCommand { get; }

        /// <summary>Setzt alle lokalen Zuordnungen zurück und scannt neu.</summary>
        public ICommand ReInitializeCommand { get; }

        /// <summary>Öffnet alle Tracks der gewählten Serie im Tag-Manager.</summary>
        public ICommand OpenAllSeriesTracksCommand { get; }

        /// <summary>Öffnet alle Tracks der gewählten Episode im Tag-Manager.</summary>
        public ICommand OpenAllEpisodeTracksCommand { get; }

        /// <summary>
        /// Signalisiert, dass der Nutzer einen Ordner als neue lokale Serie hinzufügen möchte.
        /// Das Kommando selbst kennt das HWND nicht – die Page reagiert auf das Event
        /// und ruft <see cref="AddFolderAsync"/> mit dem Fenster-Handle auf.
        /// </summary>
        public ICommand AddFolderCommand { get; }

        /// <summary>
        /// Spielt alle Tracks der aktuell gewählten Folge in sortierter Reihenfolge ab.
        /// Aktiviert den MiniPlayer im Hauptfenster.
        /// Nur ausführbar wenn eine Folge mit mindestens einem Track ausgewählt ist.
        /// </summary>
        public RelayCommand PlayEpisodeCommand { get; }

        /// <summary>
        /// Prüft alle abonnierten Serien mit lokalem Ordner auf fehlende Folgen
        /// (lokale Lücken + Live-Online-Abgleich) und feuert <see cref="AllSeriesCheckCompleted"/>.
        /// </summary>
        public ICommand CheckAllSeriesCommand { get; }

        // ── Navigation ───────────────────────────────────────────────────────────

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer den Tag-Manager für einen Pfad öffnen möchte.
        /// Die <see cref="EchoPlay.App.Pages.MediathekLokalPage"/> abonniert dieses Event
        /// und führt die Frame-Navigation durch.
        /// </summary>
        public event Action<string>? NavigateToTagManagerRequested;

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer den Ordner-Hinzufügen-Button drückt.
        /// Die Page muss das HWND liefern und <see cref="AddFolderAsync"/> aufrufen.
        /// </summary>
        public event Action? AddFolderRequested;

        /// <summary>
        /// Wird ausgelöst, nachdem fehlende Episoden ermittelt wurden.
        /// Die Page zeigt die übergebene Titelliste in einem ContentDialog an.
        /// Die Liste ist leer wenn alle Episoden lokal vorhanden sind.
        /// </summary>
        public event Action<IReadOnlyList<string>>? MissingEpisodesResolved;

        /// <summary>
        /// Wird ausgelöst, nachdem die Gesamtprüfung aller Serien abgeschlossen ist.
        /// Die Page zeigt den Bericht in einem Dialog an und bietet den TXT-Export an.
        /// </summary>
        public event Action<MissingEpisodesReport>? AllSeriesCheckCompleted;

        /// <summary>
        /// Wird ausgelöst, wenn eine Online-Cover-Suche für eine Serie Treffer liefert.
        /// Die Page öffnet daraufhin den Kachelauswahl-Dialog und ruft anschließend
        /// <see cref="ApplySelectedSeriesCoverAsync(CoverSearchResult)"/> mit dem gewählten Kandidaten auf.
        /// Ohne abonnierte Listener würde das Ergebnis still verworfen.
        /// </summary>
        public event EventHandler<IReadOnlyList<CoverSearchResult>>? SeriesCoverSearchResultsReady;

        /// <summary>
        /// Wird ausgelöst, wenn eine Online-Cover-Suche für eine Episode Treffer liefert.
        /// Die Page öffnet daraufhin den Kachelauswahl-Dialog und ruft anschließend
        /// <see cref="ApplySelectedEpisodeCoverAsync(CoverSearchResult)"/> mit dem gewählten Kandidaten auf.
        /// </summary>
        public event EventHandler<IReadOnlyList<CoverSearchResult>>? EpisodeCoverSearchResultsReady;

        /// <summary>
        /// Wird ausgelöst, wenn der Ordnerstruktur-Assistent eine Vorschau erstellt hat.
        /// Die Page zeigt die geplanten Verschiebungen in einem ContentDialog an.
        /// Bei Bestätigung durch den Nutzer ruft die Page <see cref="ExecuteRestructureAsync"/> auf.
        /// </summary>
        public event Action<EchoPlay.LocalLibrary.Models.RestructurePreview>? RestructurePreviewReady;

        // ── Laden ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lädt Bibliothekseinstellungen und alle Serien mit lokalem Ordner.
        /// Setzt die Auswahl zurück – mittlere und rechte Spalte werden geleert.
        /// Die Liste erscheint sofort ohne Cover; Cover laden progressiv im Hintergrund nach.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task LoadAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            ISeriesDataService seriesService        = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService      = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();

            EchoPlay.Data.Entities.Settings.AppSettings settings = await settingsService.GetAsync();
            LibraryRootPath         = settings.LocalLibraryRootPath ?? string.Empty;
            NeedsLibraryFolderSetup = string.IsNullOrWhiteSpace(settings.LocalLibraryRootPath);

            // Nur Serien anzeigen, für die der Scanner einen Ordner gefunden hat
            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();
            List<Series> localSeries = [..allSeries.Where(s => s.LocalFolderPath is not null)];

            // Episodenzähler für alle Serien in einer einzigen Datenbankabfrage –
            // ersetzt die frühere Schleife mit N einzelnen GetBySeriesIdAsync-Aufrufen
            List<Guid> seriesIds = [..localSeries.Select(s => s.Id)];
            IReadOnlyDictionary<Guid, (int Total, int Local)> episodeCounts =
                await episodeService.GetEpisodeCountsForSeriesAsync(seriesIds);

            List<LocalArtistCardViewModel> artistCards = new(localSeries.Count);

            foreach (Series series in localSeries)
            {
                // Serien ohne Episoden (noch nicht vollständig gescannt) erhalten (0, 0)
                (int total, int local) = episodeCounts.TryGetValue(series.Id, out (int Total, int Local) counts)
                    ? (counts.Total, counts.Local)
                    : (0, 0);

                artistCards.Add(new LocalArtistCardViewModel(
                    seriesId:          series.Id,
                    title:             series.Title,
                    coverImage:        null,
                    localFolderPath:   series.LocalFolderPath,
                    localEpisodeCount: local,
                    totalEpisodeCount: total,
                    isFavorite:        series.IsFavorite,
                    isWatched:         series.IsWatched,
                    scopeFactory:      _scopeFactory));
            }

            // Auswahl zurücksetzen, damit Akkordeon-Bereiche leer bleiben
            SelectedArtist      = null;
            SelectedEpisode     = null;
            SelectedArtistIndex = -1;
            Episodes            = [];
            Tracks              = [];
            _allArtists         = artistCards;
            ApplyLocalSearchFilter();

            // Cover nachträglich laden – Kacheln sind bereits sichtbar, Cover erscheinen progressiv.
            // Zip verbindet Karte und Serie, die in derselben Reihenfolge aufgebaut wurden.
            foreach ((LocalArtistCardViewModel card, Series series) in artistCards.Zip(localSeries))
            {
                _ = LoadCoverForCardAsync(card, series);
            }
        }

        /// <summary>
        /// Lädt das Cover einer Kachel im Hintergrund und setzt es direkt auf dem ViewModel.
        /// Fehler werden ignoriert – der Platzhalter bleibt bestehen, ohne den Ladevorgang zu stören.
        /// </summary>
        /// <param name="card">Die Kachel, deren Cover gesetzt werden soll.</param>
        /// <param name="series">Die Serie mit den Quell-Coverdaten.</param>
        private async Task LoadCoverForCardAsync(LocalArtistCardViewModel card, Series series)
        {
            try
            {
                BitmapImage? cover = await BuildCoverImageAsync(series);
                card.CoverImage = cover;
            }
            catch
            {
                // Cover-Ladefehler ignorieren – Platzhalter bleibt bestehen
            }
        }

        // ── Auswahl-Logik ────────────────────────────────────────────────────────

        /// <summary>
        /// Wählt eine Serie aus und lädt deren Episoden mit lokalem Ordner in die mittlere Spalte.
        /// Die rechte Spalte wird dabei geleert.
        /// </summary>
        /// <param name="artist">Die ausgewählte Serie.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task SelectArtistAsync(LocalArtistCardViewModel artist)
        {
            // Laufende Cover-Tasks der vorherigen Serie abbrechen – verhindert,
            // dass alte Hintergrund-Tasks Cover auf bereits ersetzte Karten setzen.
            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = new CancellationTokenSource();
            CancellationToken coverToken = _coverCts.Token;

            // V-Indikator: vorherige Auswahl zurücksetzen, neue setzen
            foreach (LocalArtistCardViewModel a in _allArtists)
            {
                a.IsSelectedInAccordion = false;
            }

            artist.IsSelectedInAccordion = true;

            SelectedArtist  = artist;
            SelectedEpisode = null;
            Tracks          = [];

            // Index für den Akkordeon-Split ermitteln – Page braucht die Position in der Liste
            int idx = -1;
            for (int i = 0; i < _artists.Count; i++)
            {
                if (ReferenceEquals(_artists[i], artist)) { idx = i; break; }
            }
            SelectedArtistIndex = idx;

            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();

            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(artist.SeriesId);

            // Schritt 1: Karten OHNE Cover erstellen – GridView rendert sofort
            List<LocalEpisodeCardViewModel> episodeCards = [];
            List<(LocalEpisodeCardViewModel Card, Episode Episode)> coverQueue = [];

            foreach (Episode episode in episodes.Where(e => e.LocalFolderPath is not null).OrderBy(e => e.EpisodeNumber))
            {
                // Serien-Präfix aus dem Episodentitel entfernen, falls vorhanden.
                // Viele Hörspiel-Ordner heißen z.B. "Abenteurer unserer Zeit - Der Pirat" –
                // der Serienname am Anfang ist redundant und verschwendet Platz in der Kachel.
                string displayTitle = StripSeriesPrefix(episode.Title, artist.Title);

                // Sonderfolge: Nummer 0 oder null (kein erkennbares Nummernmuster)
                bool isSpecial = episode.EpisodeNumber is null or 0;

                LocalEpisodeCardViewModel card = new(
                    episodeId:        episode.Id,
                    episodeNumber:    episode.EpisodeNumber,
                    title:            displayTitle,
                    localTrackCount:  episode.LocalTrackCount ?? 0,
                    folderPath:       episode.LocalFolderPath,
                    isSpecialEpisode: isSpecial);

                episodeCards.Add(card);
                coverQueue.Add((card, episode));
            }

            // Wiedergabestatus-IDs für Filtern laden (ein einziger Batch-Query)
            List<Guid> episodeIds = episodeCards.Select(c => c.EpisodeId).ToList();
            IPlaybackStateDataService stateService = scope.ServiceProvider
                .GetRequiredService<IPlaybackStateDataService>();
            _completedEpisodeIds = await stateService.GetCompletedEpisodeIdsAsync(episodeIds);

            // In-Progress-IDs: alle mit PlaybackState, die nicht completed sind
            IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync();
            _inProgressEpisodeIds = allStates
                .Where(s => episodeIds.Contains(s.EpisodeId) && !s.IsCompleted && s.LastPosition > TimeSpan.Zero)
                .Select(s => s.EpisodeId)
                .ToHashSet();

            // Gehört-Status auf den Karten setzen (nach dem Laden der IDs)
            foreach (LocalEpisodeCardViewModel card in episodeCards)
            {
                card.IsCompleted = _completedEpisodeIds.Contains(card.EpisodeId);
            }

            // Ungefilterte Gesamtliste speichern, Tab/Filter/Sortierung zurücksetzen
            _allEpisodes = episodeCards;
            _episodeTabIndex = 0;
            _episodeFilterIndex = 0;
            OnPropertyChanged(nameof(EpisodeTabIndex));
            OnPropertyChanged(nameof(EpisodeFilterIndex));
            OnPropertyChanged(nameof(HasSpecialEpisodes));
            OnPropertyChanged(nameof(SpecialEpisodeCount));
            ApplyFilterAndSort();

            // Erste Charge: max. 60 Kacheln (reguläre + Sonderfolgen gemischt) MIT Cover laden,
            // damit der aktive Tab sofort Cover zeigt. Sonderfolgen werden nicht mehr verzögert,
            // weil die Cover-Dateien auf lokaler SSD schnell genug laden.
            int firstBatchSize = Math.Min(60, coverQueue.Count);

            for (int i = 0; i < firstBatchSize; i++)
            {
                if (coverToken.IsCancellationRequested) return;
                await LoadCoverForEpisodeCardAsync(coverQueue[i].Card, coverQueue[i].Episode);
            }

            // Rest im Hintergrund – ab Position 60, in 60er-Chargen nachladend.
            if (coverQueue.Count > firstBatchSize)
            {
                List<(LocalEpisodeCardViewModel Card, Episode Episode)> remaining =
                    coverQueue.GetRange(firstBatchSize, coverQueue.Count - firstBatchSize);
                _ = LoadCoversBatchedAsync(remaining, coverToken);
            }
        }

        /// <summary>
        /// Wählt eine Episode aus und lädt deren Tracks in die rechte Spalte.
        /// </summary>
        /// <param name="episode">Die ausgewählte Episode.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task SelectEpisodeAsync(LocalEpisodeCardViewModel episode)
        {
            SelectedEpisode = episode;

            using IServiceScope scope = _scopeFactory.CreateScope();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();

            IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episode.EpisodeId);

            List<LocalTrackRowViewModel> trackRows = [];

            foreach (LocalTrack track in tracks.OrderBy(t => t.TrackNumber))
            {
                trackRows.Add(new LocalTrackRowViewModel(
                    trackId:                     track.Id,
                    trackNumber:                 track.TrackNumber,
                    filePath:                    track.FilePath,
                    duration:                    track.Duration,
                    requestTagManagerNavigation: RequestTagManagerNavigation));
            }

            Tracks = trackRows;

            // Metadaten-Zeile: "Folge 229 · 8 Tracks · 1:12:34"
            TimeSpan totalDuration = TimeSpan.Zero;
            foreach (LocalTrack track in tracks)
            {
                totalDuration += track.Duration;
            }

            string durationText = totalDuration.TotalHours >= 1
                ? $"{(int)totalDuration.TotalHours}:{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}"
                : $"{(int)totalDuration.TotalMinutes}:{totalDuration.Seconds:D2}";

            string trackWord = tracks.Count == 1 ? "Track" : "Tracks";
            string numberPart = episode.EpisodeNumber.HasValue
                ? $"Folge {episode.EpisodeNumber.Value} \u00B7 "
                : string.Empty;

            TrackPanelSubtitle = $"{numberPart}{tracks.Count} {trackWord} \u00B7 {durationText}";
            OnPropertyChanged(nameof(TrackPanelSubtitle));
        }

        // ── Tag-Manager-Navigation ───────────────────────────────────────────────

        /// <summary>
        /// Löst das <see cref="NavigateToTagManagerRequested"/>-Event aus.
        /// Aufgerufen von <see cref="LocalTrackRowViewModel"/>-Callbacks
        /// sowie von <see cref="OpenAllSeriesTracksCommand"/> und <see cref="OpenAllEpisodeTracksCommand"/>.
        /// </summary>
        /// <param name="path">Ordnerpfad, den der Tag-Manager öffnen soll.</param>
        public void RequestTagManagerNavigation(string path)
        {
            NavigateToTagManagerRequested?.Invoke(path);
        }

        // ── Scan / Neu-Initialisierung ───────────────────────────────────────────

        /// <summary>
        /// Startet einen Bibliotheks-Scan über den SyncService.
        /// Serien werden über den <c>onSeriesSynced</c>-Callback sofort in der Liste angezeigt,
        /// sobald sie DB-synchronisiert sind – ohne auf das Gesamtergebnis zu warten.
        /// Nach Abschluss wird <see cref="LoadAsync"/> aufgerufen, damit Episodenzähler und
        /// Cover korrekt gesetzt sind.
        /// </summary>
        private async Task ScanAsync()
        {
            if (IsScanning)
            {
                return;
            }

            // Liste leeren, damit keine veralteten Karten sichtbar sind, während der Scan läuft
            Artists         = [];
            SelectedArtist  = null;
            SelectedEpisode = null;
            Episodes        = [];
            Tracks          = [];

            IsScanning          = true;
            IsScanIndeterminate = true;
            ScanProgressPercent = 0;
            ScanDetailText      = string.Empty;
            SyncStatusText      = "Vorbereitung …";

            try
            {
                // Wenn die DB keine Serien enthält (z.B. nach "Bibliothek zurücksetzen"),
                // verhält sich Scan wie Neu-Initialisierung – forceImportAll erzwingt den
                // vollständigen Import aller gefundenen Serienordner.
                bool forceImportAll;
                using (IServiceScope checkScope = _scopeFactory.CreateScope())
                {
                    ISeriesDataService checkService = checkScope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                    IReadOnlyList<Series> existing  = await checkService.GetAllAsync();
                    forceImportAll = !existing.Any();
                }

                Progress<ScanProgress> progress = new(p =>
                {
                    // Phasenbeschreibung anzeigen wenn vorhanden, sonst den StatusText des Scanners
                    SyncStatusText      = !string.IsNullOrEmpty(p.PhaseLabel) ? p.PhaseLabel : p.StatusText;
                    ScanDetailText      = p.DetailText ?? string.Empty;
                    IsScanIndeterminate = p.PercentComplete <= 0;
                    ScanProgressPercent = p.PercentComplete;
                    _statusBar.UpdateScanProgress(p);
                });

                // IScanEventService übernimmt die Serie-Callbacks – kein direkter Callback mehr nötig
                SyncResult result = await _syncService.SyncAsync(progress, forceImportAll: forceImportAll);

                // Fortschrittsbalken sofort ausblenden – bevor LoadAsync die Liste neu befüllt.
                // Sonst wirkt die Ladezeit der DB-Ansicht wie ein "hängengebliebener" Scan.
                _statusBar.ClearScanProgress();
                SyncStatusText = $"Scan abgeschlossen: {result.TracksCreated} Tracks angelegt, {result.EpisodesUpdated} Episoden aktualisiert";
                ScanDetailText = string.Empty;

                // Abschließender Reload für konsistente Episodenzähler und Cover
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _statusBar.ClearScanProgress();
                SyncStatusText = string.Empty;
                ScanDetailText = string.Empty;
                await _errorDialogService.ShowAsync("Scan fehlgeschlagen", ex.Message);
            }
            finally
            {
                IsScanning = false;
            }
        }

        /// <summary>
        /// Setzt die lokale Bibliothek vollständig zurück und startet einen Neu-Scan.
        /// Rein lokale Serien (ohne Online-ID) werden per Soft-Delete entfernt.
        /// Online-importierte Serien behalten ihre Metadaten – nur der lokale Pfad und
        /// die Track-Zuordnungen werden gelöscht. Wiedergabestatus bleibt in jedem Fall erhalten.
        /// </summary>
        private async Task ReInitializeAsync()
        {
            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                "Neu-Initialisierung",
                "Alle lokal gescannten Serien werden vollständig entfernt und neu eingelesen. " +
                "Online-importierte Serien behalten ihre Metadaten. " +
                "Wiedergabestatus bleibt erhalten. Fortfahren?");

            if (!confirmed)
            {
                return;
            }

            // Sofort leeren – der Nutzer sieht das Reset unmittelbar, bevor der lange DB-Cleanup beginnt
            Artists  = [];
            Episodes = [];
            Tracks   = [];
            IsScanning          = true;
            IsScanIndeterminate = true;
            ScanProgressPercent = 0;
            ScanDetailText      = string.Empty;
            SyncStatusText      = "Vorbereitung …";

            try
            {
                // Datenbankbereinigung auf dem Threadpool – verhindert UI-Freeze durch die vielen
                // aufeinanderfolgenden DB-Awaits (jedes await-Continuation würde sonst auf den
                // WinUI-3-DispatcherQueue gepostet und Eingaben/Zeichnen verdrängen).
                // Ein eigener Scope ist nötig, da DbContext nicht thread-sicher ist.
                await Task.Run(async () =>
                {
                    using IServiceScope cleanupScope = _scopeFactory.CreateScope();
                    ISeriesDataService seriesService    = cleanupScope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                    IEpisodeDataService episodeService  = cleanupScope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                    ILocalTrackDataService trackService = cleanupScope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();

                    // GetAllAsync() liefert nur nicht-gelöschte Serien – korrekt, da wir nur aktive zurücksetzen wollen
                    IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();

                    foreach (Series series in allSeries)
                    {
                        bool isLocalOnly = series.SpotifyArtistId is null && series.AppleMusicArtistId is null;

                        if (isLocalOnly)
                        {
                            // Rein lokale Serie vollständig entfernen – kaskadiert Episoden, Tracks und PlaybackStates
                            await seriesService.DeleteAsync(series.Id);
                            continue;
                        }

                        // Online-importierte Serie: lokale Zuordnung entfernen, Metadaten behalten
                        series.LocalFolderPath = null;
                        // Gespeichertes Cover löschen – beim nächsten Scan wird es neu eingelesen.
                        // Ohne dieses Reset würde das alte Binärbild dauerhaft in der DB verbleiben,
                        // selbst wenn der Nutzer ein anderes Bild auf die Festplatte legt.
                        series.LocalCoverData  = null;
                        await seriesService.UpdateAsync(series);

                        IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id);

                        foreach (Episode episode in episodes)
                        {
                            episode.LocalFolderPath = null;
                            episode.LocalTrackCount = null;
                            episode.LocalCoverData  = null;
                            // Zurück auf den Ausgangszustand – kein lokaler Abgleich mehr vorhanden
                            episode.TrackMatchKind  = TrackMatchKind.NotMatched;
                            await episodeService.UpdateAsync(episode);

                            // Leere Liste löscht alle LocalTrack-Einträge dieser Episode
                            await trackService.SaveTracksForEpisodeAsync(episode.Id, []);
                        }
                    }
                });

                SyncStatusText = "Zurückgesetzt. Starte Scan …";
                _statusBar.UpdateScanProgress(new ScanProgress { StatusText = "Zurückgesetzt. Starte Scan …" });

                Progress<ScanProgress> progress = new(p =>
                {
                    SyncStatusText      = !string.IsNullOrEmpty(p.PhaseLabel) ? p.PhaseLabel : p.StatusText;
                    ScanDetailText      = p.DetailText ?? string.Empty;
                    IsScanIndeterminate = p.PercentComplete <= 0;
                    ScanProgressPercent = p.PercentComplete;
                    _statusBar.UpdateScanProgress(p);
                });
                // forceImportAll: true – nach dem Reset müssen alle Serienordner neu importiert werden,
                // unabhängig davon ob AutoImportAfterScan in den Einstellungen aktiviert ist.
                // IScanEventService übernimmt die Serie-Callbacks
                SyncResult result = await _syncService.SyncAsync(progress, forceImportAll: true);

                _statusBar.ClearScanProgress();
                SyncStatusText = $"Neu-Initialisierung abgeschlossen: {result.TracksCreated} Tracks angelegt";
                ScanDetailText = string.Empty;
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _statusBar.ClearScanProgress();
                SyncStatusText = string.Empty;
                ScanDetailText = string.Empty;
                await _errorDialogService.ShowAsync("Neu-Initialisierung fehlgeschlagen", ex.Message);
            }
            finally
            {
                IsScanning = false;
            }
        }

        // ── Ordner wählen ────────────────────────────────────────────────────────

        /// <summary>
        /// Öffnet den System-Ordnerpicker, speichert den gewählten Pfad in AppSettings
        /// und lädt die Seite danach neu.
        /// Bricht der Benutzer ab, bleibt der bisherige Zustand erhalten.
        /// </summary>
        /// <param name="windowHandle">
        /// HWND des Hauptfensters – WinRT-FolderPicker muss per <c>InitializeWithWindow</c>
        /// an ein Fenster gebunden werden.
        /// </param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task PickFolderAsync(nint windowHandle)
        {
            Windows.Storage.Pickers.FolderPicker picker = new();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
            picker.FileTypeFilter.Add("*");

            Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();

            if (folder is null)
            {
                return;
            }

            // Pfad dauerhaft speichern, damit er beim nächsten Start noch vorhanden ist
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            EchoPlay.Data.Entities.Settings.AppSettings settings = await settingsService.GetAsync();
            settings.LocalLibraryRootPath = folder.Path;
            await settingsService.SaveAsync(settings);

            // Seite neu laden – NeedsLibraryFolderSetup wird zurückgesetzt
            await LoadAsync();
        }

        // ── Ordner manuell hinzufügen ────────────────────────────────────────────

        /// <summary>
        /// Öffnet den Ordnerpicker, legt den gewählten Ordner als neue lokale Serie an
        /// und lädt die Ansicht danach neu.
        /// Wurde der Ordner bereits als Serienordner zugeordnet, wird kein Duplikat erstellt –
        /// der Nutzer erhält stattdessen einen stillen Abbruch.
        /// </summary>
        /// <param name="windowHandle">
        /// HWND des Hauptfensters – WinRT-FolderPicker muss per <c>InitializeWithWindow</c>
        /// an ein Fenster gebunden werden.
        /// </param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task AddFolderAsync(nint windowHandle)
        {
            Windows.Storage.Pickers.FolderPicker picker = new();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
            picker.FileTypeFilter.Add("*");

            Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();

            if (folder is null)
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

            // Bestehende Serien laden, um Duplikate per Ordnerpfad zu erkennen
            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();

            bool pathAlreadyUsed = allSeries.Any(
                s => string.Equals(s.LocalFolderPath, folder.Path, StringComparison.OrdinalIgnoreCase));

            if (pathAlreadyUsed)
            {
                return;
            }

            Series newSeries = new()
            {
                Title           = folder.Name,
                LocalFolderPath = folder.Path
            };

            await seriesService.AddAsync(newSeries);
            await LoadAsync();
        }

        // ── Serien-Verwaltung ─────────────────────────────────────────────────────

        /// <summary>
        /// Kennzeichnet alle Episoden einer Serie als vollständig gehört.
        /// Existiert für eine Episode bereits ein <see cref="PlaybackState"/>, wird nur
        /// <c>IsCompleted</c> gesetzt. Fehlt der Eintrag, wird er neu angelegt.
        /// Statistiken in der Statusleiste werden nach dem Vorgang aktualisiert.
        /// </summary>
        /// <param name="seriesId">ID der betroffenen Serie.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task MarkAllAsReadAsync(Guid seriesId)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService       = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IPlaybackStateDataService playbackService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(seriesId);

            foreach (Episode episode in episodes)
            {
                PlaybackState? existing = await playbackService.GetByEpisodeIdAsync(episode.Id);

                if (existing is not null)
                {
                    if (!existing.IsCompleted)
                    {
                        existing.IsCompleted = true;
                        existing.CompletedAt = DateTime.UtcNow;
                        await playbackService.UpdateAsync(existing);
                    }
                }
                else
                {
                    await playbackService.AddAsync(new PlaybackState
                    {
                        EpisodeId    = episode.Id,
                        IsCompleted  = true,
                        CompletedAt  = DateTime.UtcNow,
                        LastPlayedAt = DateTime.UtcNow
                    });
                }
            }

            // Statistiken (gehörte/offene Folgen) in der Statusleiste aktualisieren
            await _statusBar.RefreshAsync();
        }

        /// <summary>
        /// Entfernt eine Serie aus der Bibliothek (Soft-Delete in der DB).
        /// Die Dateien auf der Festplatte bleiben erhalten – beim nächsten Scan
        /// wird die Serie wieder erkannt und kann erneut importiert werden.
        /// </summary>
        /// <param name="seriesId">ID der zu entfernenden Serie.</param>
        public async Task DeleteSeriesFromLibraryAsync(Guid seriesId)
        {
            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                "Aus Bibliothek entfernen",
                "Die Serie und alle zugehörigen Episoden werden aus der Bibliothek entfernt. " +
                "Die Dateien auf der Festplatte bleiben erhalten. Fortfahren?");

            if (!confirmed)
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            await seriesService.DeleteAsync(seriesId);

            await LoadAsync();
        }

        /// <summary>
        /// Löscht eine Serie unwiderruflich – sowohl aus der DB als auch von der Festplatte.
        /// </summary>
        /// <param name="seriesId">ID der Serie.</param>
        /// <param name="folderPath">Lokaler Ordnerpfad der Serie.</param>
        public async Task DeleteSeriesFromDiskAsync(Guid seriesId, string? folderPath)
        {
            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                "Von Festplatte löschen",
                "Die Serie, alle Episoden und alle Audiodateien werden unwiderruflich gelöscht. " +
                "Dieser Vorgang kann nicht rückgängig gemacht werden. Fortfahren?");

            if (!confirmed)
            {
                return;
            }

            // Erst aus DB löschen
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            await seriesService.DeleteAsync(seriesId);

            // Dann Ordner auf der Festplatte löschen
            if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            {
                await Task.Run(() => Directory.Delete(folderPath, recursive: true));
            }

            await LoadAsync();
        }

        // ── Wiedergabe ────────────────────────────────────────────────────────────

        /// <summary>
        /// Spielt alle Tracks der aktuell gewählten Episode in korrekter Reihenfolge ab.
        /// Startet den MiniPlayer im Hauptfenster über den globalen PlayerService.
        /// Die Tracks sind bereits nach Tracknummer sortiert in <see cref="Tracks"/> vorhanden.
        /// </summary>
        private void PlayCurrentEpisode()
        {
            if (_selectedEpisode is null || _tracks.Count == 0)
            {
                return;
            }

            // FilePaths in der aktuellen Reihenfolge sammeln – bereits nach TrackNumber sortiert
            List<string> trackPaths = [.._tracks.Select(t => t.FilePath)];
            _playerService.Play(_selectedEpisode.EpisodeId, trackPaths);
        }

        // ── Fehlende Folgen ────────────────────────────────────────────────────────

        /// <summary>
        /// Mögliche Ergebnisse des Drei-Optionen-Dialogs für die Fehlende-Folgen-Prüfung.
        /// </summary>
        public enum MissingEpisodesMode
        {
            /// <summary>Nutzer hat abgebrochen – keine Prüfung.</summary>
            Cancel,
            /// <summary>Nur lokale Ordnerstruktur prüfen (kein Netzwerk).</summary>
            OfflineOnly,
            /// <summary>Lokale Lücken + Live-Online-Abgleich per iTunes.</summary>
            WithOnline
        }

        /// <summary>
        /// Wird ausgelöst, bevor die Fehlende-Folgen-Prüfung beginnt.
        /// Die Page zeigt einen Drei-Optionen-Dialog (Online / Nur offline / Abbrechen)
        /// und setzt den <see cref="TaskCompletionSource{MissingEpisodesMode}"/>.
        /// </summary>
        public event Func<Task<MissingEpisodesMode>>? MissingEpisodesModeRequested;

        /// <summary>
        /// Ermittelt fehlende Folgen einer Serie. Fragt den Nutzer zuerst über
        /// <see cref="MissingEpisodesModeRequested"/>, ob online geprüft werden soll.
        /// Analysiert dann die lokale Ordnerstruktur und optional die iTunes API.
        /// </summary>
        /// <param name="seriesId">ID der zu prüfenden Serie.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task ShowMissingEpisodesAsync(Guid seriesId)
        {
            // Serienkarte finden – enthält den lokalen Ordnerpfad
            LocalArtistCardViewModel? card = _artists.FirstOrDefault(a => a.SeriesId == seriesId);

            if (card?.LocalFolderPath is null || !Directory.Exists(card.LocalFolderPath))
            {
                MissingEpisodesResolved?.Invoke(["Kein lokaler Ordner für diese Serie vorhanden."]);
                return;
            }

            // Nutzer fragen: Online + Offline, nur Offline oder Abbrechen?
            MissingEpisodesMode mode = MissingEpisodesMode.OfflineOnly;
            if (MissingEpisodesModeRequested is not null)
            {
                mode = await MissingEpisodesModeRequested.Invoke();
            }

            if (mode == MissingEpisodesMode.Cancel)
            {
                return;
            }

            // Phase 1: Dateisystem-Lücken im Thread-Pool analysieren
            List<string> result = await Task.Run(() => AnalyzeMissingEpisodes(card.LocalFolderPath));

            // Phase 2: Online-Abgleich nur wenn gewünscht
            if (mode == MissingEpisodesMode.WithOnline)
            {
                List<string> onlineMessages = await AnalyzeLiveOnlineMissingAsync(seriesId, card);
                if (onlineMessages.Count > 0)
                {
                    result.Add(string.Empty);
                    result.AddRange(onlineMessages);
                }
            }

            MissingEpisodesResolved?.Invoke(result);
        }

        /// <summary>
        /// Prüft per iTunes API, welche Folgen online nach der höchsten lokalen Nummer existieren.
        /// Setzt den temporären Online-Status in der StatusBar während der Prüfung.
        /// </summary>
        private async Task<List<string>> AnalyzeLiveOnlineMissingAsync(
            Guid seriesId, LocalArtistCardViewModel card)
        {
            // Temporären Online-Status setzen (ohne erneuten Dialog – der Nutzer hat bereits bestätigt)
            _statusBar.IsTemporarilyOnline = true;

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                Series? series = await seriesService.GetByIdAsync(seriesId);

                if (series is null)
                {
                    return [];
                }

                CheckableSeriesInfo checkable = new()
                {
                    SeriesId           = series.Id,
                    Title              = series.Title,
                    AppleMusicArtistId = series.AppleMusicArtistId,
                    LocalFolderPath    = card.LocalFolderPath,
                    CoverImageUrl      = series.CoverImageUrl
                };

                IReadOnlyList<OnlineEpisodeCheckResult> results =
                    await _onlineEpisodeChecker.CheckAllAsync([checkable]);

                if (results.Count == 0)
                {
                    return [];
                }

                OnlineEpisodeCheckResult checkResult = results[0];

                if (checkResult.MissingOnlineEpisodes.Count == 0)
                {
                    return [];
                }

                // Fehlende Folgen mit iTunes-Albumnamen auflisten
                List<string> messages =
                [
                    $"Online verfügbar (nach Folge {checkResult.LocalHighestNumber}):",
                    string.Empty
                ];

                foreach (MissingOnlineEpisode ep in checkResult.MissingOnlineEpisodes)
                {
                    messages.Add($"  Folge {ep.EpisodeNumber:D3} – {ep.AlbumTitle}");
                }

                return messages;
            }
            catch (Exception)
            {
                return [];
            }
            finally
            {
                _statusBar.IsTemporarilyOnline = false;
            }
        }

        /// <summary>
        /// Prüft alle abonnierten Serien mit lokalem Ordner auf fehlende Folgen.
        /// Kombiniert lokale Lückenanalyse mit Live-Online-Abgleich per iTunes API.
        /// Zeigt Fortschritt in der StatusBar und feuert <see cref="AllSeriesCheckCompleted"/> am Ende.
        /// </summary>
        public async Task CheckAllSeriesAsync()
        {
            // Gleicher Drei-Optionen-Dialog wie bei der Einzelserien-Prüfung
            MissingEpisodesMode mode = MissingEpisodesMode.OfflineOnly;
            if (MissingEpisodesModeRequested is not null)
            {
                mode = await MissingEpisodesModeRequested.Invoke();
            }

            if (mode == MissingEpisodesMode.Cancel)
            {
                return;
            }

            bool onlineAvailable = mode == MissingEpisodesMode.WithOnline;

            // Temporärer Online-Status nur wenn Online-Modus gewählt
            if (onlineAvailable)
            {
                _statusBar.IsTemporarilyOnline = true;
            }

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ISeriesDataService seriesService = scope.ServiceProvider
                    .GetRequiredService<ISeriesDataService>();

                IReadOnlyList<Series> subscribed = await seriesService.GetSubscribedAsync();

                // Nur Serien mit lokalem Ordner prüfen
                List<Series> localSeries = subscribed
                    .Where(s => !string.IsNullOrWhiteSpace(s.LocalFolderPath))
                    .OrderBy(s => s.Title)
                    .ToList();

                List<SeriesMissingEpisodesResult> results = new(localSeries.Count);

                for (int i = 0; i < localSeries.Count; i++)
                {
                    Series series = localSeries[i];
                    _statusBar.SetScanProgress($"Prüfe Serie {i + 1}/{localSeries.Count}: {series.Title} …");

                    SeriesMissingEpisodesResult result = await CheckSingleSeriesForReportAsync(
                        series, onlineAvailable);
                    results.Add(result);
                }

                MissingEpisodesReport report = new()
                {
                    CheckedAtUtc = DateTime.UtcNow,
                    Results      = results
                };

                AllSeriesCheckCompleted?.Invoke(report);
            }
            finally
            {
                if (onlineAvailable)
                {
                    _statusBar.IsTemporarilyOnline = false;
                }

                _statusBar.ClearScanProgress();
            }
        }

        /// <summary>
        /// Prüft eine einzelne Serie für den Gesamtbericht: lokale Lücken + optionaler Online-Abgleich.
        /// </summary>
        private async Task<SeriesMissingEpisodesResult> CheckSingleSeriesForReportAsync(
            Series series, bool onlineAvailable)
        {
            try
            {
                // Lokale Lücken analysieren
                List<int> gaps = [];
                int localHighest = 0;

                if (!string.IsNullOrWhiteSpace(series.LocalFolderPath)
                    && Directory.Exists(series.LocalFolderPath))
                {
                    (gaps, localHighest) = await Task.Run(
                        () => AnalyzeMissingEpisodesForReport(series.LocalFolderPath));
                }

                // Online-Abgleich wenn erlaubt
                int onlineHighest = 0;
                List<OnlineEpisodeInfo> onlineEpisodes = [];

                if (onlineAvailable && localHighest > 0)
                {
                    CheckableSeriesInfo checkable = new()
                    {
                        SeriesId           = series.Id,
                        Title              = series.Title,
                        AppleMusicArtistId = series.AppleMusicArtistId,
                        LocalFolderPath    = series.LocalFolderPath,
                        CoverImageUrl      = series.CoverImageUrl
                    };

                    IReadOnlyList<OnlineEpisodeCheckResult> checkResults =
                        await _onlineEpisodeChecker.CheckAllAsync([checkable]);

                    if (checkResults.Count > 0)
                    {
                        OnlineEpisodeCheckResult cr = checkResults[0];
                        onlineHighest = cr.OnlineHighestNumber;

                        // iTunes-Albumnamen für fehlende Folgen übernehmen
                        foreach (MissingOnlineEpisode ep in cr.MissingOnlineEpisodes)
                        {
                            onlineEpisodes.Add(new OnlineEpisodeInfo
                            {
                                EpisodeNumber = ep.EpisodeNumber,
                                Title         = ep.AlbumTitle
                            });
                        }
                    }
                }

                return new SeriesMissingEpisodesResult
                {
                    SeriesTitle        = series.Title,
                    LocalHighestNumber = localHighest,
                    OnlineHighestNumber = onlineHighest,
                    LocalGaps          = gaps,
                    OnlineEpisodes     = onlineEpisodes
                };
            }
            catch (Exception ex)
            {
                return new SeriesMissingEpisodesResult
                {
                    SeriesTitle        = series.Title,
                    LocalGaps          = [],
                    OnlineEpisodes     = [],
                    ErrorMessage       = ex.Message
                };
            }
        }

        /// <summary>
        /// Analysiert lokale Lücken und gibt sowohl die Lücken als auch die höchste Nummer zurück.
        /// Für den Gesamtbericht – liefert strukturierte Daten statt formatierter Strings.
        /// </summary>
        /// <summary>
        /// Filtert die Serien-Kacheln anhand des <see cref="LocalSearchText"/>.
        /// Leerer Suchtext zeigt alle Serien. Filtert clientseitig – kein DB-Query.
        /// </summary>
        private void ApplyLocalSearchFilter()
        {
            if (string.IsNullOrWhiteSpace(_localSearchText))
            {
                Artists = _allArtists;
                return;
            }

            List<LocalArtistCardViewModel> filtered = [];
            foreach (LocalArtistCardViewModel card in _allArtists)
            {
                if (card.Title.Contains(_localSearchText, StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add(card);
                }
            }

            Artists = filtered;
        }

        private static (List<int> Gaps, int MaxNumber) AnalyzeMissingEpisodesForReport(string seriesFolderPath)
        {
            string[] subfolders;
            try
            {
                subfolders = Directory.GetDirectories(seriesFolderPath);
            }
            catch
            {
                return ([], 0);
            }

            // Ordner mit Audiodateien sammeln
            List<string> episodeFolderNames = [];
            foreach (string folder in subfolders)
            {
                try
                {
                    bool hasAudio = Directory
                        .GetFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Any(EchoPlay.Core.AudioExtensions.IsAudioFile);

                    if (hasAudio)
                    {
                        string? name = Path.GetFileName(folder);
                        if (name is not null)
                        {
                            episodeFolderNames.Add(name);
                        }
                    }
                }
                catch { /* Einzelner Ordner nicht lesbar – überspringen */ }
            }

            if (episodeFolderNames.Count == 0) return ([], 0);

            // Bestes Muster erkennen
            EpisodeFolderParser[] candidateParsers =
            [
                new("{number:000} - {title}"),
                new("{*} - {number:000} - {title}"),
                new("Folge {number:000} - {title}"),
                new("{number:000}_{title}"),
                new("{number} - {title}"),
                new("{title} - {number:000}"),
                new("{number:000} {title}"),
                new("{*} - {number} - {title}")
            ];

            EpisodeFolderParser? bestParser = null;
            int bestMatchCount = 0;

            foreach (EpisodeFolderParser parser in candidateParsers)
            {
                int matchCount = episodeFolderNames
                    .Count(name => parser.TryParse(name, out int? num, out _) && num is > 0);

                if (matchCount > bestMatchCount)
                {
                    bestMatchCount = matchCount;
                    bestParser = parser;
                }
            }

            if (bestParser is null) return ([], 0);

            // Nummern extrahieren
            HashSet<int> foundNumbers = new();
            int maxNumber = 0;

            foreach (string name in episodeFolderNames)
            {
                if (bestParser.TryParse(name, out int? number, out _) && number is > 0)
                {
                    foundNumbers.Add(number.Value);
                    if (number.Value > maxNumber) maxNumber = number.Value;
                }
            }

            // Lücken finden
            List<int> gaps = [];
            for (int i = 1; i <= maxNumber; i++)
            {
                if (!foundNumbers.Contains(i)) gaps.Add(i);
            }

            return (gaps, maxNumber);
        }

        /// <summary>
        /// Prüft in der Datenbank, ob für die Serie Episoden existieren, die eine höhere
        /// Folgennummer haben als die höchste lokal vorhandene. Diese Folgen sind online
        /// bekannt, fehlen aber lokal.
        /// </summary>
        /// <param name="seriesId">ID der zu prüfenden Serie.</param>
        /// <returns>Meldungen für den Dialog oder leere Liste wenn nichts fehlt.</returns>
        private async Task<List<string>> AnalyzeOnlineMissingEpisodesAsync(Guid seriesId)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();

            int? highestLocal = await episodeService.GetHighestLocalEpisodeNumberAsync(seriesId);

            if (highestLocal is null)
            {
                return [];
            }

            // Alle DB-Episoden ohne lokalen Ordner, die eine höhere Nummer haben
            IReadOnlyList<Episode> missingOnline = await episodeService.GetMissingLocalEpisodesAsync(seriesId);
            List<Episode> newOnline = missingOnline
                .Where(e => e.EpisodeNumber is not null && e.EpisodeNumber > highestLocal)
                .OrderBy(e => e.EpisodeNumber)
                .ToList();

            if (newOnline.Count == 0)
            {
                return [];
            }

            List<string> messages =
            [
                $"Online verfügbar (nach Folge {highestLocal}):",
                string.Empty
            ];

            foreach (Episode episode in newOnline)
            {
                string title = !string.IsNullOrWhiteSpace(episode.Title) ? $" – {episode.Title}" : string.Empty;
                messages.Add($"  Folge {episode.EpisodeNumber:D3}{title}");
            }

            return messages;
        }

        /// <summary>
        /// Analysiert den Serienordner auf fehlende Folgen.
        /// Läuft im Thread-Pool – darf keine UI-Elemente anfassen.
        /// </summary>
        /// <param name="seriesFolderPath">Absoluter Pfad zum Serienordner.</param>
        /// <returns>Liste der Meldungen für den Dialog.</returns>
        private static List<string> AnalyzeMissingEpisodes(string seriesFolderPath)
        {
            // ── Schritt 1: Alle Unterordner mit Audiodateien sammeln ────────────
            string[] subfolders;
            try
            {
                subfolders = Directory.GetDirectories(seriesFolderPath);
            }
            catch (IOException)
            {
                return ["Ordner konnte nicht gelesen werden."];
            }
            catch (UnauthorizedAccessException)
            {
                return ["Zugriff auf den Ordner verweigert."];
            }

            // Nur Ordner mit mindestens einer Audiodatei sind echte Folgen.
            // Jubiläumsfolgen (z.B. 100, 125, 150, 200) sind oft Mehrteiler –
            // die Audio-Dateien liegen dort in Unterordnern (CD1, Teil A etc.),
            // deshalb wird rekursiv gesucht (SearchOption.AllDirectories).
            List<string> episodeFolderNames = [];
            foreach (string folder in subfolders)
            {
                try
                {
                    bool hasAudio = Directory
                        .GetFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Any(EchoPlay.Core.AudioExtensions.IsAudioFile);

                    if (hasAudio)
                    {
                        string? name = Path.GetFileName(folder);
                        if (name is not null)
                        {
                            episodeFolderNames.Add(name);
                        }
                    }
                }
                catch (IOException) { /* Einzelner Ordner nicht lesbar – Rest weiterscannen */ }
                catch (UnauthorizedAccessException) { /* Kein Zugriff auf Ordner – überspringen */ }
            }

            if (episodeFolderNames.Count == 0)
            {
                return ["Keine Folgenordner mit Audiodateien gefunden."];
            }

            // ── Schritt 2: Hauptmuster der Serie erkennen ────────────────────
            // Das Muster mit den meisten Treffern (Nummer > 0) gewinnt.
            // Wichtig: nur EIN Muster verwenden, damit Sonderfolgen wie
            // "000 - Planetarium - 001 - Titel" nicht fälschlich als Folge 001 gezählt werden
            // (der Parser "{*} - {number:000} - {title}" würde dort 001 extrahieren).
            EpisodeFolderParser[] candidateParsers =
            [
                new("{number:000} - {title}"),
                new("{*} - {number:000} - {title}"),
                new("Folge {number:000} - {title}"),
                new("{number:000}_{title}"),
                new("{number} - {title}"),
                new("{title} - {number:000}"),
                new("{number:000} {title}"),
                new("{*} - {number} - {title}")
            ];

            EpisodeFolderParser? bestParser = null;
            int bestMatchCount = 0;

            foreach (EpisodeFolderParser parser in candidateParsers)
            {
                int matchCount = episodeFolderNames
                    .Count(name => parser.TryParse(name, out int? num, out _) && num is > 0);

                if (matchCount > bestMatchCount)
                {
                    bestMatchCount = matchCount;
                    bestParser = parser;
                }
            }

            if (bestParser is null || bestMatchCount == 0)
            {
                return [$"{episodeFolderNames.Count} Folgen vorhanden (keine Nummerierung erkannt)."];
            }

            // ── Schritt 3: Nummern mit dem Hauptmuster extrahieren ──────────────
            HashSet<int> foundNumbers = new();
            int maxNumber = 0;

            foreach (string name in episodeFolderNames)
            {
                if (bestParser.TryParse(name, out int? number, out _) && number is > 0)
                {
                    foundNumbers.Add(number.Value);
                    if (number.Value > maxNumber)
                    {
                        maxNumber = number.Value;
                    }
                }
                // Ordner passt nicht oder hat Nummer 0 → Sonderfolge, existiert auf Platte.
                // Nicht melden – es geht nur um fehlende Folgen.
            }

            // ── Schritt 3: Lücken finden ────────────────────────────────────────

            List<int> gaps = [];
            for (int i = 1; i <= maxNumber; i++)
            {
                if (!foundNumbers.Contains(i))
                {
                    gaps.Add(i);
                }
            }

            if (gaps.Count == 0)
            {
                return [$"Alle Folgen vorhanden (1–{maxNumber}), keine Lücken."];
            }

            List<string> messages =
            [
                $"{gaps.Count} fehlende Folge(n) von {maxNumber}:",
                string.Empty
            ];

            foreach (int gap in gaps)
            {
                messages.Add($"  Folge {gap:D3}");
            }

            return messages;
        }

        /// <summary>
        /// Lädt Cover in 60er-Chargen sequenziell nach. Jede Charge wird komplett abgearbeitet
        /// bevor die nächste startet. Erzeugt einen Infinite-Scrolling-Effekt: Cover erscheinen blockweise.
        /// Bei Abbruch (Serienwechsel) wird sofort aufgehört.
        /// </summary>
        private async Task LoadCoversBatchedAsync(
            List<(LocalEpisodeCardViewModel Card, Episode Episode)> queue,
            CancellationToken cancellationToken)
        {
            try
            {
                int batchSize = 60;

                for (int offset = 0; offset < queue.Count; offset += batchSize)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    int count = Math.Min(batchSize, queue.Count - offset);
                    List<(LocalEpisodeCardViewModel Card, Episode Episode)> batch =
                        queue.GetRange(offset, count);

                    await LoadCoversThrottledAsync(batch, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Erwarteter Abbruch bei Seitenwechsel
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Hintergrund-Cover-Laden fehlgeschlagen: {ex.Message}");
            }
        }

        /// <summary>
        /// Lädt die Cover aller Episodenkarten mit begrenzter Parallelität.
        /// Die Byte-Daten werden auf Hintergrundthreads geladen (IO-bound),
        /// die BitmapImage-Erstellung erfolgt auf dem UI-Thread (WinRT-COM-Pflicht).
        /// Maximal 8 Cover werden gleichzeitig geladen, damit der UI-Thread nicht
        /// mit hunderten PropertyChanged-Notifications gleichzeitig überflutet wird.
        /// </summary>
        private async Task LoadCoversThrottledAsync(
            List<(LocalEpisodeCardViewModel Card, Episode Episode)> coverQueue,
            CancellationToken cancellationToken)
        {
            // 8 parallele Ladevorgänge – genug für flüssiges Nachladen, ohne den UI-Thread zu überlasten
            SemaphoreSlim throttle = new(8);
            List<Task> tasks = new(coverQueue.Count);

            foreach ((LocalEpisodeCardViewModel card, Episode episode) in coverQueue)
            {
                if (cancellationToken.IsCancellationRequested) break;

                await throttle.WaitAsync(cancellationToken);

                // Byte-Laden auf Hintergrundthread, BitmapImage-Erstellung danach auf UI-Thread.
                // Task.Run ist nötig, weil File.Exists und TagLib# synchron blockieren würden.
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        byte[]? bytes = await LoadCoverBytesAsync(episode);

                        if (bytes is not null && !cancellationToken.IsCancellationRequested)
                        {
                            // BitmapImage ist ein WinRT-COM-Objekt – Erstellung nur auf dem UI-Thread erlaubt.
                            // Ohne dieses Marshalling schlägt SetSourceAsync mit einer COM-Exception fehl,
                            // die der catch-Block verschluckt und das Cover bleibt ein Platzhalter.
                            if (_dispatcherQueue is not null)
                            {
                                TaskCompletionSource tcs = new();

                                // TryEnqueue gibt false zurück wenn der Dispatcher herunterfährt –
                                // ohne Prüfung würde die TaskCompletionSource nie abgeschlossen
                                // und der Task hängt ewig.
                                bool enqueued = _dispatcherQueue.TryEnqueue(async () =>
                                {
                                    try
                                    {
                                        card.CoverImage = await EchoPlay.App.Services.CoverService.ConvertToBitmapAsync(bytes);
                                    }
                                    catch
                                    {
                                        // Cover-Fehler auf dem UI-Thread – Platzhalter bleibt stehen
                                    }
                                    finally
                                    {
                                        tcs.SetResult();
                                    }
                                });

                                if (!enqueued)
                                {
                                    // Dispatcher fährt runter – Cover-Laden abbrechen
                                    return;
                                }

                                await tcs.Task;
                            }
                            else
                            {
                                // Unit-Tests ohne Dispatcher – direkt setzen
                                card.CoverImage = await EchoPlay.App.Services.CoverService.ConvertToBitmapAsync(bytes);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Serienwechsel – erwarteter Abbruch
                    }
                    catch
                    {
                        // Cover-Laden darf die UI nicht blockieren – Platzhalter bleibt stehen
                    }
                    finally
                    {
                        throttle.Release();
                    }
                }, cancellationToken));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Serienwechsel – erwarteter Abbruch
            }

            throttle.Dispose();
        }

        /// <summary>
        /// Lädt die rohen Cover-Bytes einer Episode (threadpool-safe, kein UI-Zugriff).
        /// Priorität: DB-Cover → cover.jpg im Ordner → ID3-Tag des ersten Tracks.
        /// </summary>
        /// <param name="episode">Die Episode, deren Cover geladen werden soll.</param>
        /// <returns>Rohe Bilddaten oder <see langword="null"/> wenn kein Cover vorhanden.</returns>
        private async Task<byte[]?> LoadCoverBytesAsync(Episode episode)
        {
            // DB-Cover über CoverService laden (CoverImages-Tabelle)
            if (_coverService is not null)
            {
                IReadOnlyDictionary<Guid, byte[]> coverMap =
                    await _coverService.GetEpisodeCoverBytesAsync([episode.Id]);
                if (coverMap.TryGetValue(episode.Id, out byte[]? dbBytes))
                {
                    return dbBytes;
                }
            }

            // Ersten Track für den ID3-Fallback ermitteln – nur wenn cover.jpg fehlt
            string? firstTrackPath = null;

            if (episode.LocalFolderPath is not null &&
                !File.Exists(Path.Combine(episode.LocalFolderPath, Core.CoverConstants.CoverFileName)))
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ILocalTrackDataService trackService = scope.ServiceProvider
                    .GetRequiredService<ILocalTrackDataService>();

                IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episode.Id);
                firstTrackPath = tracks.OrderBy(t => t.TrackNumber).FirstOrDefault()?.FilePath;
            }

            return await _coverLoader.LoadAsync(episode.LocalFolderPath, firstTrackPath);
        }

        /// <summary>
        /// Lädt das Cover einer Episode asynchron und setzt es auf der Karte.
        /// Wird für die erste Charge (max. 60 Kacheln) direkt auf dem UI-Thread aufgerufen,
        /// daher ist BitmapImage-Erstellung hier sicher.
        /// Fehler werden still ignoriert – fehlende Cover sind kein kritisches Problem.
        /// </summary>
        private async Task LoadCoverForEpisodeCardAsync(LocalEpisodeCardViewModel card, Episode episode)
        {
            try
            {
                byte[]? bytes = await LoadCoverBytesAsync(episode);

                if (bytes is not null)
                {
                    card.CoverImage = await EchoPlay.App.Services.CoverService.ConvertToBitmapAsync(bytes);
                }
            }
            catch
            {
                // Cover-Laden darf die UI nicht blockieren – fehlende Cover zeigen den Platzhalter
            }
        }

        /// <summary>
        /// Speichert Cover-Bytes als cover.jpg im angegebenen Ordner,
        /// wenn die AppSettings-Option <c>SaveCoverToDirectory</c> aktiv ist.
        /// Fehler werden still ignoriert – die DB-Speicherung hat bereits stattgefunden.
        /// </summary>
        private static async Task SaveCoverToDirectoryAsync(IServiceScope scope, string? folderPath, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return;
            }

            try
            {
                IAppSettingsDataService settingsService = scope.ServiceProvider
                    .GetRequiredService<IAppSettingsDataService>();
                Data.Entities.Settings.AppSettings settings = await settingsService.GetAsync();

                if (!settings.SaveCoverToDirectory)
                {
                    return;
                }

                string coverPath = Path.Combine(folderPath, Core.CoverConstants.CoverFileName);
                await File.WriteAllBytesAsync(coverPath, bytes);
            }
            catch
            {
                // Datei-Speicherung ist optional – DB hat das Cover bereits
            }
        }

        // ── Episoden-Status ──────────────────────────────────────────────────────

        /// <summary>
        /// Markiert eine Episode als vollständig gehört.
        /// Setzt <c>IsCompleted = true</c> und <c>CompletedAt = DateTime.UtcNow</c> im PlaybackState.
        /// Legt einen neuen PlaybackState an, falls noch keiner existiert.
        /// </summary>
        /// <param name="episodeId">Die ID der zu markierenden Episode.</param>
        public async Task MarkEpisodeAsPlayedAsync(Guid episodeId)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            Data.Services.Interfaces.IPlaybackStateDataService stateService =
                scope.ServiceProvider.GetRequiredService<Data.Services.Interfaces.IPlaybackStateDataService>();

            Data.Entities.Playback.PlaybackState? existing = await stateService.GetByEpisodeIdAsync(episodeId);

            if (existing is not null)
            {
                existing.IsCompleted = true;
                existing.CompletedAt = DateTime.UtcNow;
                await stateService.UpdateAsync(existing);
            }
            else
            {
                Data.Entities.Playback.PlaybackState newState = new()
                {
                    EpisodeId = episodeId,
                    IsCompleted = true,
                    CompletedAt = DateTime.UtcNow,
                    LastPlayedAt = DateTime.UtcNow
                };
                await stateService.AddAsync(newState);
            }

            // Kachel sofort aktualisieren – ohne Serienwechsel und Rückkehr.
            // Haken erscheint per PropertyChanged auf IsCompleted → CompletedCheckVisibility.
            _completedEpisodeIds.Add(episodeId);
            _inProgressEpisodeIds.Remove(episodeId);
            LocalEpisodeCardViewModel? card = _allEpisodes.FirstOrDefault(c => c.EpisodeId == episodeId);
            if (card is not null)
            {
                card.IsCompleted = true;
            }
        }

        /// <summary>
        /// Markiert eine Episode als ungehört.
        /// Löscht den PlaybackState vollständig aus der Datenbank.
        /// </summary>
        /// <param name="episodeId">Die ID der Episode.</param>
        public async Task MarkEpisodeAsUnplayedAsync(Guid episodeId)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            Data.Services.Interfaces.IPlaybackStateDataService stateService =
                scope.ServiceProvider.GetRequiredService<Data.Services.Interfaces.IPlaybackStateDataService>();

            Data.Entities.Playback.PlaybackState? existing = await stateService.GetByEpisodeIdAsync(episodeId);

            if (existing is not null)
            {
                await stateService.DeleteAsync(existing.Id);
            }

            // Kachel sofort aktualisieren – Haken verschwindet per PropertyChanged.
            _completedEpisodeIds.Remove(episodeId);
            _inProgressEpisodeIds.Remove(episodeId);
            LocalEpisodeCardViewModel? card = _allEpisodes.FirstOrDefault(c => c.EpisodeId == episodeId);
            if (card is not null)
            {
                card.IsCompleted = false;
            }
        }

        // ── Ordnerstruktur-Assistent ─────────────────────────────────────────────

        /// <summary>
        /// Analysiert den Serienordner und erstellt eine Vorschau für den Ordnerstruktur-Umbau.
        /// Löst <see cref="RestructurePreviewReady"/> aus, wenn verschiebare Dateien gefunden wurden.
        /// </summary>
        /// <param name="seriesId">ID der Serie, deren Ordner analysiert werden soll.</param>
        public async Task AnalyzeRestructureAsync(Guid seriesId)
        {
            LocalArtistCardViewModel? card = _artists.FirstOrDefault(a => a.SeriesId == seriesId);

            if (card?.LocalFolderPath is null || !Directory.Exists(card.LocalFolderPath))
            {
                return;
            }

            // Ordnermuster aus den AppSettings laden (oder Serie-spezifisches Muster)
            using IServiceScope settingsScope = _scopeFactory.CreateScope();
            Data.Services.Interfaces.IAppSettingsDataService settingsService =
                settingsScope.ServiceProvider.GetRequiredService<Data.Services.Interfaces.IAppSettingsDataService>();
            Data.Entities.Settings.AppSettings appSettings = await settingsService.GetAsync();
            string folderPattern = appSettings.EpisodeFolderPattern;

            // Dateisystem-Zugriff im Thread-Pool
            EchoPlay.LocalLibrary.Models.RestructurePreview preview = await Task.Run(() =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                EchoPlay.LocalLibrary.Abstractions.IFolderRestructureService service =
                    scope.ServiceProvider.GetRequiredService<EchoPlay.LocalLibrary.Abstractions.IFolderRestructureService>();

                return service.Analyze(card.LocalFolderPath, folderPattern);
            });

            if (preview.IsEmpty)
            {
                return;
            }

            RestructurePreviewReady?.Invoke(preview);
        }

        /// <summary>
        /// Führt den Ordnerstruktur-Umbau aus und löst danach einen Neu-Scan der Bibliothek aus.
        /// </summary>
        /// <param name="preview">Die zuvor erstellte Vorschau.</param>
        /// <returns>Anzahl der verschobenen Dateien.</returns>
        public async Task<int> ExecuteRestructureAsync(EchoPlay.LocalLibrary.Models.RestructurePreview preview)
        {
            int movedCount = await Task.Run(() =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                EchoPlay.LocalLibrary.Abstractions.IFolderRestructureService service =
                    scope.ServiceProvider.GetRequiredService<EchoPlay.LocalLibrary.Abstractions.IFolderRestructureService>();

                return service.Execute(preview);
            });

            return movedCount;
        }

        // ── Cover-Verwaltung ──────────────────────────────────────────────────────

        /// <summary>
        /// Speichert die übergebenen Bytes als Serien-Cover in der DB und aktualisiert die Kachel sofort.
        /// Hat die Serie bereits ein Cover, wird der Nutzer vorher um Bestätigung gebeten.
        /// </summary>
        /// <param name="card">Die Serienkachel, deren Cover aktualisiert werden soll.</param>
        /// <param name="bytes">Die rohen Bilddaten des neuen Covers.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task ApplySeriesCoverFromBytesAsync(LocalArtistCardViewModel card, byte[] bytes)
        {
            if (card.CoverImage is not null)
            {
                bool confirmed = await _confirmationDialogService.ConfirmAsync(
                    "Cover überschreiben",
                    "Diese Serie hat bereits ein Cover. Soll es ersetzt werden?");

                if (!confirmed)
                {
                    return;
                }
            }

            // Cover über CoverService in der CoverImages-Tabelle speichern
            if (_coverService is not null)
            {
                await _coverService.SetSeriesCoverAsync(card.SeriesId, bytes);
            }
            else
            {
                // Fallback ohne CoverService (Tests)
                using IServiceScope fallbackScope = _scopeFactory.CreateScope();
                ISeriesDataService seriesService = fallbackScope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                await seriesService.SetLocalCoverAsync(card.SeriesId, bytes);
            }

            // Cover zusätzlich als Datei im Serienordner speichern (wenn Einstellung aktiv)
            using IServiceScope scope = _scopeFactory.CreateScope();
            await SaveCoverToDirectoryAsync(scope, card.LocalFolderPath, bytes);

            card.CoverImage = await EchoPlay.App.Services.CoverService.ConvertToBitmapAsync(bytes);
        }

        /// <summary>
        /// Prüft den Offline-Modus und zeigt bei Bedarf einen Bestätigungsdialog.
        /// Muss von der Page aufgerufen werden, bevor der Cover-Such-Dialog geöffnet wird.
        /// </summary>
        /// <returns>
        /// Ein <see cref="IDisposable"/>, das den temporären Online-Status beim Dispose beendet.
        /// <see langword="null"/> wenn der Nutzer die Aktion abgelehnt hat.
        /// </returns>
        public Task<IDisposable?> RequestOnlineAccessForCoverSearchAsync()
            => _onlineAccessGuard.RequestOnlineAccessAsync();

        /// <summary>
        /// Sucht Cover-Kandidaten für den angegebenen Begriff im Cover Art Archive.
        /// Wird von der Page direkt aufgerufen, um die Suchergebnisse im Dialog anzuzeigen –
        /// ohne den Event-basierten Umweg über <see cref="SeriesCoverSearchResultsReady"/>.
        /// </summary>
        /// <param name="query">Suchbegriff, z.B. Serien- oder Episodentitel.</param>
        /// <param name="ct">Abbruchtoken, z.B. für Dialog-Schließen.</param>
        /// <returns>Liste der Treffer, leer wenn nichts gefunden wurde.</returns>
        public Task<IReadOnlyList<CoverSearchResult>> SearchCoversAsync(string query, CancellationToken ct)
            => _coverSearchService.SearchAsync(query, ct);

        /// <summary>
        /// Übernimmt einen Cover-Kandidaten direkt für die übergebene Serienkachel.
        /// Dieser Overload wird vom neuen Dialog-basierten Flow aufgerufen, bei dem die
        /// Page die Karte selbst verwaltet – ohne den <see cref="_pendingSeriesCoverCard"/>-Mechanismus.
        /// </summary>
        /// <param name="card">Die Serienkachel, für die das Cover gesetzt werden soll.</param>
        /// <param name="result">Der vom Nutzer gewählte Cover-Kandidat.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task ApplySelectedSeriesCoverAsync(LocalArtistCardViewModel card, CoverSearchResult result)
        {
            byte[]? bytes = await DownloadCoverBytesAsync(result.FullUrl);

            if (bytes is null)
            {
                await _errorDialogService.ShowAsync(
                    "Download fehlgeschlagen",
                    "Das Cover konnte nicht heruntergeladen werden. Bitte versuche es später erneut.");
                return;
            }

            await ApplySeriesCoverFromBytesAsync(card, bytes);
        }

        /// <summary>
        /// Übernimmt einen Cover-Kandidaten direkt für die übergebene Episodenkachel.
        /// Entspricht dem Serien-Overload, aber für Episoden.
        /// </summary>
        /// <param name="card">Die Episodenkachel, für die das Cover gesetzt werden soll.</param>
        /// <param name="result">Der vom Nutzer gewählte Cover-Kandidat.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task ApplySelectedEpisodeCoverAsync(LocalEpisodeCardViewModel card, CoverSearchResult result)
        {
            byte[]? bytes = await DownloadCoverBytesAsync(result.FullUrl);

            if (bytes is null)
            {
                await _errorDialogService.ShowAsync(
                    "Download fehlgeschlagen",
                    "Das Cover konnte nicht heruntergeladen werden. Bitte versuche es später erneut.");
                return;
            }

            await ApplyEpisodeCoverFromBytesAsync(card, bytes);
        }

        /// <summary>
        /// Startet eine Online-Cover-Suche für eine Serie über das Cover Art Archive.
        /// Bei Treffern werden diese über <see cref="SeriesCoverSearchResultsReady"/> an die Page
        /// übergeben, die den Kachelauswahl-Dialog öffnet.
        /// Die Referenz auf <paramref name="card"/> wird in <see cref="_pendingSeriesCoverCard"/>
        /// zwischengespeichert, damit <see cref="ApplySelectedSeriesCoverAsync(CoverSearchResult)"/> die richtige
        /// Kachel nach dem Dialog-Abschluss aktualisieren kann.
        /// </summary>
        /// <param name="card">Die Serienkachel, für die ein Cover gesucht werden soll.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task SearchAndApplySeriesCoverAsync(LocalArtistCardViewModel card)
        {
            _pendingSeriesCoverCard = card;

            IReadOnlyList<CoverSearchResult> results =
                await _coverSearchService.SearchAsync(card.Title, CancellationToken.None);

            if (results.Count == 0)
            {
                _pendingSeriesCoverCard = null;
                await _errorDialogService.ShowAsync(
                    "Keine Cover gefunden",
                    $"Für \"{card.Title}\" wurden im Cover Art Archive keine Treffer gefunden.");
                return;
            }

            SeriesCoverSearchResultsReady?.Invoke(this, results);
        }

        /// <summary>
        /// Übernimmt den vom Nutzer im Auswahl-Dialog gewählten Cover-Kandidaten für die
        /// zwischengespeicherte Serie.
        /// Muss von der Page nach dem Schließen des Auswahl-Dialogs aufgerufen werden.
        /// Ohne ausstehende Karte (z.B. bei unerwartetem parallelem Aufruf) wird still abgebrochen.
        /// </summary>
        /// <param name="result">Der vom Nutzer gewählte Cover-Kandidat.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task ApplySelectedSeriesCoverAsync(CoverSearchResult result)
        {
            if (_pendingSeriesCoverCard is null)
            {
                return;
            }

            LocalArtistCardViewModel card = _pendingSeriesCoverCard;
            _pendingSeriesCoverCard = null;

            byte[]? bytes = await DownloadCoverBytesAsync(result.FullUrl);

            if (bytes is null)
            {
                await _errorDialogService.ShowAsync(
                    "Download fehlgeschlagen",
                    "Das Cover konnte nicht heruntergeladen werden. Bitte versuche es später erneut.");
                return;
            }

            await ApplySeriesCoverFromBytesAsync(card, bytes);
        }

        /// <summary>
        /// Speichert die übergebenen Bytes als Episoden-Cover in der DB und aktualisiert die Kachel sofort.
        /// Hat die Episode bereits ein Cover, wird der Nutzer vorher um Bestätigung gebeten.
        /// </summary>
        /// <param name="card">Die Episodenkachel, deren Cover aktualisiert werden soll.</param>
        /// <param name="bytes">Die rohen Bilddaten des neuen Covers.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task ApplyEpisodeCoverFromBytesAsync(LocalEpisodeCardViewModel card, byte[] bytes)
        {
            if (card.CoverImage is not null)
            {
                bool confirmed = await _confirmationDialogService.ConfirmAsync(
                    "Cover überschreiben",
                    "Diese Folge hat bereits ein Cover. Soll es ersetzt werden?");

                if (!confirmed)
                {
                    return;
                }
            }

            // Cover über CoverService in der CoverImages-Tabelle speichern
            if (_coverService is not null)
            {
                await _coverService.SetEpisodeCoverAsync(card.EpisodeId, bytes);
            }
            else
            {
                // Fallback ohne CoverService (Tests)
                using IServiceScope fallbackScope = _scopeFactory.CreateScope();
                IEpisodeDataService episodeService = fallbackScope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                await episodeService.SetLocalCoverAsync(card.EpisodeId, bytes);
            }

            // Cover zusätzlich als Datei im Episodenordner speichern
            using IServiceScope scope = _scopeFactory.CreateScope();
            await SaveCoverToDirectoryAsync(scope, card.FolderPath, bytes);

            card.CoverImage = await EchoPlay.App.Services.CoverService.ConvertToBitmapAsync(bytes);
        }

        /// <summary>
        /// Startet eine Online-Cover-Suche für eine Episode über das Cover Art Archive.
        /// Bei Treffern werden diese über <see cref="EpisodeCoverSearchResultsReady"/> an die Page
        /// übergeben, die den Kachelauswahl-Dialog öffnet.
        /// </summary>
        /// <param name="card">Die Episodenkachel, für die ein Cover gesucht werden soll.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task SearchAndApplyEpisodeCoverAsync(LocalEpisodeCardViewModel card)
        {
            _pendingEpisodeCoverCard = card;

            IReadOnlyList<CoverSearchResult> results =
                await _coverSearchService.SearchAsync(card.Title, CancellationToken.None);

            if (results.Count == 0)
            {
                _pendingEpisodeCoverCard = null;
                await _errorDialogService.ShowAsync(
                    "Keine Cover gefunden",
                    $"Für \"{card.Title}\" wurden im Cover Art Archive keine Treffer gefunden.");
                return;
            }

            EpisodeCoverSearchResultsReady?.Invoke(this, results);
        }

        /// <summary>
        /// Übernimmt den vom Nutzer im Auswahl-Dialog gewählten Cover-Kandidaten für die
        /// zwischengespeicherte Episode.
        /// Muss von der Page nach dem Schließen des Auswahl-Dialogs aufgerufen werden.
        /// </summary>
        /// <param name="result">Der vom Nutzer gewählte Cover-Kandidat.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task ApplySelectedEpisodeCoverAsync(CoverSearchResult result)
        {
            if (_pendingEpisodeCoverCard is null)
            {
                return;
            }

            LocalEpisodeCardViewModel card = _pendingEpisodeCoverCard;
            _pendingEpisodeCoverCard = null;

            byte[]? bytes = await DownloadCoverBytesAsync(result.FullUrl);

            if (bytes is null)
            {
                await _errorDialogService.ShowAsync(
                    "Download fehlgeschlagen",
                    "Das Cover konnte nicht heruntergeladen werden. Bitte versuche es später erneut.");
                return;
            }

            await ApplyEpisodeCoverFromBytesAsync(card, bytes);
        }

        /// <summary>
        /// Lädt Bilddaten von der angegebenen URL als Byte-Array herunter.
        /// Gibt <see langword="null"/> zurück wenn der Download fehlschlägt – kein Absturz.
        /// Der <see cref="_downloadClient"/> wird als static-Feld gehalten, um Socket-Erschöpfung
        /// durch wiederholte Instanziierung zu vermeiden.
        /// </summary>
        /// <param name="url">Die Bild-URL – typischerweise vom Cover Art Archive.</param>
        /// <returns>Bilddaten oder <see langword="null"/> bei Fehler.</returns>
        private static async Task<byte[]?> DownloadCoverBytesAsync(string url)
        {
            try
            {
                return await _downloadClient.GetByteArrayAsync(url);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ── Hilfsmethoden ─────────────────────────────────────────────────────────

        /// <summary>
        /// Filtert und sortiert die Episodenliste basierend auf dem gewählten Filter und Sortierkriterium.
        /// Arbeitet rein im Speicher auf <see cref="_allEpisodes"/> – kein DB-Zugriff nötig.
        /// </summary>
        private void ApplyFilterAndSort()
        {
            // Schritt 0: Tab-Filter – reguläre Folgen vs. Sonderfolgen
            IEnumerable<LocalEpisodeCardViewModel> tabFiltered = _episodeTabIndex == 1
                ? _allEpisodes.Where(e => e.IsSpecialEpisode)
                : _allEpisodes.Where(e => !e.IsSpecialEpisode);

            // Schritt 1: Status-Filter
            IEnumerable<LocalEpisodeCardViewModel> filtered = _episodeFilterIndex switch
            {
                1 => tabFiltered.Where(e => !_completedEpisodeIds.Contains(e.EpisodeId)
                                             && !_inProgressEpisodeIds.Contains(e.EpisodeId)),
                2 => tabFiltered.Where(e => _completedEpisodeIds.Contains(e.EpisodeId)),
                3 => tabFiltered.Where(e => _inProgressEpisodeIds.Contains(e.EpisodeId)),
                _ => tabFiltered
            };

            // Schritt 2: Sortieren
            IEnumerable<LocalEpisodeCardViewModel> sorted = _episodeSortIndex switch
            {
                1 => filtered.OrderByDescending(e => e.EpisodeNumber ?? int.MaxValue),
                2 => filtered.OrderBy(e => e.Title),
                _ => filtered.OrderBy(e => e.EpisodeNumber ?? int.MaxValue)
            };

            _episodes = sorted.ToList();
            OnPropertyChanged(nameof(Episodes));
            OnPropertyChanged(nameof(EpisodesEmptyVisibility));
        }

        /// <summary>
        /// Löst die Tag-Manager-Navigation aus, wenn ein gültiger Pfad vorhanden ist.
        /// Stille Rückgabe bei null oder leerem Pfad – kein Fehler-Dialog, da der Button
        /// nur sichtbar ist, wenn ein Pfad bekannt ist.
        /// </summary>
        /// <param name="path">Der Ordnerpfad oder null.</param>
        private void OpenAllTracksByPath(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                RequestTagManagerNavigation(path);
            }
        }

        /// <summary>
        /// Fügt eine DB-synchronisierte Serie sofort zur Künstlerliste hinzu.
        /// Wird vom <see cref="IScanEventService.SeriesSynced"/>-Event aufgerufen – muss auf dem UI-Thread
        /// laufen, daher Dispatch in <see cref="OnSeriesSynced"/> über <see cref="_dispatcherQueue"/>.
        /// Der Duplikat-Schutz verhindert doppelte Kacheln, wenn <see cref="LoadAsync"/> und das Event
        /// dieselbe Serie melden.
        /// Die Episodenzähler sind beim Callback-Zeitpunkt in Phase 2 bereits in der DB (Episoden werden
        /// vor dem Event-Auslösen importiert) und können sofort korrekt gesetzt werden.
        /// </summary>
        /// <param name="series">Die gerade DB-synchronisierte Serie.</param>
        private async Task AppendArtistCardAsync(Series series)
        {
            try
            {
                // Duplikat-Schutz: LoadAsync() und das ScanEvent können dieselbe Serie melden
                if (_artists.Any(a => a.SeriesId == series.Id))
                {
                    return;
                }

                BitmapImage? cover = await BuildCoverImageAsync(series);

                // Episodenzähler aus DB laden – bei Phase-2-Callback bereits vollständig angelegt
                using IServiceScope scope = _scopeFactory.CreateScope();
                IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id);
                int localEpisodeCount = episodes.Count(e => e.LocalFolderPath is not null);
                int totalEpisodeCount = episodes.Count;

                LocalArtistCardViewModel card = new(
                    seriesId:          series.Id,
                    title:             series.Title,
                    coverImage:        cover,
                    localFolderPath:   series.LocalFolderPath,
                    localEpisodeCount: localEpisodeCount,
                    totalEpisodeCount: totalEpisodeCount,
                    isFavorite:        series.IsFavorite,
                    isWatched:         series.IsWatched,
                    scopeFactory:      _scopeFactory);

                // Unveränderliche neue Liste – _allArtists ist die vollständige Sammlung,
                // Artists wird über den Suchfilter daraus abgeleitet.
                _allArtists = [.._allArtists, card];
                ApplyLocalSearchFilter();
            }
            catch
            {
                // Fehler dürfen den Scan nicht unterbrechen – die Karte erscheint beim abschließenden LoadAsync()
            }
        }

        /// <summary>
        /// Erstellt ein <see cref="BitmapImage"/> aus den Seriendaten.
        /// Priorität: DB-Cover (CoverService) → Online-URL → cover.jpg im Serienordner → null.
        /// Das Cover aus der CoverImages-Tabelle hat Vorrang, weil es bereits optimiert vorliegt.
        /// Die cover.jpg im Serienordner ist der Fallback für rein lokal importierte Serien ohne Online-Metadaten.
        /// </summary>
        private async Task<BitmapImage?> BuildCoverImageAsync(Series series)
        {
            // DB-Cover über CoverService laden (CoverImages-Tabelle)
            if (_coverService is not null)
            {
                BitmapImage? dbCover = await _coverService.GetSeriesCoverImageAsync(series.Id);
                if (dbCover is not null)
                {
                    return dbCover;
                }
            }

            if (series.CoverImageUrl is not null)
            {
                return new BitmapImage(new Uri(series.CoverImageUrl));
            }

            // Fallback: cover.jpg im Serienordner – typisch für lokal importierte Serien ohne Online-Match
            if (series.LocalFolderPath is not null)
            {
                string coverPath = Path.Combine(series.LocalFolderPath, Core.CoverConstants.CoverFileName);
                if (File.Exists(coverPath))
                {
                    byte[] coverBytes = await File.ReadAllBytesAsync(coverPath);
                    return await EchoPlay.App.Services.CoverService.ConvertToBitmapAsync(coverBytes);
                }
            }

            return null;
        }

        /// <summary>
        /// Entfernt den Seriennamen als Präfix aus dem Episodentitel, falls vorhanden.
        /// Viele Hörspiel-Ordner tragen den Seriennamen als Präfix, z.B.
        /// "Abenteurer unserer Zeit - Der Pirat" → "Der Pirat".
        /// Trennzeichen wie " - ", "- ", " -", "-" werden ebenfalls bereinigt.
        /// </summary>
        /// <param name="episodeTitle">Originaler Episodentitel (typischerweise der Ordnername).</param>
        /// <param name="seriesTitle">Name der übergeordneten Serie.</param>
        /// <returns>
        /// Bereinigter Titel ohne Serien-Präfix. Wenn der Titel nach dem Entfernen leer wäre,
        /// wird der Originaltitel zurückgegeben – ein leerer Anzeigename ist nie sinnvoll.
        /// </returns>
        private static string StripSeriesPrefix(string episodeTitle, string seriesTitle)
        {
            if (string.IsNullOrWhiteSpace(episodeTitle) || string.IsNullOrWhiteSpace(seriesTitle))
            {
                return episodeTitle;
            }

            // Prüfen ob der Episodentitel mit dem Seriennamen beginnt (Groß-/Kleinschreibung ignorieren)
            if (!episodeTitle.StartsWith(seriesTitle, StringComparison.OrdinalIgnoreCase))
            {
                return episodeTitle;
            }

            // Serien-Präfix entfernen
            string remaining = episodeTitle[seriesTitle.Length..];

            // Führende Trennzeichen und Leerzeichen bereinigen:
            // " - ", "- ", " -", "-", "_", "–" (Halbgeviertstrich)
            remaining = remaining.TrimStart(' ', '-', '_', '\u2013');
            remaining = remaining.TrimStart();

            // Sicherheitsprüfung: wenn nach dem Bereinigen nichts übrig bleibt,
            // den Originaltitel behalten – ein leerer Titel ist nie hilfreich
            return remaining.Length > 0 ? remaining : episodeTitle;
        }

        /// <summary>
        /// Gibt die Cover-CancellationTokenSource frei und bricht laufende Cover-Tasks ab.
        /// </summary>
        public void Dispose()
        {
            _coverCts?.Cancel();
            _coverCts?.Dispose();
        }
    }
}

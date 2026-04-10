using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
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
        private readonly IPlayerService _playerService;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly StatusBarViewModel _statusBar;
        private readonly ICoverSearchService _coverSearchService;
        private readonly IOnlineAccessGuard _onlineAccessGuard;
        private readonly IOnlineEpisodeChecker _onlineEpisodeChecker;
        private readonly IWatchToggleService? _watchToggleService;
        private readonly IPageModeGuard? _pageModeGuard;
        private readonly IFolderRestructureCoordinator? _restructureCoordinator;
        private readonly IMissingEpisodesCoordinator? _missingEpisodesCoordinator;
        private readonly IEpisodeCoverCoordinator? _coverCoordinator;


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
        /// <param name="watchToggleService">Optionaler Service für das Umschalten der Neuerscheinungs-Überwachung. In Tests <see langword="null"/>.</param>
        /// <param name="pageModeGuard">Optionaler Page-Mode-Guard – prüft den Nur-Online-Modus beim Betreten der Page. In Tests <see langword="null"/>.</param>
        /// <param name="restructureCoordinator">Optionaler App-Service für den Ordnerstruktur-Assistenten. In Tests <see langword="null"/>.</param>
        /// <param name="missingEpisodesCoordinator">Optionaler App-Service für die Fehlende-Folgen-Prüfung. In Tests <see langword="null"/>.</param>
        /// <param name="coverCoordinator">Optionaler App-Service für Cover-Suche, -Apply und -Download. In Tests <see langword="null"/>.</param>
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
            EchoPlay.App.Services.CoverService? coverService = null,
            IWatchToggleService? watchToggleService = null,
            IPageModeGuard? pageModeGuard = null,
            IFolderRestructureCoordinator? restructureCoordinator = null,
            IMissingEpisodesCoordinator? missingEpisodesCoordinator = null,
            IEpisodeCoverCoordinator? coverCoordinator = null)
        {
            _scopeFactory               = scopeFactory;
            _playerService              = playerService;
            _confirmationDialogService  = confirmationDialogService;
            _statusBar                  = statusBar;
            _coverSearchService         = coverSearchService;
            _onlineAccessGuard          = onlineAccessGuard;
            _onlineEpisodeChecker       = onlineEpisodeChecker;
            _watchToggleService         = watchToggleService;
            _pageModeGuard              = pageModeGuard;
            _restructureCoordinator     = restructureCoordinator;
            _missingEpisodesCoordinator = missingEpisodesCoordinator;
            _coverCoordinator           = coverCoordinator;

            // Sub-VM für die Episoden-Spalte – kapselt Filter, Sortierung, Cover-Laden und
            // den Gehört-Status. Events des Sub-VMs werden an die eigenen Pass-Through-Properties
            // weitergereicht, damit bestehende XAML-Bindings unverändert funktionieren.
            EpisodesVM = new LocalEpisodesViewModel(scopeFactory, coverLoader, coverService);
            EpisodesVM.PropertyChanged += OnEpisodesVmPropertyChanged;

            // Sub-VM für die Track-Spalte – kapselt Trackliste, PlayCommand und Tag-Manager-Sprünge.
            // Events werden ebenfalls an die Pass-Through-Properties weitergereicht.
            TracksVM = new LocalTracksViewModel(playerService, RequestTagManagerNavigation);
            TracksVM.PropertyChanged += OnTracksVmPropertyChanged;

            // Sub-VM für die Künstler-/Serien-Spalte – kapselt Liste, Suchfilter, Cover-Build,
            // AppendArtistCard und Auswahl-State.
            ArtistsVM = new LocalArtistsViewModel(scopeFactory, coverService);
            ArtistsVM.PropertyChanged += OnArtistsVmPropertyChanged;

            // Sub-VM für Scan, Neu-Initialisierung und Ordnerauswahl – bekommt als Callback eine
            // Referenz auf ArtistsVM.AppendArtistCardAsync, damit live eintreffende Serien sofort
            // in der lokalen Kachelgrid erscheinen.
            ScanVM = new LocalLibraryScanViewModel(
                scopeFactory,
                syncService,
                errorDialogService,
                confirmationDialogService,
                statusBar,
                scanEventService,
                series => _ = ArtistsVM.AppendArtistCardAsync(series));

            // PropertyChanged des Sub-VMs an die eigenen Pass-Through-Properties weiterreichen,
            // damit bestehende XAML-Bindings wie {x:Bind ViewModel.IsScanning} unverändert funktionieren.
            ScanVM.PropertyChanged += OnScanVmPropertyChanged;

            // ScanStarting leert die Listen, bevor ein Scan loslegt – veraltete Kacheln sollen
            // während des Scans nicht sichtbar bleiben.
            ScanVM.ScanStarting += OnScanStarting;

            // Nach Abschluss eines Scans lädt LoadAsync die Serien konsistent aus der DB nach.
            ScanVM.LibraryReloaded += LoadAsync;

            CheckAllSeriesCommand = new RelayCommand(() => _ = CheckAllSeriesAsync());
        }

        /// <summary>
        /// Leitet PropertyChanged-Events des Tracks-Sub-VMs an die eigenen Pass-Through-Properties weiter.
        /// </summary>
        private void OnTracksVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            OnPropertyChanged(e.PropertyName);
        }

        /// <summary>
        /// Leitet PropertyChanged-Events des Künstler-Sub-VMs an die eigenen Pass-Through-Properties weiter.
        /// </summary>
        private void OnArtistsVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            OnPropertyChanged(e.PropertyName);
        }

        /// <summary>
        /// Leitet PropertyChanged-Events des Scan-Sub-VMs an die eigenen Pass-Through-Properties
        /// weiter, sodass bestehende XAML-Bindings weiterhin ohne Änderung funktionieren.
        /// </summary>
        private void OnScanVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Alle Scan-relevanten Property-Namen sind identisch zwischen Top-VM und Sub-VM.
            OnPropertyChanged(e.PropertyName);
        }

        /// <summary>
        /// Leitet PropertyChanged-Events des Episoden-Sub-VMs an die eigenen Pass-Through-Properties
        /// weiter, sodass bestehende XAML-Bindings auf Episodes, EpisodeSortIndex etc. unverändert
        /// funktionieren.
        /// </summary>
        private void OnEpisodesVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Alle relevanten Property-Namen sind identisch zwischen Top-VM und Sub-VM.
            OnPropertyChanged(e.PropertyName);
        }

        /// <summary>
        /// Leert Künstler-, Episoden- und Track-Listen vor Beginn eines Scans.
        /// Verhindert, dass veraltete Kacheln während des Scans sichtbar bleiben.
        /// </summary>
        private void OnScanStarting()
        {
            ArtistsVM.Clear();
            EpisodesVM.Clear();
            TracksVM.Clear();
        }

        /// <summary>
        /// Sub-ViewModel für Scan, Neu-Initialisierung und Ordnerauswahl.
        /// </summary>
        public LocalLibraryScanViewModel ScanVM { get; }

        /// <summary>
        /// Sub-ViewModel für die Episoden-Spalte – Filter, Sortierung, Cover und Gehört-Status.
        /// </summary>
        public LocalEpisodesViewModel EpisodesVM { get; }

        /// <summary>
        /// Sub-ViewModel für die Track-Spalte – Trackliste, PlayCommand und Tag-Manager-Sprünge.
        /// </summary>
        public LocalTracksViewModel TracksVM { get; }

        /// <summary>
        /// Sub-ViewModel für die Künstler-/Serien-Spalte – Liste, Filter, Cover, Auswahl-State.
        /// </summary>
        public LocalArtistsViewModel ArtistsVM { get; }

        /// <summary>
        /// Wird vom Code-Behind beim Betreten der Seite aufgerufen. Prüft den
        /// Nur-Online-Modus und navigiert zurück, falls die lokale Mediathek deaktiviert ist.
        /// Liefert <see langword="false"/>, falls die Page nicht weiter geladen werden soll.
        /// Die Prüfung läuft über den <see cref="IPageModeGuard"/>; in Tests ohne Guard
        /// wird der Check übersprungen und die Page darf laden.
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            if (_pageModeGuard is null)
            {
                return true;
            }

            return await _pageModeGuard.EnsureLocalAccessAsync();
        }

        /// <summary>
        /// Schaltet die Neuerscheinungs-Überwachung einer lokalen Serie um und aktualisiert die Karte.
        /// Die eigentliche Logik liegt im <see cref="IWatchToggleService"/>.
        /// </summary>
        /// <param name="seriesId">ID der Serie.</param>
        /// <param name="watch">Neuer Status.</param>
        public async Task ToggleWatchAsync(Guid seriesId, bool watch)
        {
            if (_watchToggleService is null)
            {
                return;
            }

            await _watchToggleService.ToggleAsync(seriesId, watch);

            LocalArtistCardViewModel? card = ArtistsVM.AllArtists
                .FirstOrDefault(a => a.SeriesId == seriesId);
            if (card is not null)
            {
                card.IsWatched = watch;
            }
        }

        /// <summary>
        /// Aktiviert das ViewModel beim Navigieren zur Seite.
        /// Delegiert an <see cref="ScanVM"/>, damit laufende Scans auch nach einer Rücknavigation
        /// weiterhin live Kacheln einfügen.
        /// </summary>
        public void Activate()
        {
            ScanVM.Activate();
        }

        /// <summary>
        /// Deaktiviert das ViewModel beim Verlassen der Seite.
        /// Delegiert an <see cref="ScanVM"/>, um Memory-Leaks durch den Singleton-Scan-Dienst zu verhindern.
        /// </summary>
        public void Deactivate()
        {
            ScanVM.Deactivate();
        }

        // ── Bibliothek (Pass-Through zum ScanVM) ─────────────────────────────────

        /// <summary>Pfad zur lokalen Hörspielbibliothek laut AppSettings.</summary>
        public string LibraryRootPath => ScanVM.LibraryRootPath;

        /// <summary>
        /// Gibt an, ob noch kein Bibliotheksordner konfiguriert wurde.
        /// Steuert die Sichtbarkeit des Einrichtungshinweises.
        /// </summary>
        public bool NeedsLibraryFolderSetup => ScanVM.NeedsLibraryFolderSetup;

        /// <summary>Gibt an, ob gerade ein Scan-Vorgang läuft.</summary>
        public bool IsScanning => ScanVM.IsScanning;

        /// <summary>
        /// Gibt an, ob kein Scan läuft.
        /// Wird für <c>IsEnabled</c>-Bindungen der Scan-Schaltflächen verwendet,
        /// da WinUI 3 keine Negations-Konverter unterstützt.
        /// </summary>
        public bool IsNotScanning => ScanVM.IsNotScanning;

        /// <summary>
        /// Sichtbarkeit des Scan-Overlays – eingeblendet während ein Scan oder eine Neu-Initialisierung läuft.
        /// </summary>
        public Visibility IsScanningVisibility => ScanVM.IsScanningVisibility;

        /// <summary>
        /// Gibt an, ob der Fortschrittsbalken im Scan-Overlay indeterministisch angezeigt werden soll.
        /// </summary>
        public bool IsScanIndeterminate => ScanVM.IsScanIndeterminate;

        /// <summary>
        /// Fortschritt in Prozent (0–100) für den deterministischen Fortschrittsbalken im Overlay.
        /// </summary>
        public double ScanProgressPercent => ScanVM.ScanProgressPercent;

        /// <summary>
        /// Detailtext für das Scan-Overlay, z.B. "Datei 12 von 150".
        /// </summary>
        public string ScanDetailText => ScanVM.ScanDetailText;

        /// <summary>
        /// Sichtbarkeit des Detail-Texts im Scan-Overlay.
        /// </summary>
        public Visibility ScanDetailVisibility => ScanVM.ScanDetailVisibility;

        /// <summary>Statusmeldung des laufenden oder abgeschlossenen Scans.</summary>
        public string SyncStatusText => ScanVM.SyncStatusText;

        // ── Akkordeon-Daten ──────────────────────────────────────────────────────

        /// <summary>
        /// Serien mit lokalem Ordner – Pass-Through zum <see cref="ArtistsVM"/>.
        /// </summary>
        public IReadOnlyList<LocalArtistCardViewModel> Artists => ArtistsVM.Artists;

        /// <summary>
        /// Freitext-Suchfilter – Pass-Through zum <see cref="ArtistsVM"/>.
        /// </summary>
        public string LocalSearchText
        {
            get => ArtistsVM.LocalSearchText;
            set => ArtistsVM.LocalSearchText = value;
        }

        /// <summary>
        /// Episoden der gewählten Serie – Pass-Through zum <see cref="EpisodesVM"/>.
        /// </summary>
        public IReadOnlyList<LocalEpisodeCardViewModel> Episodes => EpisodesVM.Episodes;

        /// <summary>
        /// Tracks der gewählten Episode – Pass-Through zum <see cref="TracksVM"/>.
        /// </summary>
        public IReadOnlyList<LocalTrackRowViewModel> Tracks => TracksVM.Tracks;

        /// <summary>Aktuell gewählte Serie – Pass-Through zum <see cref="ArtistsVM"/>.</summary>
        public LocalArtistCardViewModel? SelectedArtist => ArtistsVM.SelectedArtist;

        /// <summary>Aktuell gewählte Episode – Pass-Through zum <see cref="TracksVM"/>.</summary>
        public LocalEpisodeCardViewModel? SelectedEpisode => TracksVM.SelectedEpisode;

        /// <summary>
        /// Index der ausgewählten Serie – Pass-Through zum <see cref="ArtistsVM"/>.
        /// </summary>
        public int SelectedArtistIndex => ArtistsVM.SelectedArtistIndex;

        /// <summary>
        /// Sichtbarkeit des Folgen-Akkordeons – Pass-Through zum <see cref="ArtistsVM"/>.
        /// </summary>
        public Visibility EpisodesAccordionVisibility => ArtistsVM.EpisodesAccordionVisibility;

        /// <summary>
        /// Sichtbarkeit des Track-Panels rechts neben den Folgen-Kacheln.
        /// Pass-Through zum <see cref="TracksVM"/>.
        /// </summary>
        public Visibility TracksAccordionVisibility => TracksVM.EpisodeAccordionVisibility;

        // ── Sichtbarkeits-Helfer ─────────────────────────────────────────────────

        /// <summary>
        /// Aktuell gewählte Sortierung der Episodenliste. Pass-Through zum <see cref="EpisodesVM"/>.
        /// </summary>
        public int EpisodeSortIndex
        {
            get => EpisodesVM.EpisodeSortIndex;
            set => EpisodesVM.EpisodeSortIndex = value;
        }

        /// <summary>
        /// Aktuell gewählter Filter der Episodenliste. Pass-Through zum <see cref="EpisodesVM"/>.
        /// </summary>
        public int EpisodeFilterIndex
        {
            get => EpisodesVM.EpisodeFilterIndex;
            set => EpisodesVM.EpisodeFilterIndex = value;
        }

        /// <summary>
        /// Aktiver Tab: 0 = Folgen (regulär), 1 = Sonderfolgen.
        /// Pass-Through zum <see cref="EpisodesVM"/>.
        /// </summary>
        public int EpisodeTabIndex
        {
            get => EpisodesVM.EpisodeTabIndex;
            set => EpisodesVM.EpisodeTabIndex = value;
        }

        /// <summary>
        /// True wenn die aktuelle Serie Sonderfolgen hat – nur dann wird der Tab sichtbar.
        /// Pass-Through zum <see cref="EpisodesVM"/>.
        /// </summary>
        public bool HasSpecialEpisodes => EpisodesVM.HasSpecialEpisodes;

        /// <summary>
        /// Hebt die Serienauswahl auf – der gesamte Folgenbereich verschwindet.
        /// Koordiniert die Sub-VMs: Künstler-Auswahl löschen, Episoden-Liste leeren,
        /// Track-Panel ausblenden.
        /// </summary>
        public void DeselectArtist()
        {
            ArtistsVM.DeselectArtist();
            EpisodesVM.Clear();
            TracksVM.Clear();
        }

        /// <summary>
        /// Anzahl der Sonderfolgen für die Tab-Beschriftung (z.B. "Sonderfolgen (35)").
        /// Pass-Through zum <see cref="EpisodesVM"/>.
        /// </summary>
        public int SpecialEpisodeCount => EpisodesVM.SpecialEpisodeCount;

        /// <summary>
        /// Sichtbarkeit des "Keine Serien"-Platzhalters – Pass-Through zum <see cref="ArtistsVM"/>.
        /// </summary>
        public Visibility ArtistsEmptyVisibility => ArtistsVM.ArtistsEmptyVisibility;

        /// <summary>
        /// Sichtbarkeit des "Folge wählen"-Platzhalters in der mittleren Spalte.
        /// Pass-Through zum <see cref="EpisodesVM"/>.
        /// </summary>
        public Visibility EpisodesEmptyVisibility => EpisodesVM.EpisodesEmptyVisibility;

        /// <summary>
        /// Sichtbarkeit der Sortier-ComboBox in der mittleren Spalte.
        /// Pass-Through zum <see cref="EpisodesVM"/>.
        /// </summary>
        public Visibility EpisodesLoadedVisibility => EpisodesVM.EpisodesLoadedVisibility;

        /// <summary>
        /// Sichtbarkeit des "Track wählen"-Platzhalters – Pass-Through zum <see cref="TracksVM"/>.
        /// </summary>
        public Visibility TracksEmptyVisibility => TracksVM.TracksEmptyVisibility;

        /// <summary>
        /// Sichtbarkeit des "Alle Tracks dieser Serie bearbeiten"-Buttons – Pass-Through zum <see cref="ArtistsVM"/>.
        /// </summary>
        public Visibility SeriesActionsVisibility => ArtistsVM.SeriesActionsVisibility;

        /// <summary>
        /// Sichtbarkeit der Episoden-Aktionsleiste – Pass-Through zum <see cref="TracksVM"/>.
        /// </summary>
        public Visibility TrackActionsVisibility => TracksVM.TracksHeaderVisibility;

        /// <summary>
        /// Anzeigename der aktuell gewählten Episode – Pass-Through zum <see cref="TracksVM"/>.
        /// </summary>
        public string SelectedEpisodeTitle => TracksVM.SelectedEpisodeTitle;

        /// <summary>
        /// Metadaten-Zeile über dem Folgentitel – Pass-Through zum <see cref="TracksVM"/>.
        /// </summary>
        public string TrackPanelSubtitle => TracksVM.TrackPanelSubtitle;

        // ── Befehle ──────────────────────────────────────────────────────────────

        /// <summary>Startet einen neuen Scan der lokalen Bibliothek.</summary>
        public ICommand ScanCommand => ScanVM.ScanCommand;

        /// <summary>Setzt alle lokalen Zuordnungen zurück und scannt neu.</summary>
        public ICommand ReInitializeCommand => ScanVM.ReInitializeCommand;

        /// <summary>Öffnet alle Tracks der gewählten Serie im Tag-Manager. Pass-Through.</summary>
        public ICommand OpenAllSeriesTracksCommand => TracksVM.OpenAllSeriesTracksCommand;

        /// <summary>Öffnet alle Tracks der gewählten Episode im Tag-Manager. Pass-Through.</summary>
        public ICommand OpenAllEpisodeTracksCommand => TracksVM.OpenAllEpisodeTracksCommand;

        /// <summary>
        /// Signalisiert, dass der Nutzer einen Ordner als neue lokale Serie hinzufügen möchte.
        /// Das Kommando selbst kennt das HWND nicht – die Page reagiert auf das Event
        /// und ruft <see cref="AddFolderAsync"/> mit dem Fenster-Handle auf.
        /// </summary>
        public ICommand AddFolderCommand => ScanVM.AddFolderCommand;

        /// <summary>
        /// Spielt alle Tracks der aktuell gewählten Folge in sortierter Reihenfolge ab.
        /// Pass-Through zum <see cref="TracksVM"/>.
        /// </summary>
        public RelayCommand PlayEpisodeCommand => TracksVM.PlayEpisodeCommand;

        /// <summary>
        /// Prüft alle abonnierten Serien mit lokalem Ordner auf fehlende Folgen
        /// (lokale Lücken + Live-Online-Abgleich) und feuert <see cref="AllSeriesCheckCompleted"/>.
        /// </summary>
        public ICommand CheckAllSeriesCommand { get; }

        // ── Navigation ───────────────────────────────────────────────────────────

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer den Tag-Manager für einen Pfad öffnen möchte.
        /// Die <see cref="EchoPlay.App.Views.MediathekLokalPage"/> abonniert dieses Event
        /// und führt die Frame-Navigation durch.
        /// </summary>
        public event Action<string>? NavigateToTagManagerRequested;

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer den Ordner-Hinzufügen-Button drückt.
        /// Die Page muss das HWND liefern und <see cref="AddFolderAsync"/> aufrufen.
        /// Pass-Through zum <see cref="ScanVM"/>.
        /// </summary>
        public event Action? AddFolderRequested
        {
            add    => ScanVM.AddFolderRequested += value;
            remove => ScanVM.AddFolderRequested -= value;
        }

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
        /// Wird ausgelöst, wenn der Ordnerstruktur-Assistent eine Vorschau erstellt hat.
        /// Die Page zeigt die geplanten Verschiebungen in einem ContentDialog an.
        /// Bei Bestätigung durch den Nutzer ruft die Page <see cref="ExecuteRestructureAsync"/> auf.
        /// </summary>
        public event Action<RestructurePreviewDisplay>? RestructurePreviewReady;

        // ── Laden ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lädt Bibliothekseinstellungen und alle Serien mit lokalem Ordner. Setzt die
        /// Auswahl zurück – mittlere und rechte Spalte werden geleert. Die Künstler-Liste
        /// und das Cover-Laden übernimmt das <see cref="ArtistsVM"/>; das Top-VM koordiniert
        /// nur die AppSettings-Spiegelung in den ScanVM.
        /// </summary>
        public async Task LoadAsync()
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
                EchoPlay.Data.Entities.Settings.AppSettings settings = await settingsService.GetAsync();
                ScanVM.LibraryRootPath         = settings.LocalLibraryRootPath ?? string.Empty;
                ScanVM.NeedsLibraryFolderSetup = string.IsNullOrWhiteSpace(settings.LocalLibraryRootPath);
            }

            EpisodesVM.Clear();
            TracksVM.Clear();
            await ArtistsVM.LoadFromDatabaseAsync();
        }

        // ── Auswahl-Logik ────────────────────────────────────────────────────────

        /// <summary>
        /// Wählt eine Serie aus und lädt deren Episoden mit lokalem Ordner in die mittlere Spalte.
        /// Die rechte Spalte wird dabei geleert. Das Top-VM koordiniert die drei Sub-VMs.
        /// </summary>
        /// <param name="artist">Die ausgewählte Serie.</param>
        public async Task SelectArtistAsync(LocalArtistCardViewModel artist)
        {
            ArtistsVM.SelectArtist(artist);
            TracksVM.Clear();

            // Episoden und Wiedergabestatus laden
            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IPlaybackStateDataService stateService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(artist.SeriesId);

            List<Guid> episodeIds = [.. episodes
                .Where(e => e.LocalFolderPath is not null)
                .Select(e => e.Id)];

            HashSet<Guid> completedIds = await stateService.GetCompletedEpisodeIdsAsync(episodeIds);

            IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync();
            HashSet<Guid> inProgressIds = allStates
                .Where(s => episodeIds.Contains(s.EpisodeId) && !s.IsCompleted && s.LastPosition > TimeSpan.Zero)
                .Select(s => s.EpisodeId)
                .ToHashSet();

            await EpisodesVM.LoadForSeriesAsync(artist, episodes, completedIds, inProgressIds);
        }

        /// <summary>
        /// Wählt eine Episode aus und lädt deren Tracks in die rechte Spalte.
        /// </summary>
        /// <param name="episode">Die ausgewählte Episode.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task SelectEpisodeAsync(LocalEpisodeCardViewModel episode)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();

            IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episode.EpisodeId);

            TracksVM.SetTracks(episode, tracks);
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

        // ── Scan / Ordner-Picker (Pass-Through zum ScanVM) ───────────────────────

        /// <summary>
        /// Öffnet den System-Ordnerpicker und übernimmt den gewählten Pfad als
        /// neuen Bibliotheksordner. Delegiert an <see cref="ScanVM"/>.
        /// </summary>
        /// <param name="windowHandle">HWND des Hauptfensters für den FolderPicker.</param>
        public Task PickFolderAsync(nint windowHandle) => ScanVM.PickFolderAsync(windowHandle);

        /// <summary>
        /// Öffnet den Ordnerpicker und legt den gewählten Ordner als neue lokale Serie an.
        /// Delegiert an <see cref="ScanVM"/>.
        /// </summary>
        /// <param name="windowHandle">HWND des Hauptfensters für den FolderPicker.</param>
        public Task AddFolderAsync(nint windowHandle) => ScanVM.AddFolderAsync(windowHandle);

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

        // ── Fehlende Folgen ────────────────────────────────────────────────────────

        /// <summary>
        /// Wird ausgelöst, bevor die Fehlende-Folgen-Prüfung beginnt.
        /// Die Page zeigt einen Drei-Optionen-Dialog (Online / Nur offline / Abbrechen)
        /// und liefert das Ergebnis als <see cref="MissingEpisodesMode"/>.
        /// </summary>
        public event Func<Task<MissingEpisodesMode>>? MissingEpisodesModeRequested;

        /// <summary>
        /// Ermittelt fehlende Folgen einer Serie. Fragt den Nutzer zuerst über
        /// <see cref="MissingEpisodesModeRequested"/>, ob online geprüft werden soll, und
        /// delegiert die eigentliche Analyse an den <see cref="IMissingEpisodesCoordinator"/>.
        /// </summary>
        /// <param name="seriesId">ID der zu prüfenden Serie.</param>
        public async Task ShowMissingEpisodesAsync(Guid seriesId)
        {
            if (_missingEpisodesCoordinator is null)
            {
                return;
            }

            LocalArtistCardViewModel? card = ArtistsVM.AllArtists.FirstOrDefault(a => a.SeriesId == seriesId);

            MissingEpisodesMode mode = await RequestMissingEpisodesModeAsync();
            if (mode == MissingEpisodesMode.Cancel)
            {
                return;
            }

            IReadOnlyList<string> result = await _missingEpisodesCoordinator.CheckSingleSeriesAsync(
                seriesId, card?.LocalFolderPath, mode);

            MissingEpisodesResolved?.Invoke(result);
        }

        /// <summary>
        /// Prüft alle abonnierten Serien mit lokalem Ordner auf fehlende Folgen.
        /// Delegiert an den <see cref="IMissingEpisodesCoordinator"/> und feuert
        /// <see cref="AllSeriesCheckCompleted"/> mit dem Ergebnis.
        /// </summary>
        public async Task CheckAllSeriesAsync()
        {
            if (_missingEpisodesCoordinator is null)
            {
                return;
            }

            MissingEpisodesMode mode = await RequestMissingEpisodesModeAsync();
            if (mode == MissingEpisodesMode.Cancel)
            {
                return;
            }

            MissingEpisodesReport report = await _missingEpisodesCoordinator.CheckAllSeriesAsync(mode);
            AllSeriesCheckCompleted?.Invoke(report);
        }

        /// <summary>
        /// Holt den Modus aus dem UI-Dialog. Ohne Listener wird "Nur offline" angenommen –
        /// das ist das Verhalten in Unit-Tests, in denen kein Dialog existiert.
        /// </summary>
        private async Task<MissingEpisodesMode> RequestMissingEpisodesModeAsync()
        {
            if (MissingEpisodesModeRequested is null)
            {
                return MissingEpisodesMode.OfflineOnly;
            }

            return await MissingEpisodesModeRequested.Invoke();
        }

        // ── Episoden-Status (Pass-Through zum EpisodesVM) ──────────────────────

        /// <summary>
        /// Markiert eine Episode als vollständig gehört. Pass-Through zum <see cref="EpisodesVM"/>.
        /// </summary>
        public Task MarkEpisodeAsPlayedAsync(Guid episodeId) => EpisodesVM.MarkEpisodeAsPlayedAsync(episodeId);

        /// <summary>
        /// Markiert eine Episode als ungehört. Pass-Through zum <see cref="EpisodesVM"/>.
        /// </summary>
        public Task MarkEpisodeAsUnplayedAsync(Guid episodeId) => EpisodesVM.MarkEpisodeAsUnplayedAsync(episodeId);

        // ── Ordnerstruktur-Assistent ─────────────────────────────────────────────

        /// <summary>
        /// Analysiert den Serienordner und erstellt eine Vorschau für den Ordnerstruktur-Umbau.
        /// Löst <see cref="RestructurePreviewReady"/> aus, wenn verschiebbare Dateien gefunden wurden.
        /// Die eigentliche Analyse läuft im <see cref="IFolderRestructureCoordinator"/>.
        /// </summary>
        /// <param name="seriesId">ID der Serie, deren Ordner analysiert werden soll.</param>
        public async Task AnalyzeRestructureAsync(Guid seriesId)
        {
            if (_restructureCoordinator is null)
            {
                return;
            }

            LocalArtistCardViewModel? card = ArtistsVM.AllArtists.FirstOrDefault(a => a.SeriesId == seriesId);
            if (card?.LocalFolderPath is null)
            {
                return;
            }

            RestructurePreviewDisplay? preview =
                await _restructureCoordinator.AnalyzeAsync(card.LocalFolderPath);

            if (preview is null)
            {
                return;
            }

            RestructurePreviewReady?.Invoke(preview);
        }

        /// <summary>
        /// Führt den Ordnerstruktur-Umbau aus und löst danach einen Neu-Scan der Bibliothek aus.
        /// Delegiert an den <see cref="IFolderRestructureCoordinator"/>.
        /// </summary>
        /// <param name="preview">Die zuvor erstellte App-Display-Vorschau.</param>
        /// <returns>Anzahl der verschobenen Dateien.</returns>
        public async Task<int> ExecuteRestructureAsync(RestructurePreviewDisplay preview)
        {
            if (_restructureCoordinator is null)
            {
                return 0;
            }

            return await _restructureCoordinator.ExecuteAsync(preview);
        }

        // ── Cover-Verwaltung (Delegationen an den IEpisodeCoverCoordinator) ──────

        /// <summary>
        /// Prüft den Offline-Modus und zeigt bei Bedarf einen Bestätigungsdialog.
        /// Muss von der Page aufgerufen werden, bevor der Cover-Such-Dialog geöffnet wird.
        /// </summary>
        public Task<IDisposable?> RequestOnlineAccessForCoverSearchAsync()
            => _onlineAccessGuard.RequestOnlineAccessAsync();

        /// <summary>
        /// Sucht Cover-Kandidaten für den angegebenen Begriff. Delegiert an den
        /// <see cref="IEpisodeCoverCoordinator"/>.
        /// </summary>
        public Task<IReadOnlyList<CoverSearchHit>> SearchCoversAsync(string query, CancellationToken ct)
        {
            if (_coverCoordinator is null)
            {
                return Task.FromResult<IReadOnlyList<CoverSearchHit>>([]);
            }

            return _coverCoordinator.SearchCoversAsync(query, ct);
        }

        /// <summary>
        /// Übernimmt rohe Bytes als Serien-Cover. Delegiert an den
        /// <see cref="IEpisodeCoverCoordinator"/>.
        /// </summary>
        public Task ApplySeriesCoverFromBytesAsync(LocalArtistCardViewModel card, byte[] bytes)
        {
            if (_coverCoordinator is null)
            {
                return Task.CompletedTask;
            }

            return _coverCoordinator.ApplySeriesCoverFromBytesAsync(card, bytes);
        }

        /// <summary>
        /// Übernimmt rohe Bytes als Episoden-Cover. Delegiert an den
        /// <see cref="IEpisodeCoverCoordinator"/>.
        /// </summary>
        public Task ApplyEpisodeCoverFromBytesAsync(LocalEpisodeCardViewModel card, byte[] bytes)
        {
            if (_coverCoordinator is null)
            {
                return Task.CompletedTask;
            }

            return _coverCoordinator.ApplyEpisodeCoverFromBytesAsync(card, bytes);
        }

        /// <summary>
        /// Lädt das gewählte Cover herunter und übernimmt es als Serien-Cover.
        /// Delegiert an den <see cref="IEpisodeCoverCoordinator"/>.
        /// </summary>
        public Task ApplySelectedSeriesCoverAsync(LocalArtistCardViewModel card, CoverSearchHit hit)
        {
            if (_coverCoordinator is null)
            {
                return Task.CompletedTask;
            }

            return _coverCoordinator.ApplySelectedSeriesCoverAsync(card, hit);
        }

        /// <summary>
        /// Lädt das gewählte Cover herunter und übernimmt es als Episoden-Cover.
        /// Delegiert an den <see cref="IEpisodeCoverCoordinator"/>.
        /// </summary>
        public Task ApplySelectedEpisodeCoverAsync(LocalEpisodeCardViewModel card, CoverSearchHit hit)
        {
            if (_coverCoordinator is null)
            {
                return Task.CompletedTask;
            }

            return _coverCoordinator.ApplySelectedEpisodeCoverAsync(card, hit);
        }

        /// <summary>
        /// Räumt das ViewModel auf: meldet Sub-VMs ab und entlässt sie.
        /// </summary>
        public void Dispose()
        {
            ScanVM.PropertyChanged    -= OnScanVmPropertyChanged;
            ScanVM.ScanStarting       -= OnScanStarting;
            ScanVM.LibraryReloaded    -= LoadAsync;
            ScanVM.Dispose();

            EpisodesVM.PropertyChanged  -= OnEpisodesVmPropertyChanged;
            EpisodesVM.Dispose();

            TracksVM.PropertyChanged  -= OnTracksVmPropertyChanged;
            ArtistsVM.PropertyChanged -= OnArtistsVmPropertyChanged;
        }
    }
}

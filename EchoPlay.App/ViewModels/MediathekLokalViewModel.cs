using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

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
        private readonly IPlayerService _playerService;
        private readonly IOnlineEpisodeChecker _onlineEpisodeChecker;
        private readonly IPageModeGuard? _pageModeGuard;
        private readonly MediathekLokalActions _actions;

        /// <summary>
        /// Initialisiert das ViewModel mit dem Service-Context.
        /// </summary>
        /// <param name="context">Bündelt alle per DI aufgelösten Service-Abhängigkeiten.</param>
        internal MediathekLokalViewModel(MediathekLokalViewModelContext context)
        {
            _playerService = context.PlayerService;
            _onlineEpisodeChecker = context.OnlineEpisodeChecker;
            _pageModeGuard = context.PageModeGuard;

            // Sub-VM für die Episoden-Spalte – kapselt Filter, Sortierung, Cover-Laden und
            // den Gehört-Status. Events des Sub-VMs werden an die eigenen Pass-Through-Properties
            // weitergereicht, damit bestehende XAML-Bindings unverändert funktionieren.
            EpisodesVM = new LocalEpisodesViewModel(context.ScopeFactory, context.CoverLoader, context.Clock, context.CoverService, context.Logger);
            EpisodesVM.PropertyChanged += OnEpisodesVmPropertyChanged;

            // Sub-VM für die Track-Spalte – kapselt Trackliste, PlayCommand und Tag-Manager-Sprünge.
            // Events werden ebenfalls an die Pass-Through-Properties weitergereicht.
            TracksVM = new LocalTracksViewModel(context.PlayerService, RequestTagManagerNavigation);
            TracksVM.PropertyChanged += OnTracksVmPropertyChanged;

            // Sub-VM für die Künstler-/Serien-Spalte – kapselt Liste, Suchfilter, Cover-Build,
            // AppendArtistCard und Auswahl-State.
            ArtistsVM = new LocalArtistsViewModel(context.ScopeFactory, context.CoverService);
            ArtistsVM.PropertyChanged += OnArtistsVmPropertyChanged;

            // Sub-VM für Scan, Neu-Initialisierung und Ordnerauswahl – bekommt als Callback eine
            // Referenz auf ArtistsVM.AppendArtistCardAsync, damit live eintreffende Serien sofort
            // in der lokalen Kachelgrid erscheinen.
            ScanVM = new LocalLibraryScanViewModel(
                context.ScopeFactory,
                context.SyncService,
                context.ErrorDialogService,
                context.ConfirmationDialogService,
                context.StatusBar,
                context.ScanEventService,
                series => _ = ArtistsVM.AppendArtistCardAsync(series));

            // Orchestrator für alle Async-Aktionen – bekommt die Sub-VMs und alle Services,
            // die nur für Aktionen benötigt werden.
            _actions = new MediathekLokalActions(
                context.ScopeFactory,
                context.ConfirmationDialogService,
                context.StatusBar,
                context.CoverSearchService,
                context.OnlineAccessGuard,
                context.WatchToggleService,
                context.RestructureCoordinator,
                context.MissingEpisodesCoordinator,
                context.CoverCoordinator,
                context.Clock,
                ScanVM,
                ArtistsVM,
                EpisodesVM,
                TracksVM);

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
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        /// <param name="seriesId">ID der Serie.</param>
        /// <param name="watch">Neuer Status.</param>
        public Task ToggleWatchAsync(Guid seriesId, bool watch) => _actions.ToggleWatchAsync(seriesId, watch);

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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "VM->Page-Navigations-Bridge: der Nutzlast-String ist der Zielpfad für den Tag-Manager; Action<string> bleibt klarer als 'PathEventArgs' ohne Mehrwert.")]
        public event Action<string>? NavigateToTagManagerRequested;

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer den Ordner-Hinzufügen-Button drückt.
        /// Die Page muss das HWND liefern und <see cref="AddFolderAsync"/> aufrufen.
        /// Pass-Through zum <see cref="ScanVM"/>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Pass-Through auf ScanVM.AddFolderRequested; Signatur muss identisch zum Sub-VM bleiben.")]
        public event Action? AddFolderRequested
        {
            add => ScanVM.AddFolderRequested += value;
            remove => ScanVM.AddFolderRequested -= value;
        }

        /// <summary>
        /// Wird ausgelöst, nachdem fehlende Episoden ermittelt wurden.
        /// Die Page zeigt die übergebene Titelliste in einem ContentDialog an.
        /// Die Liste ist leer wenn alle Episoden lokal vorhanden sind.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Pass-Through auf _actions.MissingEpisodesResolved; Titelliste wird in einem Dialog angezeigt, Signatur muss identisch zum Orchestrator bleiben.")]
        public event Action<IReadOnlyList<string>>? MissingEpisodesResolved
        {
            add => _actions.MissingEpisodesResolved += value;
            remove => _actions.MissingEpisodesResolved -= value;
        }

        /// <summary>
        /// Wird ausgelöst, nachdem die Gesamtprüfung aller Serien abgeschlossen ist.
        /// Die Page zeigt den Bericht in einem Dialog an und bietet den TXT-Export an.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Pass-Through auf _actions.AllSeriesCheckCompleted; Bericht wird in einem Dialog angezeigt, Signatur muss identisch zum Orchestrator bleiben.")]
        public event Action<MissingEpisodesReport>? AllSeriesCheckCompleted
        {
            add => _actions.AllSeriesCheckCompleted += value;
            remove => _actions.AllSeriesCheckCompleted -= value;
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Ordnerstruktur-Assistent eine Vorschau erstellt hat.
        /// Die Page zeigt die geplanten Verschiebungen in einem ContentDialog an.
        /// Bei Bestätigung durch den Nutzer ruft die Page <see cref="ExecuteRestructureAsync"/> auf.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Pass-Through auf _actions.RestructurePreviewReady; Vorschau-Display wird in einem Dialog angezeigt, Signatur muss identisch zum Orchestrator bleiben.")]
        public event Action<RestructurePreviewDisplay>? RestructurePreviewReady
        {
            add => _actions.RestructurePreviewReady += value;
            remove => _actions.RestructurePreviewReady -= value;
        }

        // ── Laden ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lädt Bibliothekseinstellungen und alle Serien mit lokalem Ordner.
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        public Task LoadAsync() => _actions.LoadAsync();

        // ── Auswahl-Logik ────────────────────────────────────────────────────────

        /// <summary>
        /// Wählt eine Serie aus und lädt deren Episoden mit lokalem Ordner in die mittlere Spalte.
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        /// <param name="artist">Die ausgewählte Serie.</param>
        public Task SelectArtistAsync(LocalArtistCardViewModel artist)
        {
            ArgumentNullException.ThrowIfNull(artist);
            return _actions.SelectArtistAsync(artist);
        }

        /// <summary>
        /// Wählt eine Episode aus und lädt deren Tracks in die rechte Spalte.
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        /// <param name="episode">Die ausgewählte Episode.</param>
        public Task SelectEpisodeAsync(LocalEpisodeCardViewModel episode)
        {
            ArgumentNullException.ThrowIfNull(episode);
            return _actions.SelectEpisodeAsync(episode);
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
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        /// <param name="seriesId">ID der betroffenen Serie.</param>
        public Task MarkAllAsReadAsync(Guid seriesId) => _actions.MarkAllAsReadAsync(seriesId);

        /// <summary>
        /// Entfernt eine Serie aus der Bibliothek (Soft-Delete in der DB).
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        /// <param name="seriesId">ID der zu entfernenden Serie.</param>
        public Task DeleteSeriesFromLibraryAsync(Guid seriesId) => _actions.DeleteSeriesFromLibraryAsync(seriesId);

        /// <summary>
        /// Löscht eine Serie unwiderruflich – sowohl aus der DB als auch von der Festplatte.
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        /// <param name="seriesId">ID der Serie.</param>
        /// <param name="folderPath">Lokaler Ordnerpfad der Serie.</param>
        public Task DeleteSeriesFromDiskAsync(Guid seriesId, string? folderPath) => _actions.DeleteSeriesFromDiskAsync(seriesId, folderPath);

        // ── Fehlende Folgen ────────────────────────────────────────────────────────

        /// <summary>
        /// Wird ausgelöst, bevor die Fehlende-Folgen-Prüfung beginnt.
        /// Die Page zeigt einen Drei-Optionen-Dialog (Online / Nur offline / Abbrechen)
        /// und liefert das Ergebnis als <see cref="MissingEpisodesMode"/>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Pass-Through auf _actions.MissingEpisodesModeRequested; Signatur Func<Task<MissingEpisodesMode>> ist nötig, damit die Page das Dialog-Ergebnis asynchron zurückliefern kann (EventHandler<T> unterstuetzt kein await/Rückgabewert).")]
        public event Func<Task<MissingEpisodesMode>>? MissingEpisodesModeRequested
        {
            add => _actions.MissingEpisodesModeRequested += value;
            remove => _actions.MissingEpisodesModeRequested -= value;
        }

        /// <summary>
        /// Ermittelt fehlende Folgen einer Serie.
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        /// <param name="seriesId">ID der zu prüfenden Serie.</param>
        public Task ShowMissingEpisodesAsync(Guid seriesId) => _actions.ShowMissingEpisodesAsync(seriesId);

        /// <summary>
        /// Prüft alle abonnierten Serien mit lokalem Ordner auf fehlende Folgen.
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        public Task CheckAllSeriesAsync() => _actions.CheckAllSeriesAsync();

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
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        /// <param name="seriesId">ID der Serie, deren Ordner analysiert werden soll.</param>
        public Task AnalyzeRestructureAsync(Guid seriesId) => _actions.AnalyzeRestructureAsync(seriesId);

        /// <summary>
        /// Führt den Ordnerstruktur-Umbau aus. Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        /// <param name="preview">Die zuvor erstellte App-Display-Vorschau.</param>
        /// <returns>Anzahl der verschobenen Dateien.</returns>
        public Task<int> ExecuteRestructureAsync(RestructurePreviewDisplay preview) => _actions.ExecuteRestructureAsync(preview);

        // ── Cover-Verwaltung (Delegationen an den MediathekLokalActions) ──────

        /// <summary>
        /// Prüft den Offline-Modus und zeigt bei Bedarf einen Bestätigungsdialog.
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        public Task<IDisposable?> RequestOnlineAccessForCoverSearchAsync()
            => _actions.RequestOnlineAccessForCoverSearchAsync();

        /// <summary>
        /// Sucht Cover-Kandidaten für den angegebenen Begriff.
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        public Task<IReadOnlyList<CoverSearchHit>> SearchCoversAsync(string query, CancellationToken ct) => _actions.SearchCoversAsync(query, ct);

        /// <summary>
        /// Übernimmt rohe Bytes als Serien-Cover.
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        public Task ApplySeriesCoverFromBytesAsync(LocalArtistCardViewModel card, byte[] bytes) => _actions.ApplySeriesCoverFromBytesAsync(card, bytes);

        /// <summary>
        /// Übernimmt rohe Bytes als Episoden-Cover.
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        public Task ApplyEpisodeCoverFromBytesAsync(LocalEpisodeCardViewModel card, byte[] bytes) => _actions.ApplyEpisodeCoverFromBytesAsync(card, bytes);

        /// <summary>
        /// Lädt das gewählte Cover herunter und übernimmt es als Serien-Cover.
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        public Task ApplySelectedSeriesCoverAsync(LocalArtistCardViewModel card, CoverSearchHit hit) => _actions.ApplySelectedSeriesCoverAsync(card, hit);

        /// <summary>
        /// Lädt das gewählte Cover herunter und übernimmt es als Episoden-Cover.
        /// Delegiert an den <see cref="MediathekLokalActions"/>.
        /// </summary>
        public Task ApplySelectedEpisodeCoverAsync(LocalEpisodeCardViewModel card, CoverSearchHit hit) => _actions.ApplySelectedEpisodeCoverAsync(card, hit);

        /// <summary>
        /// Räumt das ViewModel auf: meldet Sub-VMs ab und entlässt sie.
        /// </summary>
        public void Dispose()
        {
            ScanVM.PropertyChanged -= OnScanVmPropertyChanged;
            ScanVM.ScanStarting -= OnScanStarting;
            ScanVM.LibraryReloaded -= LoadAsync;
            ScanVM.Dispose();

            EpisodesVM.PropertyChanged -= OnEpisodesVmPropertyChanged;
            EpisodesVM.Dispose();

            TracksVM.PropertyChanged -= OnTracksVmPropertyChanged;
            ArtistsVM.PropertyChanged -= OnArtistsVmPropertyChanged;
        }
    }
}

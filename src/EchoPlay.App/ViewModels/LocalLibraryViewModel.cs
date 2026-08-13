using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using System;
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
    /// <remarks>
    /// Die Seite besteht aus vier Bereichen, und jeder hat sein eigenes Ansichtsmodell:
    /// <see cref="ScanVM"/>, <see cref="ArtistsVM"/>, <see cref="EpisodesVM"/> und
    /// <see cref="TracksVM"/>; die Abläufe darüber liegen in <see cref="Actions"/>.
    /// Dieses Ansichtsmodell setzt sie zusammen und übernimmt genau das, was keinem der
    /// vier allein gehört: das Betreten und Verlassen der Seite, das Aufheben der
    /// Serienauswahl über alle drei Listen hinweg und den Sprung in den Tag-Manager.
    ///
    /// Bindungen und Seitenlogik sprechen die Bereiche direkt an
    /// (<c>ViewModel.ScanVM.IsScanning</c>). Der frühere Weg — jede Eigenschaft hier noch
    /// einmal führen und das Änderungsereignis weiterreichen — hat die Klasse auf über
    /// 700 Zeilen gebracht und dabei eine eigene Fehlerquelle geschaffen: Wo der
    /// weitergereichte Name vom Namen im Bereich abwich, wartete die Bindung auf eine
    /// Meldung, die nie kam, und die Anzeige blieb stumm leer.
    /// </remarks>
    public sealed class LocalLibraryViewModel : ObservableObject, IDisposable
    {
        private readonly IPageModeGuard? _pageModeGuard;

        /// <summary>
        /// Initialisiert das ViewModel mit dem Service-Context.
        /// </summary>
        /// <param name="context">Bündelt alle per DI aufgelösten Service-Abhängigkeiten.</param>
        internal LocalLibraryViewModel(LocalLibraryViewModelContext context)
        {
            _pageModeGuard = context.PageModeGuard;

            // Bereich für die Episoden-Spalte – kapselt Filter, Sortierung, Cover-Laden und
            // den Gehört-Status.
            EpisodesVM = new LocalEpisodesViewModel(context.ScopeFactory, context.CoverLoader, context.Clock, context.CoverService, context.Logger);

            // Bereich für die Track-Spalte – kapselt Trackliste, PlayCommand und Tag-Manager-Sprünge.
            TracksVM = new LocalTracksViewModel(context.PlayerService, context.ScopeFactory, RequestTagManagerNavigation);

            // Bereich für die Künstler-/Serien-Spalte – kapselt Liste, Suchfilter, Cover-Build,
            // AppendArtistCard und Auswahl-State.
            // Der Filter kommt aus der Ablage, nicht aus dem ViewModel: Beim Verlassen der
            // Seite wird dieses hier freigegeben, beim Zurückkehren neu erzeugt. Ohne die
            // Ablage stünde die Liste nach jedem Ausflug wieder ungefiltert da.
            ArtistsVM = new LocalArtistsViewModel(
                context.ScopeFactory,
                new SeriesCoverBuilder(context.CoverService),
                context.FilterState?.GetOrCreate<LocalArtistFilter>("LocalLibrary"));

            // Bereich für Scan, Neu-Initialisierung und Ordnerauswahl – bekommt als Callback eine
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

            // Orchestrator für alle Async-Aktionen – bekommt die Bereiche und alle Services,
            // die nur für Aktionen benötigt werden.
            Actions = new LocalLibraryActions(
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

            // ScanStarting leert die Listen, bevor ein Scan loslegt – veraltete Kacheln sollen
            // während des Scans nicht sichtbar bleiben.
            ScanVM.ScanStarting += OnScanStarting;

            // Nach Abschluss eines Scans lädt LoadAsync die Serien konsistent aus der DB nach.
            ScanVM.LibraryReloaded += Actions.LoadAsync;

            CheckAllSeriesCommand = new RelayCommand(() => _ = Actions.CheckAllSeriesAsync());
            ResetFiltersCommand = new RelayCommand(ArtistsVM.ResetFilters);
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer den Tag-Manager für einen Pfad öffnen möchte.
        /// Die <see cref="EchoPlay.App.Views.LocalLibraryPage"/> abonniert dieses Event
        /// und führt die Frame-Navigation durch.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "VM->Page-Navigations-Bridge: der Nutzlast-String ist der Zielpfad für den Tag-Manager; Action<string> bleibt klarer als 'PathEventArgs' ohne Mehrwert.")]
        public event Action<string>? NavigateToTagManagerRequested;

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
        /// Die Abläufe der Seite: Laden, Auswahl, Serien-Verwaltung, fehlende Folgen,
        /// Ordnerstruktur und Cover. Die Seite ruft sie direkt auf.
        /// </summary>
        internal LocalLibraryActions Actions { get; }

        /// <summary>
        /// Nimmt Suche und Filter zurück. Gehört zum „Nichts gefunden"-Hinweis: Von dort führt
        /// genau ein Schritt zum vollständigen Bestand.
        /// </summary>
        public ICommand ResetFiltersCommand { get; }

        /// <summary>
        /// Prüft alle abonnierten Serien mit lokalem Ordner auf fehlende Folgen und meldet
        /// das Ergebnis über <see cref="LocalLibraryActions.AllSeriesCheckCompleted"/>.
        /// </summary>
        public ICommand CheckAllSeriesCommand { get; }

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

        /// <summary>
        /// Hebt die Serienauswahl auf – der gesamte Folgenbereich verschwindet.
        /// Koordiniert die drei Listen: Künstler-Auswahl löschen, Episoden-Liste leeren,
        /// Track-Panel ausblenden.
        /// </summary>
        public void DeselectArtist()
        {
            ArtistsVM.DeselectArtist();
            EpisodesVM.Clear();
            TracksVM.Clear();
        }

        /// <summary>
        /// Löst das <see cref="NavigateToTagManagerRequested"/>-Event aus.
        /// Aufgerufen von <see cref="LocalTrackRowViewModel"/>-Callbacks
        /// sowie von den beiden „Alle Tracks öffnen"-Befehlen des <see cref="TracksVM"/>.
        /// </summary>
        /// <param name="path">Ordnerpfad, den der Tag-Manager öffnen soll.</param>
        public void RequestTagManagerNavigation(string path)
        {
            NavigateToTagManagerRequested?.Invoke(path);
        }

        /// <summary>
        /// Räumt das ViewModel auf: meldet die Bereiche ab und entlässt sie.
        /// </summary>
        public void Dispose()
        {
            ScanVM.ScanStarting -= OnScanStarting;
            ScanVM.LibraryReloaded -= Actions.LoadAsync;
            ScanVM.Dispose();

            EpisodesVM.Dispose();
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
    }
}

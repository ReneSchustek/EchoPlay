using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die Online-Mediathek mit Akkordeon-Layout.
    /// Serien erscheinen als Cover-Kachelgrid. Bei Auswahl einer Serie klappen die Folgen
    /// direkt unterhalb der gewählten Kachelreihe auf – wie in der lokalen Mediathek.
    /// Unterstützt Freitextsuche, Statusfilterung und Inline-Provider-Suche.
    /// </summary>
    public sealed class MediathekOnlineViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly ImportService _importService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly ILocalizationService _localizationService;
        private readonly IOnlineAccessGuard _onlineAccessGuard;
        private readonly EpisodeCoverCacheService? _coverCacheService;
        private readonly CoverService _coverService;
        private readonly BackgroundCoverService? _backgroundCoverService;
        private readonly IWatchToggleService? _watchToggleService;
        private readonly IPageModeGuard? _pageModeGuard;
        private readonly EchoPlay.LocalLibrary.Cover.ICoverSearchService? _coverSearchService;
        private readonly INavigationService? _navigationService;

        // Wiederverwendbarer HTTP-Client für Cover-Downloads – static verhindert Socket-Erschöpfung
        private static readonly System.Net.Http.HttpClient _downloadClient = new();

        private List<SeriesCardViewModel> _allSeries = [];
        private IReadOnlyList<SeriesCardViewModel> _series = [];
        private IReadOnlyList<SearchResultViewModel> _providerSearchResults = [];
        private IReadOnlyList<OnlineEpisodeCardViewModel> _episodes = [];
        private List<OnlineEpisodeCardViewModel> _allEpisodes = [];
        private HashSet<Guid> _completedEpisodeIds = [];
        private bool _isLoading;
        private bool _isLoadingEpisodes;
        private string _loadingStatusText = string.Empty;
        private bool _isSearchingProvider;
        private bool _hasNoProvider;
        private string _searchText = string.Empty;
        private int _selectedSeriesIndex = -1;
        private int _seriesSortIndex;
        private int _episodeSortIndex;
        private int _searchTypeIndex;
        private SeriesStatusFilter _statusFilter = SeriesStatusFilter.Alle;

        /// <summary>Bricht laufende Cover-Downloads ab wenn eine andere Serie gewählt wird.</summary>
        private CancellationTokenSource? _episodeCoverCts;

        /// <summary>
        /// Initialisiert das ViewModel mit den benötigten Services.
        /// </summary>
        /// <param name="scopeFactory">Für Datenbankzugriffe und Card-Commands.</param>
        /// <param name="confirmationDialogService">Für Bestätigungs-Dialoge beim Abonnement-Toggle.</param>
        /// <param name="importService">Für die Provider-API-Suche (Spotify/Apple Music).</param>
        /// <param name="errorDialogService">Für Fehlermeldungen bei der Provider-Suche.</param>
        /// <param name="localizationService">Für lokalisierte Dialog-Texte.</param>
        /// <param name="onlineAccessGuard">Prüft Offline-Modus vor Online-Aktionen.</param>
        /// <param name="coverCacheService">Lädt fehlende Episoden-Cover im Hintergrund. Null in Tests.</param>
        /// <param name="coverService">Zentraler Cover-Zugriff über CoverImages-Tabelle.</param>
        /// <param name="backgroundCoverService">Lädt lokale Cover ins DB-Cache. Null in Tests.</param>
        /// <param name="watchToggleService">Optionaler Service für das Umschalten der Neuerscheinungs-Überwachung. In Tests <see langword="null"/>.</param>
        /// <param name="pageModeGuard">Optionaler Page-Mode-Guard – prüft den Offline-Modus beim Betreten der Page. In Tests <see langword="null"/>.</param>
        /// <param name="coverSearchService">Optionaler Cover-Suchdienst – wird nur für die manuelle Episoden-Cover-Suche benötigt. In Tests <see langword="null"/>.</param>
        /// <param name="navigationService">Optionaler Navigationsdienst – nur für Page-Wechsel aus Empty-State-Buttons. In Tests <see langword="null"/>.</param>
        public MediathekOnlineViewModel(
            IServiceScopeFactory scopeFactory,
            IConfirmationDialogService confirmationDialogService,
            ImportService importService,
            IErrorDialogService errorDialogService,
            ILocalizationService localizationService,
            IOnlineAccessGuard onlineAccessGuard,
            EpisodeCoverCacheService? coverCacheService = null,
            CoverService? coverService = null,
            BackgroundCoverService? backgroundCoverService = null,
            IWatchToggleService? watchToggleService = null,
            IPageModeGuard? pageModeGuard = null,
            EchoPlay.LocalLibrary.Cover.ICoverSearchService? coverSearchService = null,
            INavigationService? navigationService = null)
        {
            _scopeFactory               = scopeFactory;
            _confirmationDialogService  = confirmationDialogService;
            _importService              = importService;
            _errorDialogService         = errorDialogService;
            _localizationService        = localizationService;
            _onlineAccessGuard          = onlineAccessGuard;
            _coverCacheService          = coverCacheService;
            _coverService               = coverService!;
            _backgroundCoverService     = backgroundCoverService;
            _watchToggleService         = watchToggleService;
            _pageModeGuard              = pageModeGuard;
            _coverSearchService         = coverSearchService;
            _navigationService          = navigationService;

            ProviderSearchCommand = new RelayCommand(() => _ = SearchProviderAsync());
            AddSelectedCommand = new RelayCommand(() => _ = AddSelectedAsync());
            RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
            GoToSettingsCommand = new RelayCommand(() => _navigationService?.NavigateTo(NavigationTarget.Settings));
            FocusSearchCommand = new RelayCommand(StartSearchFromEmptyState);
        }

        /// <summary>
        /// Wird aus dem Empty-State-Button "Serie suchen" ausgelöst: setzt den Fokus auf
        /// die Suchleiste (über das <see cref="FocusSearchRequested"/>-Event) und löst
        /// bei vorhandenem Suchtext sofort die Provider-Suche aus.
        /// Die Page entscheidet, wie sie den Fokus tatsächlich setzt –
        /// das ViewModel bleibt frei von WinUI-Konzepten.
        /// </summary>
        private void StartSearchFromEmptyState()
        {
            FocusSearchRequested?.Invoke();

            if (!string.IsNullOrWhiteSpace(_searchText) && ProviderSearchCommand.CanExecute(null))
            {
                ProviderSearchCommand.Execute(null);
            }
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Empty-State-Button "Serie suchen" geklickt wurde.
        /// Die Page setzt den Fokus auf die Suchbox – im VM existiert kein WinUI-Konzept.
        /// </summary>
        public event Action? FocusSearchRequested;

        /// <summary>Navigiert in die Einstellungen – aus dem Empty-State "Kein Anbieter".</summary>
        public ICommand GoToSettingsCommand { get; }

        /// <summary>Setzt den Fokus auf die Suchleiste und löst ggf. eine Suche aus – aus dem Empty-State "Keine Serien".</summary>
        public ICommand FocusSearchCommand { get; }

        /// <summary>
        /// Wird vom Code-Behind beim Betreten der Seite aufgerufen. Prüft den Offline-Modus
        /// und navigiert bei aktivem Offline-Modus zurück zur vorherigen Seite. Liefert
        /// <see langword="false"/>, falls die Page nicht weiter geladen werden soll.
        /// Die Prüfung läuft über den <see cref="IPageModeGuard"/>; in Tests ohne Guard
        /// wird der Check übersprungen und die Page darf laden.
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            if (_pageModeGuard is null)
            {
                return true;
            }

            return await _pageModeGuard.EnsureOnlineAccessAsync();
        }

        /// <summary>
        /// Schaltet die Neuerscheinungs-Überwachung einer Online-Serie um und aktualisiert
        /// die zugehörige Karte. Die eigentliche Logik (Cache + iTunes-Check) liegt im
        /// <see cref="IWatchToggleService"/>, damit sie nicht in beiden Mediathek-VMs dupliziert wird.
        /// </summary>
        /// <param name="seriesId">ID der Serie.</param>
        /// <param name="watch">Neuer Status: <see langword="true"/> aktiviert die Überwachung.</param>
        public async Task ToggleWatchAsync(Guid seriesId, bool watch)
        {
            if (_watchToggleService is null)
            {
                return;
            }

            await _watchToggleService.ToggleAsync(seriesId, watch);

            SeriesCardViewModel? card = _series.FirstOrDefault(c => c.Id == seriesId);
            if (card is not null)
            {
                card.IsWatched = watch;
            }
        }

        // ── Cover-Suche für Episoden ─────────────────────────────────────────────

        /// <summary>
        /// Sucht Cover-Kandidaten für einen Episoden-Titel über den Cover-Suchdienst und
        /// gibt die Treffer als App-eigene <see cref="CoverSearchHit"/>-Wrapper zurück.
        /// Wird vom Cover-Auswahldialog der Online-Mediathek aufgerufen, damit die Page
        /// keinen eigenen DI-Scope öffnen und das LocalLibrary-Modell nicht direkt kennen muss.
        /// </summary>
        /// <param name="query">Suchbegriff – meist der Folgentitel.</param>
        /// <param name="ct">Abbruchtoken, z.B. für Dialog-Schließen.</param>
        /// <returns>Liste der Treffer; leer wenn nichts gefunden wurde oder kein Suchdienst verfügbar ist.</returns>
        public async Task<IReadOnlyList<CoverSearchHit>> SearchEpisodeCoversAsync(string query, CancellationToken ct)
        {
            if (_coverSearchService is null)
            {
                return [];
            }

            IReadOnlyList<EchoPlay.LocalLibrary.Cover.CoverSearchResult> results =
                await _coverSearchService.SearchAsync(query, ct);

            List<CoverSearchHit> hits = new(results.Count);
            foreach (EchoPlay.LocalLibrary.Cover.CoverSearchResult r in results)
            {
                hits.Add(CoverSearchHit.From(r));
            }
            return hits;
        }

        /// <summary>
        /// Lädt das gewählte Cover herunter, speichert es über den <see cref="CoverService"/>
        /// und aktualisiert die Episodenkachel sofort. Netzwerkfehler werden still verschluckt –
        /// in diesem Fall bleibt das bisherige Cover bestehen.
        /// </summary>
        /// <param name="card">Die Episodenkachel, deren Cover aktualisiert werden soll.</param>
        /// <param name="hit">Der vom Nutzer im Auswahldialog bestätigte Cover-Kandidat.</param>
        public async Task ApplySelectedEpisodeCoverAsync(OnlineEpisodeCardViewModel card, CoverSearchHit hit)
        {
            try
            {
                byte[] coverBytes = await _downloadClient.GetByteArrayAsync(hit.FullUrl);
                await _coverService.SetEpisodeCoverAsync(card.EpisodeId, coverBytes);

                Microsoft.UI.Xaml.Media.Imaging.BitmapImage? image =
                    await CoverService.ConvertToBitmapAsync(coverBytes);

                if (image is not null)
                {
                    card.CoverImage = image;
                }
            }
            catch (System.Net.Http.HttpRequestException)
            {
                // Netzwerkfehler → Platzhalter bleibt
            }
        }

        // ── Serien (Bibliothek) ──────────────────────────────────────────────────

        /// <summary>
        /// Die aktuell sichtbaren Serien – gefiltert nach
        /// <see cref="SearchText"/> und <see cref="StatusFilter"/>.
        /// </summary>
        public IReadOnlyList<SeriesCardViewModel> Series
        {
            get => _series;
            private set => SetProperty(ref _series, value);
        }

        /// <summary>
        /// Freitext-Suchfilter. Filtert die Bibliothek clientseitig.
        /// Leerer Suchtext bei Enter → Provider-Suche, leerer Text bei X → Reset.
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilter();

                    // Suchfeld geleert (z.B. X-Button) → Suchergebnisse zurücksetzen
                    if (string.IsNullOrEmpty(value))
                    {
                        ProviderSearchResults = [];
                    }
                }
            }
        }

        /// <summary>
        /// Filter nach Wiedergabefortschritt.
        /// </summary>
        public SeriesStatusFilter StatusFilter
        {
            get => _statusFilter;
            set
            {
                if (SetProperty(ref _statusFilter, value))
                {
                    ApplyFilter();
                }
            }
        }

        /// <summary>Gibt an, ob gerade ein Ladevorgang läuft.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Gibt an, ob gerade Episoden einer Serie geladen werden.
        /// Steuert den ProgressRing im Akkordeon-Bereich.
        /// </summary>
        public bool IsLoadingEpisodes
        {
            get => _isLoadingEpisodes;
            private set
            {
                if (SetProperty(ref _isLoadingEpisodes, value))
                {
                    OnPropertyChanged(nameof(LoadingEpisodesVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Lade-Indikators im Akkordeon-Bereich.
        /// </summary>
        public Visibility LoadingEpisodesVisibility =>
            _isLoadingEpisodes ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Statustext während des Ladevorgangs – z.B. bei der automatischen Episoden-Nachladung
        /// nach einer Migration. Leer wenn kein spezieller Status angezeigt werden soll.
        /// </summary>
        public string LoadingStatusText
        {
            get => _loadingStatusText;
            private set => SetProperty(ref _loadingStatusText, value);
        }

        /// <summary>Gibt an, ob kein Provider konfiguriert ist.</summary>
        public bool HasNoProvider
        {
            get => _hasNoProvider;
            private set
            {
                if (SetProperty(ref _hasNoProvider, value))
                {
                    OnPropertyChanged(nameof(EmptyStateVisibility));
                    OnPropertyChanged(nameof(NoProviderVisibility));
                    OnPropertyChanged(nameof(NoSeriesVisibility));
                }
            }
        }

        // ── Sichtbarkeiten ──────────────────────────────────────────────────────

        /// <summary>Leer-Zustand sichtbar wenn keine Serien und kein Laden.</summary>
        public Visibility EmptyStateVisibility =>
            !_isLoading && _series.Count == 0 && _providerSearchResults.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des "Kein Provider"-Hinweises.</summary>
        public Visibility NoProviderVisibility =>
            _hasNoProvider ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des "Keine Serien"-Hinweises.</summary>
        public Visibility NoSeriesVisibility =>
            !_hasNoProvider ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Bibliothek sichtbar wenn keine Suchergebnisse.</summary>
        public Visibility LibraryVisibility =>
            _providerSearchResults.Count == 0 && !_isSearchingProvider
                ? Visibility.Visible : Visibility.Collapsed;

        // ── Akkordeon: Episoden ──────────────────────────────────────────────────

        /// <summary>
        /// Index der gewählten Serie in <see cref="Series"/>. -1 wenn keine Serie gewählt.
        /// Steuert über <see cref="EpisodesAccordionVisibility"/> die Sichtbarkeit des Folgenbereichs.
        /// Steuert über PropertyChanged das Neuberechnen des Grid-Splits in der Page.
        /// </summary>
        public int SelectedSeriesIndex
        {
            get => _selectedSeriesIndex;
            private set
            {
                if (SetProperty(ref _selectedSeriesIndex, value))
                {
                    OnPropertyChanged(nameof(EpisodesAccordionVisibility));
                }
            }
        }

        /// <summary>Akkordeon sichtbar wenn eine Serie ausgewählt ist.</summary>
        public Visibility EpisodesAccordionVisibility =>
            _selectedSeriesIndex >= 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Episoden der gewählten Serie – gefiltert und sortiert.
        /// </summary>
        public IReadOnlyList<OnlineEpisodeCardViewModel> Episodes
        {
            get => _episodes;
            private set => SetProperty(ref _episodes, value);
        }

        /// <summary>
        /// Index der Serien-Sortierung: 0 = Name A–Z, 1 = Neueste zuerst, 2 = Meiste Folgen.
        /// Sortierung ist clientseitig – kein neuer DB-Query nötig.
        /// </summary>
        public int SeriesSortIndex
        {
            get => _seriesSortIndex;
            set
            {
                if (SetProperty(ref _seriesSortIndex, value))
                {
                    ApplyFilter();
                }
            }
        }

        /// <summary>
        /// Index der Episoden-Sortierung: 0 = Titel A–Z, 1 = Titel Z–A, 2 = Neueste zuerst.
        /// </summary>
        public int EpisodeSortIndex
        {
            get => _episodeSortIndex;
            set
            {
                if (SetProperty(ref _episodeSortIndex, value))
                {
                    ApplyEpisodeSort();
                }
            }
        }

        // ── Provider-Suche ──────────────────────────────────────────────────────

        /// <summary>Startet eine Suche beim Provider.</summary>
        /// <summary>Startet die Provider-Suche.</summary>
        public ICommand ProviderSearchCommand { get; }

        /// <summary>Importiert alle ausgewählten Suchergebnisse.</summary>
        public ICommand AddSelectedCommand { get; }

        /// <summary>Prüft alle Online-Serien auf neue Folgen beim Provider.</summary>
        public ICommand RefreshCommand { get; }

        /// <summary>Gibt an, ob gerade eine Provider-Suche läuft.</summary>
        public bool IsSearchingProvider
        {
            get => _isSearchingProvider;
            private set => SetProperty(ref _isSearchingProvider, value);
        }

        /// <summary>Suchergebnisse vom Provider.</summary>
        public IReadOnlyList<SearchResultViewModel> ProviderSearchResults
        {
            get => _providerSearchResults;
            private set
            {
                if (SetProperty(ref _providerSearchResults, value))
                {
                    OnPropertyChanged(nameof(ProviderSearchResultsVisibility));
                    OnPropertyChanged(nameof(SearchTypeFilterVisibility));
                    OnPropertyChanged(nameof(StatusFilterVisibility));
                    OnPropertyChanged(nameof(LibraryVisibility));
                    OnPropertyChanged(nameof(EmptyStateVisibility));
                }
            }
        }

        /// <summary>
        /// Suchtyp-Index: 0 = Serien, 1 = Folgen. Steuert ob `SearchAsync` oder
        /// `SearchAlbumsAsync` aufgerufen wird. Nur sichtbar während der Provider-Suche.
        /// </summary>
        public int SearchTypeIndex
        {
            get => _searchTypeIndex;
            set => SetProperty(ref _searchTypeIndex, value);
        }

        /// <summary>Sichtbarkeit des Suchtyp-Filters: nur bei aktiven Suchergebnissen.</summary>
        public Visibility SearchTypeFilterVisibility =>
            _providerSearchResults.Count > 0 || _isSearchingProvider
                ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des Statusfilters: nur in der Bibliothek (keine Suchergebnisse).</summary>
        public Visibility StatusFilterVisibility =>
            _providerSearchResults.Count == 0 && !_isSearchingProvider
                ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit der Provider-Suchergebnisse.</summary>
        public Visibility ProviderSearchResultsVisibility =>
            _providerSearchResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // ── Laden ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lädt alle online-importierten Serien aus der Datenbank.
        /// </summary>
        public async Task LoadAsync()
        {
            IsLoading = true;

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ISeriesDataService        seriesService   = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                IEpisodeDataService       episodeService  = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                IPlaybackStateDataService stateService    = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();
                IAppSettingsDataService   settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();

                AppSettings settings = await settingsService.GetAsync();
                HasNoProvider = settings.ActiveProvider == ProviderType.None;

                // Alle Wiedergabestände einmalig laden – für die Serien-Zähler und Episoden-Häkchen
                IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync();
                _completedEpisodeIds = allStates
                    .Where(s => s.IsCompleted)
                    .Select(s => s.EpisodeId)
                    .ToHashSet();

                IReadOnlyList<Series> dbSeries = await seriesService.GetAllAsync();
                List<SeriesCardViewModel> cards = new(dbSeries.Count);

                foreach (Series series in dbSeries)
                {
                    if (!series.IsOnlineImported) continue;

                    IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id);

                    // Nach einer Migration können Episoden fehlen (z.B. Album-Fix).
                    // In diesem Fall werden sie automatisch vom Provider nachgeladen.
                    if (episodes.Count == 0 && !HasNoProvider)
                    {
                        LoadingStatusText = string.Format(
                            System.Globalization.CultureInfo.CurrentCulture,
                            _localizationService.Get("OnlineReImportStatusText"), series.Title);

                        try
                        {
                            int reImported = await _importService.ReImportEpisodesAsync(series);

                            if (reImported > 0)
                            {
                                // Neuen Scope für die erneute Abfrage – der EpisodeDataService im
                                // aktuellen Scope sieht die gerade per ImportService geschriebenen Daten
                                // möglicherweise nicht sofort (separater DbContext).
                                using IServiceScope freshScope = _scopeFactory.CreateScope();
                                IEpisodeDataService freshEpisodeService = freshScope.ServiceProvider
                                    .GetRequiredService<IEpisodeDataService>();
                                episodes = await freshEpisodeService.GetBySeriesIdAsync(series.Id);
                            }
                        }
                        catch (Exception)
                        {
                            // Einzelne Serien-Fehler dürfen den Ladevorgang nicht abbrechen
                        }
                    }

                    (int finishedCount, int inProgressCount, int notStartedCount) =
                        await stateService.GetCountsBySeriesIdAsync(series.Id);

                    SeriesCardViewModel card = new(
                        id:                        series.Id,
                        title:                     series.Title,
                        coverImage:                await BuildCoverImageAsync(series),
                        totalEpisodeCount:         episodes.Count,
                        newEpisodeCount:           notStartedCount,
                        inProgressCount:           inProgressCount,
                        finishedCount:             finishedCount,
                        isSubscribed:              series.IsSubscribed,
                        isFavorite:                series.IsFavorite,
                        isWatched:                 series.IsWatched,
                        scopeFactory:              _scopeFactory,
                        confirmationDialogService: _confirmationDialogService,
                        localizationService:       _localizationService);

                    cards.Add(card);
                }

                _allSeries = cards;
                DeselectSeries();
                ApplyFilter();

                // Cover im Hintergrund herunterladen und in der DB cachen –
                // UI ist bereits sichtbar (Kacheln mit Platzhalter), Cover erscheinen progressiv.
                _ = CacheSeriesCoversAsync(dbSeries);
            }
            finally
            {
                LoadingStatusText = string.Empty;
                IsLoading = false;
            }
        }

        // ── Serien-Auswahl (Akkordeon) ──────────────────────────────────────────

        /// <summary>
        /// Wählt eine Serie und lädt ihre Episoden für den Akkordeon-Bereich.
        /// </summary>
        /// <param name="card">Die gewählte Serien-Kachel.</param>
        public async Task SelectSeriesAsync(SeriesCardViewModel card)
        {
            // Laufende Cover-Downloads der vorherigen Serie abbrechen
            _episodeCoverCts?.Cancel();
            _episodeCoverCts?.Dispose();
            _episodeCoverCts = new CancellationTokenSource();
            CancellationToken ct = _episodeCoverCts.Token;

            // Vorherige Auswahl zurücksetzen
            foreach (SeriesCardViewModel c in _allSeries)
            {
                c.IsSelectedInAccordion = false;
            }

            // Alte Episoden sofort ausblenden, damit kein Spinner über alten Kacheln erscheint
            _allEpisodes = [];
            Episodes = [];

            card.IsSelectedInAccordion = true;
            IsLoadingEpisodes = true;

            // Index in der gefilterten Liste finden
            int idx = -1;
            for (int i = 0; i < _series.Count; i++)
            {
                if (ReferenceEquals(_series[i], card)) { idx = i; break; }
            }

            SelectedSeriesIndex = idx;

            // Lokale Cover in CoverImages sicherstellen – liest cover.jpg / ID3-Tags aus dem Dateisystem
            if (_backgroundCoverService is not null)
            {
                await _backgroundCoverService.EnsureLocalCoversForSeriesAsync(card.Title);
            }

            // Cover aus lokalen Episoden auf Online-Episoden kopieren (reine SQL-Operation, ms)
            using IServiceScope scope = _scopeFactory.CreateScope();

            ICoverCopyService coverCopy = scope.ServiceProvider.GetRequiredService<ICoverCopyService>();
            await coverCopy.CopyFromMatchingEpisodesAsync(card.Id);

            // Episoden aus DB laden – Cover sind jetzt schon gesetzt
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(card.Id);

            // Cover-Binärdaten per Batch laden – ein Query statt N Einzelzugriffe
            List<Guid> episodeIds = new(episodes.Count);

            foreach (Episode episode in episodes)
            {
                episodeIds.Add(episode.Id);
            }

            IReadOnlyDictionary<Guid, byte[]> coverMap = _coverService is not null
                ? await _coverService.GetEpisodeCoverBytesAsync(episodeIds)
                : new Dictionary<Guid, byte[]>();

            List<OnlineEpisodeCardViewModel> episodeCards = new(episodes.Count);

            foreach (Episode episode in episodes)
            {
                OnlineEpisodeCardViewModel episodeCard = new(
                    episodeId:     episode.Id,
                    episodeNumber: episode.EpisodeNumber,
                    title:         episode.Title,
                    releaseDate:   episode.ReleaseDate,
                    isCompleted:   _completedEpisodeIds.Contains(episode.Id),
                    providerUrl:   episode.ProviderUrl,
                    scopeFactory:  _scopeFactory);

                // Cover aus CoverService-Batch
                if (coverMap.TryGetValue(episode.Id, out byte[]? coverData))
                {
                    BitmapImage? coverImage = await CoverService.ConvertToBitmapAsync(coverData);

                    if (coverImage is not null)
                    {
                        episodeCard.CoverImage = coverImage;
                    }
                }

                episodeCards.Add(episodeCard);
            }

            _allEpisodes = episodeCards;
            _episodeSortIndex = 0;
            OnPropertyChanged(nameof(EpisodeSortIndex));
            ApplyEpisodeSort();
            IsLoadingEpisodes = false;

            // Fehlende Cover im Hintergrund nachladen – UI zeigt erst Platzhalter,
            // Cover erscheinen progressiv sobald der Download fertig ist.
            bool hasMissingCovers = false;

            foreach (OnlineEpisodeCardViewModel ep in episodeCards)
            {
                if (ep.CoverImage is null) { hasMissingCovers = true; break; }
            }

            if (hasMissingCovers && _coverCacheService is not null)
            {
                // Cover-Download im Hintergrund starten, UI-Update danach auf dem UI-Thread
                _ = RefreshMissingEpisodeCoversAsync(card.Id, episodeCards, ct);
            }
        }

        /// <summary>
        /// Lädt fehlende Episoden-Cover im Hintergrund herunter und aktualisiert
        /// die Kacheln progressiv – Platzhalter wird durch das geladene Cover ersetzt.
        /// Wird abgebrochen wenn der Nutzer eine andere Serie wählt.
        /// Aktualisiert die Kacheln periodisch während des Downloads, damit der Nutzer
        /// Cover sieht sobald sie in der DB liegen – nicht erst am Ende.
        /// </summary>
        private async Task RefreshMissingEpisodeCoversAsync(
            Guid seriesId,
            List<OnlineEpisodeCardViewModel> episodeCards,
            CancellationToken ct)
        {
            try
            {
                // Cover-Download im Hintergrund starten (nicht awaiten),
                // damit wir parallel die Kacheln aktualisieren können
                Task cacheTask = _coverCacheService!.CacheCoversAsync(seriesId, ct: ct);

                // Kacheln periodisch aktualisieren bis der Download fertig ist
                while (!cacheTask.IsCompleted && !ct.IsCancellationRequested)
                {
                    // Kurz warten, damit neue Cover in die DB geschrieben werden können
                    await Task.WhenAny(cacheTask, Task.Delay(2000, ct));

                    if (ct.IsCancellationRequested) return;

                    await UpdateEpisodeCardsFromDbAsync(episodeCards);
                }

                // Abschließende Aktualisierung nach Abschluss des Downloads
                await cacheTask;
                if (!ct.IsCancellationRequested)
                {
                    await UpdateEpisodeCardsFromDbAsync(episodeCards);
                }
            }
            catch (OperationCanceledException)
            {
                // Serienwechsel – erwarteter Abbruch
            }
            catch (Exception)
            {
                // Cover-Nachladen ist optional – kein Fehler für den Nutzer
            }
        }

        /// <summary>
        /// Lädt neu verfügbare Cover aus der DB und setzt sie auf den Kacheln.
        /// Nur Kacheln ohne Cover werden aktualisiert.
        /// </summary>
        private async Task UpdateEpisodeCardsFromDbAsync(
            List<OnlineEpisodeCardViewModel> episodeCards)
        {
            List<Guid> missingIds = new();

            foreach (OnlineEpisodeCardViewModel epCard in episodeCards)
            {
                if (epCard.CoverImage is null) missingIds.Add(epCard.EpisodeId);
            }

            if (missingIds.Count == 0) return;

            IReadOnlyDictionary<Guid, byte[]> coverMap = _coverService is not null
                ? await _coverService.GetEpisodeCoverBytesAsync(missingIds)
                : new Dictionary<Guid, byte[]>();

            foreach (OnlineEpisodeCardViewModel epCard in episodeCards)
            {
                if (epCard.CoverImage is not null) continue;

                if (coverMap.TryGetValue(epCard.EpisodeId, out byte[]? coverData))
                {
                    BitmapImage? coverImage = await CoverService.ConvertToBitmapAsync(coverData);

                    if (coverImage is not null)
                    {
                        epCard.CoverImage = coverImage;
                    }
                }
            }
        }

        /// <summary>
        /// Entfernt eine Online-Serie aus der Mediathek (Soft-Delete).
        /// Löscht Serie und zugehörige Episoden aus der DB, schließt das Akkordeon
        /// und entfernt die Serie aus der lokalen Liste.
        /// </summary>
        /// <param name="seriesId">Die ID der zu entfernenden Serie.</param>
        public async Task RemoveSeriesAsync(Guid seriesId)
        {
            // Serie in der internen Liste finden – für den Bestätigungsdialog
            SeriesCardViewModel? card = null;

            foreach (SeriesCardViewModel c in _allSeries)
            {
                if (c.Id == seriesId) { card = c; break; }
            }

            if (card is null) return;

            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                _localizationService.Get("OnlineRemoveSeriesDialogTitle"),
                string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    _localizationService.Get("OnlineRemoveSeriesDialogMessage"), card.Title));

            if (!confirmed) return;

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            await seriesService.DeleteAsync(seriesId);

            // Aus der lokalen Liste entfernen und UI aktualisieren
            List<SeriesCardViewModel> updated = new(_allSeries.Count);

            foreach (SeriesCardViewModel c in _allSeries)
            {
                if (c.Id != seriesId) updated.Add(c);
            }

            _allSeries = updated;
            DeselectSeries();
            ApplyFilter();
        }

        /// <summary>
        /// Hebt die Serien-Auswahl auf und schließt das Akkordeon.
        /// </summary>
        public void DeselectSeries()
        {
            // V-Indikator bei allen Serien zurücksetzen
            foreach (SeriesCardViewModel c in _allSeries)
            {
                c.IsSelectedInAccordion = false;
            }

            SelectedSeriesIndex = -1;
            _allEpisodes        = [];
            Episodes             = [];
        }

        // ── Filter und Sortierung ────────────────────────────────────────────────

        private void ApplyFilter()
        {
            List<SeriesCardViewModel> filtered = [];

            foreach (SeriesCardViewModel card in _allSeries)
            {
                if (!MatchesSearchText(card)) continue;
                if (!MatchesStatusFilter(card)) continue;
                filtered.Add(card);
            }

            // Serien-Sortierung anwenden
            IEnumerable<SeriesCardViewModel> sorted = _seriesSortIndex switch
            {
                1 => filtered.OrderByDescending(c => c.TotalEpisodeCount),
                2 => filtered.OrderBy(c => c.FinishedCount).ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
                _ => filtered.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            };

            Series = sorted.ToList();
            OnPropertyChanged(nameof(EmptyStateVisibility));
        }

        private bool MatchesSearchText(SeriesCardViewModel card)
        {
            return string.IsNullOrWhiteSpace(_searchText)
                || card.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
        }

        private bool MatchesStatusFilter(SeriesCardViewModel card)
        {
            return _statusFilter switch
            {
                SeriesStatusFilter.Neu      => card.HasNewEpisodes,
                SeriesStatusFilter.AmHoeren => card.HasInProgressEpisodes,
                SeriesStatusFilter.Gehört   => card.AllEpisodesFinished,
                _                           => true
            };
        }

        /// <summary>
        /// Sortiert die Episoden nach dem gewählten Kriterium.
        /// </summary>
        private void ApplyEpisodeSort()
        {
            IEnumerable<OnlineEpisodeCardViewModel> sorted = _episodeSortIndex switch
            {
                1 => _allEpisodes.OrderByDescending(e => e.Title, StringComparer.OrdinalIgnoreCase),
                2 => _allEpisodes.OrderByDescending(e => e.ReleaseDate ?? DateTime.MinValue),
                _ => _allEpisodes.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            };

            Episodes = sorted.ToList();
        }

        // ── Cover-Caching ────────────────────────────────────────────────────────

        /// <summary>
        /// Lädt fehlende Serien-Cover von der Provider-URL herunter und speichert sie in der DB.
        /// Wird im Hintergrund nach dem Laden ausgeführt – blockiert die UI nicht.
        /// Bereits gecachte Cover (CoverImages-Tabelle) werden übersprungen.
        /// </summary>
        private async Task CacheSeriesCoversAsync(IReadOnlyList<Series> seriesList)
        {
            if (_coverService is null) return;

            foreach (Series series in seriesList)
            {
                if (!series.IsOnlineImported) continue;
                if (string.IsNullOrEmpty(series.CoverImageUrl)) continue;

                // Prüfen ob bereits ein Cover in der CoverImages-Tabelle existiert
                bool hasCover = await _coverService.HasSeriesCoverAsync(series.Id);
                if (hasCover) continue;

                try
                {
                    // Kein ConfigureAwait(false): der UI-Thread wird für die
                    // BitmapImage-Erstellung und Kachel-Aktualisierung danach benötigt.
                    byte[] coverBytes = await _downloadClient.GetByteArrayAsync(series.CoverImageUrl);

                    if (coverBytes.Length == 0) continue;

                    // In CoverImages-Tabelle cachen – beim nächsten Laden kein Download mehr nötig
                    await _coverService.SetSeriesCoverAsync(series.Id, coverBytes, series.CoverImageUrl);

                    // Serien-Kachel aktualisieren (falls sichtbar)
                    SeriesCardViewModel? card = _allSeries.FirstOrDefault(c => c.Id == series.Id);
                    if (card is not null)
                    {
                        BitmapImage? coverImage = await CoverService.ConvertToBitmapAsync(coverBytes);

                        if (coverImage is not null)
                        {
                            card.CoverImage = coverImage;
                        }
                    }
                }
                catch (Exception)
                {
                    // Netzwerkfehler oder abgelaufene URL → Platzhalter, kein Absturz
                }
            }
        }

        // ── Provider-Suche ──────────────────────────────────────────────────────

        private async Task SearchProviderAsync()
        {
            if (_isSearchingProvider || string.IsNullOrWhiteSpace(_searchText)) return;

            // Offenes Akkordeon schließen – sonst überlagern sich Suchergebnisse und Folgen-Panel
            DeselectSeries();

            IsSearchingProvider = true;
            ProviderSearchResults = [];

            try
            {
                // Suchtyp: 0 = Alle (Standard), 1 = nur Serien, 2 = nur Folgen
                IReadOnlyList<ImportSeries> seriesResults = _searchTypeIndex == 2
                    ? []
                    : await _importService.SearchAsync(_searchText);
                IReadOnlyList<ImportSeries> albumResults = _searchTypeIndex == 1
                    ? []
                    : await _importService.SearchAlbumsAsync(_searchText);

                string searchLower = _searchText.ToLowerInvariant();
                List<ImportSeries> combined = new(seriesResults.Count + albumResults.Count);
                combined.AddRange(seriesResults);
                combined.AddRange(albumResults);
                combined.Sort((a, b) =>
                {
                    bool aContains = a.Title.Contains(searchLower, StringComparison.OrdinalIgnoreCase)
                                  || (a.ArtistName?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false);
                    bool bContains = b.Title.Contains(searchLower, StringComparison.OrdinalIgnoreCase)
                                  || (b.ArtistName?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false);

                    if (aContains != bContains) return aContains ? -1 : 1;
                    return b.Score.CompareTo(a.Score);
                });

                List<SearchResultViewModel> viewModels = new(combined.Count);

                foreach (ImportSeries series in combined)
                {
                    bool alreadyImported = await _importService.IsAlreadyImportedAsync(series);
                    viewModels.Add(new SearchResultViewModel(
                        series, alreadyImported, _importService, _errorDialogService,
                        _localizationService, onImportCompleted: ReloadAfterImportAsync));
                }

                ProviderSearchResults = viewModels;
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(
                    _localizationService.Get("OnlineSearchFailedTitle"), ex.Message);
            }
            finally
            {
                IsSearchingProvider = false;
            }
        }

        /// <summary>
        /// Lädt die Mediathek nach dem Hinzufügen einer Serie neu.
        /// </summary>
        public async Task ReloadAfterImportAsync()
        {
            ProviderSearchResults = [];
            await LoadAsync();
        }

        /// <summary>
        /// Importiert alle ausgewählten (angehakten) Suchergebnisse nacheinander.
        /// Bereits importierte Ergebnisse werden übersprungen. Nach dem letzten Import
        /// wird die Serienliste automatisch neu geladen.
        /// </summary>
        private async Task AddSelectedAsync()
        {
            List<SearchResultViewModel> selected = [];

            foreach (SearchResultViewModel result in _providerSearchResults)
            {
                if (result.IsSelected && !result.IsImported)
                {
                    selected.Add(result);
                }
            }

            if (selected.Count == 0) return;

            foreach (SearchResultViewModel result in selected)
            {
                // ImportCommand intern aufrufen – nutzt die gleiche Logik inkl. Fehlerbehandlung
                if (result.ImportCommand.CanExecute(null))
                {
                    result.ImportCommand.Execute(null);
                }
            }
        }

        /// <summary>
        /// Prüft alle Online-Serien auf neue Folgen beim Provider (Delta-Update).
        /// Zeigt Fortschritt im LoadingStatusText und lädt die Ansicht nach Abschluss neu.
        /// Im Offline-Modus wird vorher der Online-Zugang angefragt.
        /// </summary>
        private async Task RefreshAsync()
        {
            if (_isLoading) return;

            // Offline-Modus: Nutzer fragen, ob temporär online gegangen werden soll
            using IDisposable? onlineAccess = await _onlineAccessGuard.RequestOnlineAccessAsync();

            if (onlineAccess is null) return;

            IsLoading = true;

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();

                // Nur Online-Serien mit Provider-Zuordnung prüfen
                List<Series> onlineSeries = [];

                foreach (Series series in allSeries)
                {
                    if (series.IsOnlineImported) onlineSeries.Add(series);
                }

                int totalNew = 0;

                for (int i = 0; i < onlineSeries.Count; i++)
                {
                    Series series = onlineSeries[i];
                    LoadingStatusText = string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        _localizationService.Get("OnlineRefreshProgressText"),
                        i + 1, onlineSeries.Count, series.Title);

                    try
                    {
                        int newEpisodes = await _importService.DeltaImportEpisodesAsync(series);
                        totalNew += newEpisodes;
                    }
                    catch (Exception)
                    {
                        // Einzelne Serien-Fehler nicht abbrechen – nächste Serie prüfen
                    }

                    // Rate-Limiting: kurze Pause zwischen Provider-Aufrufen
                    if (i < onlineSeries.Count - 1)
                    {
                        await Task.Delay(1500);
                    }
                }

                LoadingStatusText = string.Empty;

                // Ansicht aktualisieren – neue Folgen sichtbar machen
                await LoadAsync();
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(
                    _localizationService.Get("OnlineRefreshFailedTitle"), ex.Message);
            }
            finally
            {
                LoadingStatusText = string.Empty;
                IsLoading = false;
            }
        }

        // ── Cover-Hilfsmethoden ─────────────────────────────────────────────────

        /// <summary>
        /// Erstellt ein <see cref="BitmapImage"/> aus den Seriendaten.
        /// Priorität: DB-Cover (CoverImages) → URL-Fallback (Übergangsanzeige).
        /// Der URL-Fallback zeigt das Cover sofort an, während <see cref="CacheSeriesCoversAsync"/>
        /// es im Hintergrund in die DB persistiert. Beim nächsten Öffnen kommt es aus der DB.
        /// </summary>
        private async Task<BitmapImage?> BuildCoverImageAsync(Series series)
        {
            // CoverService ist in Tests nicht verfügbar
            BitmapImage? coverImage = _coverService is not null
                ? await _coverService.GetSeriesCoverImageAsync(series.Id)
                : null;

            if (coverImage is not null)
            {
                return coverImage;
            }

            // Übergangsanzeige bis CacheSeriesCoversAsync das Cover in die DB geschrieben hat
            if (!string.IsNullOrEmpty(series.CoverImageUrl))
            {
                return new BitmapImage(new Uri(series.CoverImageUrl));
            }

            return null;
        }

    }
}

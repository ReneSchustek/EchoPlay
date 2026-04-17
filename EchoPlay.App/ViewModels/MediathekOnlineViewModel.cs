using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Net.Http;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die Online-Mediathek mit Akkordeon-Layout.
    /// Koordiniert drei Sub-VMs (Serienliste, Episoden, Provider-Suche) und den internen
    /// <see cref="MediathekOnlineActions"/>-Orchestrator, der die gesamte Async-Aktions-
    /// Schicht (Laden, Auswahl, Provider-Suche, Refresh, Cover-Pipeline) kapselt.
    /// Das Top-VM hält nur noch Commands, Cross-Cutting-Zustand und die Pass-Through-
    /// Schicht für die unveränderte Page-XAML.
    /// </summary>
    public sealed class MediathekOnlineViewModel : ObservableObject, IDisposable
    {
        private readonly MediathekOnlineActions _actions;
        private readonly IPageModeGuard? _pageModeGuard;
        private readonly INavigationService? _navigationService;
        private readonly EchoPlay.LocalLibrary.Cover.ICoverSearchService? _coverSearchService;

        private bool _isLoading;
        private string _loadingStatusText = string.Empty;
        private bool _hasNoProvider;

        /// <summary>
        /// Initialisiert das ViewModel mit den benötigten Services und erzeugt die drei
        /// Sub-VMs sowie den Aktions-Orchestrator.
        /// </summary>
        public MediathekOnlineViewModel(
            IServiceScopeFactory scopeFactory,
            IConfirmationDialogService confirmationDialogService,
            ImportService importService,
            IErrorDialogService errorDialogService,
            ILocalizationService localizationService,
            IOnlineAccessGuard onlineAccessGuard,
            IHttpClientFactory httpClientFactory,
            CoverBrightnessAnalyzer? coverBrightnessAnalyzer = null,
            EpisodeCoverCacheService? coverCacheService = null,
            CoverService? coverService = null,
            BackgroundCoverService? backgroundCoverService = null,
            IWatchToggleService? watchToggleService = null,
            IHostRateLimiter? rateLimiter = null,
            IPageModeGuard? pageModeGuard = null,
            EchoPlay.LocalLibrary.Cover.ICoverSearchService? coverSearchService = null,
            INavigationService? navigationService = null)
        {
            _pageModeGuard = pageModeGuard;
            _navigationService = navigationService;
            _coverSearchService = coverSearchService;

            SeriesVM = new OnlineSeriesViewModel();
            EpisodesVM = new OnlineEpisodesViewModel();
            ProviderSearchVM = new OnlineProviderSearchViewModel();

            MediathekOnlineActionsContext actionsContext = new(
                scopeFactory,
                confirmationDialogService,
                importService,
                errorDialogService,
                localizationService,
                onlineAccessGuard,
                coverCacheService,
                coverService!,
                backgroundCoverService,
                watchToggleService,
                httpClientFactory,
                coverBrightnessAnalyzer,
                rateLimiter);

            _actions = new MediathekOnlineActions(
                actionsContext,
                SeriesVM, EpisodesVM, ProviderSearchVM,
                setIsLoading: v => IsLoading = v,
                setLoadingStatusText: v => LoadingStatusText = v,
                setHasNoProvider: v => HasNoProvider = v,
                reloadAfterImportAsync: ReloadAfterImportAsync);

            // PropertyChanged der Sub-VMs an eigene Pass-Through-Properties weiterreichen,
            // damit die XAML-Bindings auf Top-VM-Properties unverändert funktionieren.
            SeriesVM.PropertyChanged += OnSubVmPropertyChanged;
            EpisodesVM.PropertyChanged += OnSubVmPropertyChanged;
            ProviderSearchVM.PropertyChanged += OnSubVmPropertyChanged;

            // Cross-Cutting-Visibilities neu berechnen, wenn eine der beteiligten Größen wechselt
            ProviderSearchVM.PropertyChanged += OnProviderSearchPropertyChanged;
            SeriesVM.PropertyChanged += OnSeriesPropertyChanged;

            ProviderSearchCommand = new RelayCommand(() => _ = _actions.SearchProviderAsync(SeriesVM.SearchText));
            AddSelectedCommand = new RelayCommand(() => _actions.AddSelected());
            RefreshCommand = new RelayCommand(() => _ = _actions.RefreshAllOnlineSeriesAsync());
            GoToSettingsCommand = new RelayCommand(() => _navigationService?.NavigateTo(NavigationTarget.Settings));
            FocusSearchCommand = new RelayCommand(StartSearchFromEmptyState);
        }

        // ── Sub-VMs ─────────────────────────────────────────────────────────────

        /// <summary>Sub-VM für die Serienliste mit Filtern, Sortierung und Auswahl.</summary>
        public OnlineSeriesViewModel SeriesVM { get; }

        /// <summary>Sub-VM für die Episoden der aktuell gewählten Serie.</summary>
        public OnlineEpisodesViewModel EpisodesVM { get; }

        /// <summary>Sub-VM für die Provider-Suche (Spotify/Apple Music).</summary>
        public OnlineProviderSearchViewModel ProviderSearchVM { get; }

        // ── Events (Top-VM → Page) ──────────────────────────────────────────────

        /// <summary>
        /// Wird ausgelöst, wenn der Empty-State-Button „Serie suchen" geklickt wurde.
        /// Die Page setzt den Fokus auf die Suchbox – im VM existiert kein WinUI-Konzept.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "VM->Page-Fokus-Signal ohne Nutzdaten: die Page setzt Focus auf die Suchbox, das VM kennt keinen FocusManager; Action ist semantisch klarer als leerer EventArgs.")]
        public event Action? FocusSearchRequested;

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>Startet die Provider-Suche.</summary>
        public ICommand ProviderSearchCommand { get; }

        /// <summary>Importiert alle ausgewählten Suchergebnisse.</summary>
        public ICommand AddSelectedCommand { get; }

        /// <summary>Prüft alle Online-Serien auf neue Folgen beim Provider.</summary>
        public ICommand RefreshCommand { get; }

        /// <summary>Navigiert in die Einstellungen – aus dem Empty-State „Kein Anbieter".</summary>
        public ICommand GoToSettingsCommand { get; }

        /// <summary>Setzt den Fokus auf die Suchleiste – aus dem Empty-State „Keine Serien".</summary>
        public ICommand FocusSearchCommand { get; }

        // ── Top-Level-Zustand ───────────────────────────────────────────────────

        /// <summary>Gibt an, ob gerade ein Ladevorgang läuft.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    OnPropertyChanged(nameof(EmptyStateVisibility));
                }
            }
        }

        /// <summary>Statustext während des Ladevorgangs – z.B. bei der Episoden-Nachladung.</summary>
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
                    OnPropertyChanged(nameof(NoProviderVisibility));
                    OnPropertyChanged(nameof(NoSeriesVisibility));
                }
            }
        }

        // ── Cross-Cutting-Visibilities ──────────────────────────────────────────

        /// <summary>Leer-Zustand sichtbar wenn keine Serien, keine Suchergebnisse und kein Ladevorgang.</summary>
        public Visibility EmptyStateVisibility =>
            !_isLoading && !SeriesVM.HasFilteredSeries && !ProviderSearchVM.HasResults
                ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des „Kein Provider"-Hinweises.</summary>
        public Visibility NoProviderVisibility =>
            _hasNoProvider ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des „Keine Serien"-Hinweises.</summary>
        public Visibility NoSeriesVisibility =>
            !_hasNoProvider ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Bibliothek sichtbar wenn keine Suchergebnisse und nicht gesucht wird.</summary>
        public Visibility LibraryVisibility =>
            !ProviderSearchVM.HasResults && !ProviderSearchVM.IsSearchingProvider
                ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des Suchtyp-Filters – nur bei aktiven Suchergebnissen.</summary>
        public Visibility SearchTypeFilterVisibility =>
            ProviderSearchVM.HasResults || ProviderSearchVM.IsSearchingProvider
                ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des Statusfilters – nur in der Bibliothek (keine Suchergebnisse).</summary>
        public Visibility StatusFilterVisibility =>
            !ProviderSearchVM.HasResults && !ProviderSearchVM.IsSearchingProvider
                ? Visibility.Visible : Visibility.Collapsed;

        // ── Pass-Through-Eigenschaften: Serien ──────────────────────────────────

        /// <inheritdoc cref="OnlineSeriesViewModel.Series"/>
        public IReadOnlyList<SeriesCardViewModel> Series => SeriesVM.Series;

        /// <inheritdoc cref="OnlineSeriesViewModel.SearchText"/>
        public string SearchText
        {
            get => SeriesVM.SearchText;
            set
            {
                SeriesVM.SearchText = value;

                // Leer-Button im Suchfeld: auch Provider-Suchergebnisse zurücksetzen
                if (string.IsNullOrEmpty(value))
                {
                    ProviderSearchVM.ClearResults();
                }
            }
        }

        /// <inheritdoc cref="OnlineSeriesViewModel.StatusFilter"/>
        public SeriesStatusFilter StatusFilter
        {
            get => SeriesVM.StatusFilter;
            set => SeriesVM.StatusFilter = value;
        }

        /// <inheritdoc cref="OnlineSeriesViewModel.SeriesSortIndex"/>
        public int SeriesSortIndex
        {
            get => SeriesVM.SeriesSortIndex;
            set => SeriesVM.SeriesSortIndex = value;
        }

        /// <inheritdoc cref="OnlineSeriesViewModel.SelectedSeriesIndex"/>
        public int SelectedSeriesIndex => SeriesVM.SelectedSeriesIndex;

        /// <inheritdoc cref="OnlineSeriesViewModel.EpisodesAccordionVisibility"/>
        public Visibility EpisodesAccordionVisibility => SeriesVM.EpisodesAccordionVisibility;

        // ── Pass-Through-Eigenschaften: Episoden ────────────────────────────────

        /// <inheritdoc cref="OnlineEpisodesViewModel.Episodes"/>
        public IReadOnlyList<OnlineEpisodeCardViewModel> Episodes => EpisodesVM.Episodes;

        /// <inheritdoc cref="OnlineEpisodesViewModel.EpisodeSortIndex"/>
        public int EpisodeSortIndex
        {
            get => EpisodesVM.EpisodeSortIndex;
            set => EpisodesVM.EpisodeSortIndex = value;
        }

        /// <inheritdoc cref="OnlineEpisodesViewModel.IsLoadingEpisodes"/>
        public bool IsLoadingEpisodes => EpisodesVM.IsLoadingEpisodes;

        /// <inheritdoc cref="OnlineEpisodesViewModel.LoadingEpisodesVisibility"/>
        public Visibility LoadingEpisodesVisibility => EpisodesVM.LoadingEpisodesVisibility;

        // ── Pass-Through-Eigenschaften: Provider-Suche ──────────────────────────

        /// <inheritdoc cref="OnlineProviderSearchViewModel.ProviderSearchResults"/>
        public IReadOnlyList<SearchResultViewModel> ProviderSearchResults => ProviderSearchVM.ProviderSearchResults;

        /// <inheritdoc cref="OnlineProviderSearchViewModel.IsSearchingProvider"/>
        public bool IsSearchingProvider => ProviderSearchVM.IsSearchingProvider;

        /// <inheritdoc cref="OnlineProviderSearchViewModel.SearchTypeIndex"/>
        public int SearchTypeIndex
        {
            get => ProviderSearchVM.SearchTypeIndex;
            set => ProviderSearchVM.SearchTypeIndex = value;
        }

        /// <inheritdoc cref="OnlineProviderSearchViewModel.ProviderSearchResultsVisibility"/>
        public Visibility ProviderSearchResultsVisibility => ProviderSearchVM.ProviderSearchResultsVisibility;

        // ── Öffentliche Methoden (Delegation an den Orchestrator) ──────────────

        /// <summary>
        /// Wird vom Code-Behind beim Betreten der Seite aufgerufen. Prüft den Offline-Modus
        /// und liefert <see langword="false"/>, falls die Page nicht weiter geladen werden soll.
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            if (_pageModeGuard is null)
            {
                return true;
            }

            return await _pageModeGuard.EnsureOnlineAccessAsync();
        }

        /// <inheritdoc cref="MediathekOnlineActions.LoadAsync"/>
        public Task LoadAsync() => _actions.LoadAsync();

        /// <inheritdoc cref="MediathekOnlineActions.SelectSeriesAsync"/>
        public Task SelectSeriesAsync(SeriesCardViewModel card)
        {
            ArgumentNullException.ThrowIfNull(card);
            return _actions.SelectSeriesAsync(card);
        }

        /// <summary>Hebt die Serien-Auswahl auf und schließt das Akkordeon.</summary>
        public void DeselectSeries()
        {
            SeriesVM.DeselectSeries();
            EpisodesVM.Clear();
        }

        /// <inheritdoc cref="MediathekOnlineActions.RemoveSeriesAsync"/>
        public Task RemoveSeriesAsync(Guid seriesId) => _actions.RemoveSeriesAsync(seriesId);

        /// <inheritdoc cref="MediathekOnlineActions.ToggleWatchAsync"/>
        public Task ToggleWatchAsync(Guid seriesId, bool watch) => _actions.ToggleWatchAsync(seriesId, watch);

        /// <summary>
        /// Sucht Cover-Kandidaten für einen Episoden-Titel über den Cover-Suchdienst.
        /// </summary>
        public Task<IReadOnlyList<CoverSearchHit>> SearchEpisodeCoversAsync(string query, CancellationToken ct)
            => MediathekOnlineActions.SearchEpisodeCoversAsync(_coverSearchService, query, ct);

        /// <inheritdoc cref="MediathekOnlineActions.ApplySelectedEpisodeCoverAsync"/>
        public Task ApplySelectedEpisodeCoverAsync(OnlineEpisodeCardViewModel card, CoverSearchHit hit)
        {
            ArgumentNullException.ThrowIfNull(card);
            ArgumentNullException.ThrowIfNull(hit);
            return _actions.ApplySelectedEpisodeCoverAsync(card, hit);
        }

        /// <summary>Lädt die Mediathek nach einem erfolgreichen Import neu.</summary>
        public async Task ReloadAfterImportAsync()
        {
            ProviderSearchVM.ClearResults();
            await _actions.LoadAsync();
        }

        // ── Empty-State-Hilfen ──────────────────────────────────────────────────

        /// <summary>
        /// Wird aus dem Empty-State-Button „Serie suchen" ausgelöst: löst das
        /// <see cref="FocusSearchRequested"/>-Event aus und startet bei vorhandenem Suchtext
        /// sofort die Provider-Suche.
        /// </summary>
        private void StartSearchFromEmptyState()
        {
            FocusSearchRequested?.Invoke();

            if (!string.IsNullOrWhiteSpace(SearchText) && ProviderSearchCommand.CanExecute(null))
            {
                ProviderSearchCommand.Execute(null);
            }
        }

        // ── Sub-VM-Verdrahtung ─────────────────────────────────────────────────

        private void OnSubVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(e.PropertyName);
        }

        private void OnProviderSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Cross-Cutting-Visibilities neu berechnen, wenn sich der Such-Zustand ändert
            if (e.PropertyName is nameof(OnlineProviderSearchViewModel.ProviderSearchResults)
                               or nameof(OnlineProviderSearchViewModel.IsSearchingProvider)
                               or nameof(OnlineProviderSearchViewModel.ProviderSearchResultsVisibility)
                               or nameof(OnlineProviderSearchViewModel.HasResults))
            {
                OnPropertyChanged(nameof(EmptyStateVisibility));
                OnPropertyChanged(nameof(LibraryVisibility));
                OnPropertyChanged(nameof(SearchTypeFilterVisibility));
                OnPropertyChanged(nameof(StatusFilterVisibility));
            }
        }

        private void OnSeriesPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Cross-Cutting-Visibilities neu berechnen, wenn sich die Serienanzahl ändert
            if (e.PropertyName is nameof(OnlineSeriesViewModel.HasFilteredSeries)
                               or nameof(OnlineSeriesViewModel.Series))
            {
                OnPropertyChanged(nameof(EmptyStateVisibility));
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _actions.Dispose();
        }
    }
}

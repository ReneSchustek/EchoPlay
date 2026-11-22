using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System.ComponentModel;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die Online-Mediathek mit Akkordeon-Layout.
    /// Koordiniert drei Sub-VMs (Serienliste, Episoden, Provider-Suche) und den internen
    /// <see cref="OnlineLibraryActions"/>-Orchestrator, der die gesamte Async-Aktions-
    /// Schicht (Laden, Auswahl, Provider-Suche, Refresh, Cover-Pipeline) kapselt.
    /// Das Top-VM hält Commands, Cross-Cutting-Zustand und die Pass-Through-Schicht für
    /// die Page-XAML.
    /// </summary>
    public sealed class OnlineLibraryViewModel : ObservableObject, IDisposable
    {
        private readonly OnlineLibraryActions _actions;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly EchoPlay.Logger.Abstractions.ILogger? _logger;
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
        /// <param name="services">Die Dienste der Online-Mediathek.</param>
        /// <param name="pageModeGuard">Prüft beim Betreten, ob die Seite im aktuellen Modus erlaubt ist.</param>
        /// <param name="coverSearchService">Sucht Cover-Kandidaten für eine Folge.</param>
        /// <param name="navigationService">Führt aus dem Leer-Zustand in die Einstellungen.</param>
        public OnlineLibraryViewModel(
            OnlineLibraryActionsContext services,
            IPageModeGuard? pageModeGuard = null,
            EchoPlay.LocalLibrary.Cover.ICoverSearchService? coverSearchService = null,
            INavigationService? navigationService = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            _scopeFactory = services.ScopeFactory;
            _logger = EchoPlay.App.Infrastructure.ScopedLogger.Create(services.ScopeFactory, nameof(OnlineLibraryViewModel));
            _pageModeGuard = pageModeGuard;
            _navigationService = navigationService;
            _coverSearchService = coverSearchService;

            SeriesVM = new OnlineSeriesViewModel();
            EpisodesVM = new OnlineEpisodesViewModel();
            ProviderSearchVM = new OnlineProviderSearchViewModel();

            _actions = CreateActions(services);
            SubscribeToSubViewModels();

            ProviderSearchCommand = new RelayCommand(() => _ = _actions.SearchProviderAsync(SeriesVM.SearchText));
            AddSelectedCommand = new RelayCommand(() => _actions.AddSelected());
            RefreshCommand = new RelayCommand(() => _ = _actions.RefreshAllOnlineSeriesAsync());
            GoToSettingsCommand = new RelayCommand(() => _navigationService?.NavigateTo(NavigationTarget.Settings));
            FocusSearchCommand = new RelayCommand(StartSearchFromEmptyState);
        }

        /// <summary>
        /// Baut die Aktionen; sie schreiben Lade- und Anbieterzustand über Rückrufe zurück.
        /// </summary>
        private OnlineLibraryActions CreateActions(OnlineLibraryActionsContext actionsContext) =>
            new(
                actionsContext,
                new OnlineLibraryParts(SeriesVM, EpisodesVM, ProviderSearchVM),
                new OnlineLibraryCallbacks(
                    SetIsLoading: v => IsLoading = v,
                    SetLoadingStatusText: v => LoadingStatusText = v,
                    SetHasNoProvider: v => HasNoProvider = v,
                    ReloadAfterImportAsync: ReloadAfterImportAsync));

        private void SubscribeToSubViewModels()
        {
            // PropertyChanged der Sub-VMs an eigene Pass-Through-Properties weiterreichen,
            // damit die XAML-Bindings auf Top-VM-Properties unverändert funktionieren.
            SeriesVM.PropertyChanged += OnSubVmPropertyChanged;
            EpisodesVM.PropertyChanged += OnSubVmPropertyChanged;
            ProviderSearchVM.PropertyChanged += OnSubVmPropertyChanged;

            // Cross-Cutting-Visibilities neu berechnen, wenn eine der beteiligten Größen wechselt
            ProviderSearchVM.PropertyChanged += OnProviderSearchPropertyChanged;
            SeriesVM.PropertyChanged += OnSeriesPropertyChanged;
        }

        /// <summary>Sub-VM für die Serienliste mit Filtern, Sortierung und Auswahl.</summary>
        public OnlineSeriesViewModel SeriesVM { get; }

        /// <summary>Sub-VM für die Episoden der aktuell gewählten Serie.</summary>
        public OnlineEpisodesViewModel EpisodesVM { get; }

        /// <summary>Sub-VM für die Provider-Suche (Spotify/Apple Music).</summary>
        public OnlineProviderSearchViewModel ProviderSearchVM { get; }

        /// <summary>
        /// Wird ausgelöst, wenn der Empty-State-Button „Serie suchen" geklickt wurde.
        /// Die Page setzt den Fokus auf die Suchbox, denn im VM existiert kein WinUI-Konzept.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Der Typ ist nur öffentlich, weil WinUI-Bindungen öffentliche Typen verlangen; einen Abonnenten außerhalb der Anwendung gibt es nicht. Gleich gebaute Ereignisse in internen Klassen bleiben bei Action — ein EventArgs-Typ allein an den öffentlichen Stellen führte ein zweites Muster ein, ohne dass jemand davon profitiert.")]
        public event Action? FocusSearchRequested;

        /// <summary>Startet die Provider-Suche.</summary>
        public ICommand ProviderSearchCommand { get; }

        /// <summary>Importiert alle ausgewählten Suchergebnisse.</summary>
        public ICommand AddSelectedCommand { get; }

        /// <summary>Prüft alle Online-Serien auf neue Folgen beim Provider.</summary>
        public ICommand RefreshCommand { get; }

        /// <summary>Navigiert in die Einstellungen, aus dem Empty-State „Kein Anbieter".</summary>
        public ICommand GoToSettingsCommand { get; }

        /// <summary>Setzt den Fokus auf die Suchleiste, aus dem Empty-State „Keine Serien".</summary>
        public ICommand FocusSearchCommand { get; }

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

        /// <summary>Statustext während des Ladevorgangs, z.B. bei der Episoden-Nachladung.</summary>
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

        /// <summary>Sichtbarkeit des Suchtyp-Filters, nur bei aktiven Suchergebnissen.</summary>
        public Visibility SearchTypeFilterVisibility =>
            ProviderSearchVM.HasResults || ProviderSearchVM.IsSearchingProvider
                ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des Statusfilters, nur in der Bibliothek (keine Suchergebnisse).</summary>
        public Visibility StatusFilterVisibility =>
            !ProviderSearchVM.HasResults && !ProviderSearchVM.IsSearchingProvider
                ? Visibility.Visible : Visibility.Collapsed;

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

        /// <inheritdoc cref="OnlineEpisodesViewModel.Episodes"/>
        public IReadOnlyList<OnlineEpisodeCardViewModel> Episodes => EpisodesVM.Episodes;

        /// <inheritdoc cref="OnlineEpisodesViewModel.EpisodeSortIndex"/>
        public int EpisodeSortIndex
        {
            get => EpisodesVM.EpisodeSortIndex;
            set
            {
                if (EpisodesVM.EpisodeSortIndex == value)
                {
                    return;
                }

                EpisodesVM.EpisodeSortIndex = value;

                // Nutzer-Auswahl dauerhaft merken, damit sie über Serienwechsel und App-Neustarts
                // erhalten bleibt. Fire-and-forget: die Sortierung ist bereits angewandt, der DB-
                // Schreibvorgang darf die UI nicht blockieren.
                _ = PersistEpisodeSortIndexAsync(value);
            }
        }

        /// <summary>
        /// Schreibt den gewählten Folgen-Sortierindex in die AppSettings. Fehler werden
        /// verschluckt, denn eine nicht gespeicherte Sortier-Vorliebe ist kein nutzerrelevanter Fehler.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Best-effort-Persistenz einer UI-Vorliebe: DB-/SQLite-Fehler beim Speichern der Sortierung dürfen die bereits angewandte Sortierung und die UI nicht beeinträchtigen.")]
        private async Task PersistEpisodeSortIndexAsync(int sortIndex)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IAppSettingsDataService settingsService =
                    scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();

                AppSettings settings = await settingsService.GetAsync();
                if (settings.OnlineEpisodeSortIndex == sortIndex)
                {
                    return;
                }

                settings.OnlineEpisodeSortIndex = sortIndex;
                await settingsService.SaveAsync(settings);
            }
            catch (Exception ex)
            {
                // Die Sortierung gilt für diese Sitzung trotzdem; nur gemerkt wird sie nicht.
                _logger?.Warning("Sortierung der Folgen nicht gespeichert: {Reason}", ex.Message);
            }
        }

        /// <summary>
        /// Stellt die zuletzt gespeicherte Folgen-Sortierung wieder her. Setzt den Wert direkt am
        /// Sub-VM (nicht über den persistierenden Setter), damit das Wiederherstellen keinen
        /// erneuten Schreibvorgang auslöst.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Best-effort-Wiederherstellung einer UI-Vorliebe: schlägt der Settings-Zugriff fehl, bleibt der Standard aktiv; das darf den Seitenaufbau nicht blockieren.")]
        private async Task RestoreEpisodeSortIndexAsync()
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IAppSettingsDataService settingsService =
                    scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();

                AppSettings settings = await settingsService.GetAsync();
                EpisodesVM.EpisodeSortIndex = settings.OnlineEpisodeSortIndex;
            }
            catch (Exception ex)
            {
                // Ohne gespeicherte Vorliebe bleibt der Standard (Nummer aufsteigend).
                _logger?.Warning("Gespeicherte Sortierung der Folgen nicht lesbar: {Reason}", ex.Message);
            }
        }

        /// <inheritdoc cref="OnlineEpisodesViewModel.IsLoadingEpisodes"/>
        public bool IsLoadingEpisodes => EpisodesVM.IsLoadingEpisodes;

        /// <inheritdoc cref="OnlineEpisodesViewModel.LoadingEpisodesVisibility"/>
        public Visibility LoadingEpisodesVisibility => EpisodesVM.LoadingEpisodesVisibility;

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

        /// <inheritdoc cref="OnlineProviderSearchViewModel.IsSpotifyFallbackHintVisible"/>
        public bool IsSpotifyFallbackHintVisible => ProviderSearchVM.IsSpotifyFallbackHintVisible;

        /// <inheritdoc cref="OnlineProviderSearchViewModel.SpotifyFallbackHintVisibility"/>
        public Visibility SpotifyFallbackHintVisibility => ProviderSearchVM.SpotifyFallbackHintVisibility;

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

        /// <inheritdoc cref="OnlineLibraryActions.LoadAsync"/>
        public async Task LoadAsync()
        {
            // Gespeicherte Folgen-Sortierung wiederherstellen, bevor Episoden angezeigt werden,
            // damit die Auswahl über App-Neustarts erhalten bleibt.
            await RestoreEpisodeSortIndexAsync();
            await _actions.LoadAsync();
        }

        /// <inheritdoc cref="OnlineLibraryActions.SelectSeriesAsync"/>
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

        /// <inheritdoc cref="OnlineLibraryActions.RemoveSeriesAsync"/>
        public Task RemoveSeriesAsync(Guid seriesId) => _actions.RemoveSeriesAsync(seriesId);

        /// <inheritdoc cref="OnlineLibraryActions.ToggleWatchAsync"/>
        public Task ToggleWatchAsync(Guid seriesId, bool watch) => _actions.ToggleWatchAsync(seriesId, watch);

        /// <summary>
        /// Sucht Cover-Kandidaten für einen Episoden-Titel über den Cover-Suchdienst.
        /// </summary>
        public Task<IReadOnlyList<CoverSearchHit>> SearchEpisodeCoversAsync(string query, EchoPlay.LocalLibrary.Cover.CoverSearchPage page, CancellationToken ct)
            => OnlineLibraryActions.SearchEpisodeCoversAsync(_coverSearchService, query, page, ct);

        /// <inheritdoc cref="OnlineLibraryActions.ApplySelectedEpisodeCoverAsync"/>
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

            if (e.PropertyName is nameof(OnlineProviderSearchViewModel.IsSpotifyFallbackHintVisible)
                               or nameof(OnlineProviderSearchViewModel.SpotifyFallbackHintVisibility))
            {
                OnPropertyChanged(nameof(IsSpotifyFallbackHintVisible));
                OnPropertyChanged(nameof(SpotifyFallbackHintVisibility));
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

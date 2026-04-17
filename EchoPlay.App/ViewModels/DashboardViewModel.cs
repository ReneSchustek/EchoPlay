using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die Startseite (Dashboard).
    /// Koordiniert fünf Sub-VMs (Neuerscheinungen, Favoriten, Weiterhören, In Progress,
    /// Zuletzt gehört), lädt gemeinsame Daten einmalig aus der DB und verteilt sie über
    /// den <see cref="DashboardDataLoader"/> an die Sektionen.
    /// Die Page-XAML bindet weiterhin gegen <c>ViewModel.NewEpisodeGroups</c>,
    /// <c>ViewModel.FavoriteSeries</c>, <c>ViewModel.RecentSeries</c> usw. – per
    /// Pass-Through auf die Sub-VMs.
    /// </summary>
    public sealed class DashboardViewModel : ObservableObject, IDisposable
    {
        // Dashboard-Sektionsname für Positionsspeicherung der Neuerscheinungen
        private const string SectionNewReleases = "Neuerscheinungen";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly ILocalizationService? _localizationService;
        private readonly ILogger _logger;
        private readonly DashboardDataLoader _dataLoader;

        private bool _isLoading;
        private bool _hasSubscribedSeries = true;
        private bool _hasFavoriteSeries;

        /// <summary>
        /// Initialisiert das ViewModel mit allen benötigten Services.
        /// </summary>
        /// <param name="scopeFactory">Für scoped DB-Zugriffe in LoadAsync und in den Kachel-Commands.</param>
        /// <param name="errorDialogService">Für Info-Dialoge bei nicht verfügbaren Episoden.</param>
        /// <param name="confirmationDialogService">Für Bestätigungs-Dialoge vor Statusänderungen.</param>
        /// <param name="playerService">Für das Starten der Wiedergabe.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        /// <param name="coverService">Zentraler Cover-Dienst für DB-basierte Cover. Nullable für Tests.</param>
        /// <param name="localizationService">Liefert lokalisierte UI-Strings. Nullable für Tests.</param>
        /// <param name="clock">Abstrahierte Uhr für testbare Zeitstempel. Nullable – Fallback auf <see cref="SystemClock"/>.</param>
        public DashboardViewModel(
            IServiceScopeFactory scopeFactory,
            IErrorDialogService errorDialogService,
            IConfirmationDialogService confirmationDialogService,
            IPlayerService playerService,
            ILoggerFactory loggerFactory,
            CoverService? coverService = null,
            ILocalizationService? localizationService = null,
            IClock? clock = null)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _confirmationDialogService = confirmationDialogService;
            _localizationService = localizationService;
            _logger = loggerFactory.CreateLogger("DashboardViewModel");

            IClock resolvedClock = clock ?? new SystemClock();

            _dataLoader = new DashboardDataLoader(
                scopeFactory,
                errorDialogService,
                confirmationDialogService,
                playerService,
                coverService,
                localizationService,
                resolvedClock,
                _logger);

            // Sub-VMs initialisieren und PropertyChanged an eigene Pass-Through-Properties weiterreichen,
            // damit die XAML-Bindings auf Top-VM-Properties unverändert funktionieren.
            NeuerscheinungenVM = new DashboardNeuerscheinungenViewModel();
            FavoritenVM = new DashboardFavoritenViewModel(scopeFactory, _logger);
            WeiterhoerenVM = new DashboardWeiterhoerenViewModel();
            InProgressVM = new DashboardInProgressViewModel();
            ZuletztGehoertVM = new DashboardZuletztGehoertViewModel();

            NeuerscheinungenVM.PropertyChanged += OnSubVmPropertyChanged;
            FavoritenVM.PropertyChanged += OnSubVmPropertyChanged;
            WeiterhoerenVM.PropertyChanged += OnSubVmPropertyChanged;
            InProgressVM.PropertyChanged += OnSubVmPropertyChanged;
            ZuletztGehoertVM.PropertyChanged += OnSubVmPropertyChanged;

            // FavoritesChanged löst die Neuberechnung des NoFavoritesHint-Visibility-Flags aus,
            // wenn der Nutzer Favoriten entfernt oder umsortiert.
            FavoritenVM.FavoritesChanged += OnFavoritesChanged;
        }

        // ── Sub-VMs ─────────────────────────────────────────────────────────────

        /// <summary>Sub-VM für den Neuerscheinungen-Abschnitt.</summary>
        public DashboardNeuerscheinungenViewModel NeuerscheinungenVM { get; }

        /// <summary>Sub-VM für den Favoriten-Abschnitt.</summary>
        public DashboardFavoritenViewModel FavoritenVM { get; }

        /// <summary>Sub-VM für den „Weiterhören"-Abschnitt.</summary>
        public DashboardWeiterhoerenViewModel WeiterhoerenVM { get; }

        /// <summary>Sub-VM für den „In Progress"-Abschnitt.</summary>
        public DashboardInProgressViewModel InProgressVM { get; }

        /// <summary>Sub-VM für den „Zuletzt gehört"-Abschnitt.</summary>
        public DashboardZuletztGehoertViewModel ZuletztGehoertVM { get; }

        // ── Top-VM-Zustand ──────────────────────────────────────────────────────

        /// <summary>
        /// Gibt an, ob mindestens eine abonnierte Serie vorhanden ist.
        /// Wird auf <see langword="false"/> gesetzt, wenn die Datenbank keine abonnierten Serien
        /// enthält – dann navigiert die Startseite automatisch zur Suche (Onboarding).
        /// </summary>
        public bool HasSubscribedSeries
        {
            get => _hasSubscribedSeries;
            private set
            {
                if (SetProperty(ref _hasSubscribedSeries, value))
                {
                    OnPropertyChanged(nameof(NoFavoritesHintVisibility));
                }
            }
        }

        /// <summary>
        /// Gibt an, ob gerade ein Ladevorgang läuft. Steuert den ProgressRing auf der Startseite.
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Hinweis, wenn abonnierte Serien vorhanden sind, aber noch keine favorisiert wurden.
        /// Steuert die Sichtbarkeit des Hinweistexts im Neuerscheinungen-Abschnitt.
        /// </summary>
        public Visibility NoFavoritesHintVisibility =>
            _hasSubscribedSeries && !_hasFavoriteSeries ? Visibility.Visible : Visibility.Collapsed;

        // ── Pass-Through-Eigenschaften ──────────────────────────────────────────

        /// <inheritdoc cref="DashboardNeuerscheinungenViewModel.NewEpisodeGroups"/>
        public ObservableCollection<NewEpisodesGroupViewModel> NewEpisodeGroups => NeuerscheinungenVM.NewEpisodeGroups;

        /// <inheritdoc cref="DashboardNeuerscheinungenViewModel.NewEpisodeGroupsVisibility"/>
        public Visibility NewEpisodeGroupsVisibility => NeuerscheinungenVM.NewEpisodeGroupsVisibility;

        /// <inheritdoc cref="DashboardNeuerscheinungenViewModel.NewReleasesLoadingVisibility"/>
        public Visibility NewReleasesLoadingVisibility => NeuerscheinungenVM.NewReleasesLoadingVisibility;

        /// <inheritdoc cref="DashboardNeuerscheinungenViewModel.NewReleasesSectionVisibility"/>
        public Visibility NewReleasesSectionVisibility => NeuerscheinungenVM.NewReleasesSectionVisibility;

        /// <inheritdoc cref="DashboardNeuerscheinungenViewModel.IsLoadingNewReleases"/>
        public bool IsLoadingNewReleases => NeuerscheinungenVM.IsLoadingNewReleases;

        /// <inheritdoc cref="DashboardFavoritenViewModel.FavoriteSeries"/>
        public ObservableCollection<FavoriteSeriesCardViewModel> FavoriteSeries => FavoritenVM.FavoriteSeries;

        /// <inheritdoc cref="DashboardFavoritenViewModel.FavoriteSectionVisibility"/>
        public Visibility FavoriteSectionVisibility => FavoritenVM.FavoriteSectionVisibility;

        /// <inheritdoc cref="DashboardWeiterhoerenViewModel.UnheardSeries"/>
        public IReadOnlyList<UnheardSeriesCardViewModel> UnheardSeries => WeiterhoerenVM.UnheardSeries;

        /// <inheritdoc cref="DashboardWeiterhoerenViewModel.UnheardSectionVisibility"/>
        public Visibility UnheardSectionVisibility => WeiterhoerenVM.UnheardSectionVisibility;

        /// <inheritdoc cref="DashboardInProgressViewModel.InProgressEpisodes"/>
        public IReadOnlyList<NewEpisodeCardViewModel> InProgressEpisodes => InProgressVM.InProgressEpisodes;

        /// <inheritdoc cref="DashboardInProgressViewModel.InProgressSectionVisibility"/>
        public Visibility InProgressSectionVisibility => InProgressVM.InProgressSectionVisibility;

        /// <inheritdoc cref="DashboardZuletztGehoertViewModel.RecentSeries"/>
        public IReadOnlyList<RecentSeriesCardViewModel> RecentSeries => ZuletztGehoertVM.RecentSeries;

        /// <inheritdoc cref="DashboardZuletztGehoertViewModel.RecentSectionVisibility"/>
        public Visibility RecentSectionVisibility => ZuletztGehoertVM.RecentSectionVisibility;

        /// <inheritdoc cref="DashboardFavoritenViewModel.SaveFavoriteSeriesOrderAsync"/>
        public Task SaveFavoriteSeriesOrderAsync() => FavoritenVM.SaveFavoriteSeriesOrderAsync();

        // ── Laden ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Lädt alle Abschnitte der Startseite und verteilt die Ergebnisse an die Sub-VMs.
        /// Neuerscheinungen werden im Offline-Modus übersprungen.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task LoadAsync()
        {
            IsLoading = true;

            // StartupResult wurde im Splash vorgeladen – enthält den Offline-Status.
            StartupResult? startupResult = null;
            try
            {
                startupResult = App.StartupResultData;
            }
            catch (InvalidOperationException)
            {
                // App.StartupResultData kann InvalidOperationException werfen,
                // wenn der Splash noch nicht abgeschlossen ist – Fallback auf DB-Abfrage
            }

            bool offlineMode = false;
            IReadOnlyList<Series> subscribedSeries = [];

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                IPlaybackStateDataService stateService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

                // Serien immer frisch laden – IsWatched kann sich während der Session ändern
                subscribedSeries = await seriesService.GetSubscribedAsync();

                // Offline-Status: aus StartupResult (Konnektivitäts-Check im Splash) oder aus DB
                if (startupResult is not null)
                {
                    offlineMode = !startupResult.IsOnlineAvailable || startupResult.Settings.OfflineMode;
                }
                else
                {
                    IAppSettingsDataService settingsService =
                        scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
                    AppSettings appSettings = await settingsService.GetAsync();
                    offlineMode = appSettings.OfflineMode;
                }

                IReadOnlyList<Series> favoritesRaw = await seriesService.GetFavoritesAsync();

                // Benutzerdefinierte Reihenfolge aus der DashboardPositions-Tabelle laden.
                IDashboardPositionDataService positionService =
                    scope.ServiceProvider.GetRequiredService<IDashboardPositionDataService>();
                IReadOnlyList<DashboardPosition> savedPositions =
                    await positionService.GetBySectionAsync(SectionNewReleases);

                // Position-Lookup: SeriesId → Position (0-basiert)
                Dictionary<Guid, int> positionBySeriesId = new(savedPositions.Count);
                foreach (DashboardPosition dp in savedPositions)
                {
                    positionBySeriesId[dp.SeriesId] = dp.Position;
                }

                List<Series> favoriteSeries = [.. favoritesRaw
                    .OrderBy(s => positionBySeriesId.TryGetValue(s.Id, out int pos) ? pos : int.MaxValue)
                    .ThenBy(s => s.Title)];

                foreach (Series s in favoriteSeries)
                {
                    string posText = positionBySeriesId.TryGetValue(s.Id, out int p)
                        ? p.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : "keine";
                    _logger.Debug($"Favorit: '{s.Title}' – Position={posText}");
                }

                // Onboarding: wenn keine abonnierte Serie vorhanden ist, signalisieren wir das der Seite
                HasSubscribedSeries = subscribedSeries.Count > 0;
                _hasFavoriteSeries = favoriteSeries.Count > 0;
                OnPropertyChanged(nameof(NoFavoritesHintVisibility));

                // PlaybackStates einmal komplett laden – wird für Weiterhören, In-Progress
                // und Zuletzt-gehört benötigt. Vermeidet N+1-Abfragen pro Episode.
                IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync();
                Dictionary<Guid, PlaybackState> stateByEpisodeId = new(allStates.Count);
                foreach (PlaybackState ps in allStates)
                {
                    stateByEpisodeId[ps.EpisodeId] = ps;
                }

                // Weiterhören-Liste aufbauen – benötigt alle Folgen der Favoriten-Serien
                IReadOnlyList<UnheardSeriesCardViewModel> unheardList =
                    await BuildUnheardSeriesAsync(favoriteSeries, episodeService, stateByEpisodeId);
                WeiterhoerenVM.SetItems(unheardList);

                // Favoriten-Kacheln aufbauen (mit Cover und RemoveCommand) und sortieren
                IReadOnlyList<FavoriteSeriesCardViewModel> favoriteCards =
                    await BuildFavoriteCardsAsync(favoriteSeries, positionService);
                FavoritenVM.SetItems(favoriteCards);

                // In-Progress und Recent über den DataLoader bauen
                IReadOnlyList<NewEpisodeCardViewModel> inProgress =
                    await _dataLoader.BuildInProgressEpisodesAsync(episodeService, allStates, subscribedSeries);
                InProgressVM.SetItems(inProgress);

                IReadOnlyList<RecentSeriesCardViewModel> recent =
                    await _dataLoader.BuildRecentSeriesAsync(episodeService, allStates, subscribedSeries);
                ZuletztGehoertVM.SetItems(recent);
            }
            finally
            {
                IsLoading = false;
            }

            // Neuerscheinungen: im Offline-Modus komplett überspringen.
            // Gecachte Daten bleiben in der DB erhalten, werden aber nicht angezeigt.
            if (!offlineMode)
            {
                IReadOnlyList<NewEpisodesGroupViewModel> groups =
                    await _dataLoader.BuildNewReleaseGroupsAsync(subscribedSeries);
                NeuerscheinungenVM.SetGroups(groups);
            }
        }

        /// <summary>
        /// Baut die „Weiterhören"-Liste – Serien mit mindestens einer gehörten und mindestens
        /// einer ungehörten Folge. Nutzt eine Batch-Query für alle Folgen der Favoriten-Serien,
        /// um N+1-Abfragen zu vermeiden.
        /// </summary>
        private async Task<IReadOnlyList<UnheardSeriesCardViewModel>> BuildUnheardSeriesAsync(
            List<Series> favoriteSeries,
            IEpisodeDataService episodeService,
            Dictionary<Guid, PlaybackState> stateByEpisodeId)
        {
            List<Guid> favoriteSeriesIds = [.. favoriteSeries.Select(s => s.Id)];
            IReadOnlyList<Episode> allFavoriteEpisodes =
                await episodeService.GetBySeriesIdsAsync(favoriteSeriesIds);

            Dictionary<Guid, List<Episode>> episodesBySeriesId = new(favoriteSeries.Count);
            foreach (Episode episode in allFavoriteEpisodes)
            {
                if (!episodesBySeriesId.TryGetValue(episode.SeriesId, out List<Episode>? bucket))
                {
                    bucket = [];
                    episodesBySeriesId[episode.SeriesId] = bucket;
                }
                bucket.Add(episode);
            }

            List<UnheardSeriesCardViewModel> unheardList = [];

            foreach (Series series in favoriteSeries)
            {
                if (!episodesBySeriesId.TryGetValue(series.Id, out List<Episode>? episodes))
                {
                    continue;
                }

                int completedCount = 0;
                int totalEpisodeCount = episodes.Count;

                foreach (Episode episode in episodes)
                {
                    if (stateByEpisodeId.TryGetValue(episode.Id, out PlaybackState? state) && state.IsCompleted)
                    {
                        completedCount++;
                    }
                }

                int unheardCount = totalEpisodeCount - completedCount;
                if (completedCount > 0 && unheardCount > 0)
                {
                    BitmapImage? cover = await _dataLoader.BuildSeriesCoverAsync(series);
                    unheardList.Add(new UnheardSeriesCardViewModel(series.Id, series.Title, cover, unheardCount));
                }
            }

            return unheardList;
        }

        /// <summary>
        /// Baut die Favoriten-Kacheln inklusive Cover-Bild und sortiert sie nach der
        /// benutzerdefinierten Reihenfolge aus der DashboardPositions-Tabelle. Serien ohne
        /// gespeicherte Position werden alphabetisch angehängt.
        /// </summary>
        private async Task<IReadOnlyList<FavoriteSeriesCardViewModel>> BuildFavoriteCardsAsync(
            IReadOnlyList<Series> favoriteSeries,
            IDashboardPositionDataService positionService)
        {
            List<FavoriteSeriesCardViewModel> favoriteCards = [];

            foreach (Series series in favoriteSeries)
            {
                BitmapImage? cover = await _dataLoader.BuildSeriesCoverAsync(series);
                favoriteCards.Add(new FavoriteSeriesCardViewModel(
                    series.Id, series.Title, cover, _scopeFactory, _confirmationDialogService, _localizationService));
            }

            // Benutzerdefinierte Reihenfolge für Favoriten-Kacheln laden.
            // Die Favoriten-Positionen sind unabhängig von den Neuerscheinungen-Positionen,
            // weil der Nutzer beide Abschnitte getrennt per Drag & Drop sortieren kann.
            IReadOnlyList<DashboardPosition> favoritePositions =
                await positionService.GetBySectionAsync("Favoriten");

            Dictionary<Guid, int> favoritePositionBySeriesId = new(favoritePositions.Count);
            foreach (DashboardPosition dp in favoritePositions)
            {
                favoritePositionBySeriesId[dp.SeriesId] = dp.Position;
            }

            favoriteCards.Sort((a, b) =>
            {
                bool aHasPos = favoritePositionBySeriesId.TryGetValue(a.SeriesId, out int posA);
                bool bHasPos = favoritePositionBySeriesId.TryGetValue(b.SeriesId, out int posB);

                if (aHasPos && bHasPos)
                {
                    return posA.CompareTo(posB);
                }

                if (aHasPos)
                {
                    return -1;
                }

                if (bHasPos)
                {
                    return 1;
                }

                return string.Compare(a.SeriesName, b.SeriesName, StringComparison.Ordinal);
            });

            return favoriteCards;
        }

        // ── Event-Weiterleitung ─────────────────────────────────────────────────

        /// <summary>
        /// Leitet PropertyChanged-Events der Sub-VMs an die eigenen Pass-Through-Properties weiter.
        /// Alle relevanten Property-Namen sind zwischen Top-VM und Sub-VMs bewusst identisch gehalten.
        /// </summary>
        private void OnSubVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(e.PropertyName);
        }

        /// <summary>
        /// Aktualisiert den <c>_hasFavoriteSeries</c>-Flag und den NoFavoritesHint,
        /// wenn der Nutzer Favoriten entfernt oder umsortiert.
        /// </summary>
        private void OnFavoritesChanged()
        {
            _hasFavoriteSeries = FavoritenVM.FavoriteSeries.Count > 0;
            OnPropertyChanged(nameof(NoFavoritesHintVisibility));
        }

        /// <summary>
        /// Löst alle Event-Subscriptions und gibt das Favoriten-Sub-VM frei.
        /// </summary>
        public void Dispose()
        {
            NeuerscheinungenVM.PropertyChanged -= OnSubVmPropertyChanged;
            FavoritenVM.PropertyChanged -= OnSubVmPropertyChanged;
            WeiterhoerenVM.PropertyChanged -= OnSubVmPropertyChanged;
            InProgressVM.PropertyChanged -= OnSubVmPropertyChanged;
            ZuletztGehoertVM.PropertyChanged -= OnSubVmPropertyChanged;

            FavoritenVM.FavoritesChanged -= OnFavoritesChanged;
            FavoritenVM.Dispose();
        }
    }
}

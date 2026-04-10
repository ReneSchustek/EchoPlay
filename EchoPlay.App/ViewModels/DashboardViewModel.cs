using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die Startseite (Dashboard).
    /// Zeigt Neuerscheinungen, Favoriten, laufende Episoden und angekündigte Episoden.
    /// </summary>
    public sealed class DashboardViewModel : ObservableObject
    {
        // Maximalanzahl der angezeigten Episoden pro Serie – mehr würde die Kachelliste unübersichtlich machen
        private const int MaxEpisodesPerSeries = 5;

        // Dashboard-Sektionsnamen für Positionsspeicherung (Drag & Drop Reihenfolge)
        private const string SectionNewReleases = "Neuerscheinungen";
        private const string SectionFavorites = "Favoriten";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IErrorDialogService _errorDialogService;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly IPlayerService _playerService;
        private readonly CoverService? _coverService;
        private readonly ILocalizationService? _localizationService;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger;

        // Maximalanzahl der zuletzt gehörten Serien im Dashboard-Abschnitt
        private const int MaxRecentSeries = 8;

        // Maximalanzahl der laufenden Episoden im In-Progress-Abschnitt
        private const int MaxInProgressEpisodes = 10;

        private ObservableCollection<NewEpisodesGroupViewModel> _newEpisodeGroups = [];
        private ObservableCollection<FavoriteSeriesCardViewModel> _favoriteSeries = [];
        private IReadOnlyList<NewEpisodeCardViewModel> _inProgressEpisodes = [];
        private IReadOnlyList<RecentSeriesCardViewModel> _recentSeries = [];
        private IReadOnlyList<UnheardSeriesCardViewModel> _unheardSeries = [];
        private bool _isLoading;
        private bool _isLoadingNewReleases;
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
        public DashboardViewModel(
            IServiceScopeFactory scopeFactory,
            IErrorDialogService errorDialogService,
            IConfirmationDialogService confirmationDialogService,
            IPlayerService playerService,
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory,
            CoverService? coverService = null,
            ILocalizationService? localizationService = null)
        {
            _scopeFactory               = scopeFactory;
            _errorDialogService         = errorDialogService;
            _confirmationDialogService  = confirmationDialogService;
            _playerService              = playerService;
            _coverService               = coverService;
            _localizationService        = localizationService;
            _logger                     = loggerFactory.CreateLogger("DashboardViewModel");
        }

        /// <summary>
        /// Neue, ungehörte Episoden favorisierter Serien, gruppiert nach Serie.
        /// Pro Serie eine eigene Kachelreihe mit Serienname als Überschrift.
        /// </summary>
        /// <summary>
        /// ObservableCollection, damit das ListView mit CanReorderItems die Sammlung
        /// direkt per Drag &amp; Drop umsortieren kann.
        /// </summary>
        public ObservableCollection<NewEpisodesGroupViewModel> NewEpisodeGroups
        {
            get => _newEpisodeGroups;
            private set
            {
                if (SetProperty(ref _newEpisodeGroups, value))
                {
                    OnPropertyChanged(nameof(NewEpisodeGroupsVisibility));
                    OnPropertyChanged(nameof(NewReleasesSectionVisibility));
                    OnPropertyChanged(nameof(NewReleasesLoadingVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Neuerscheinungs-Abschnitts.
        /// Wird <see cref="Visibility.Visible"/> wenn mindestens eine Gruppe mit Episoden vorhanden ist.
        /// </summary>
        public Visibility NewEpisodeGroupsVisibility =>
            _newEpisodeGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;


        /// <summary>
        /// Favorisierte Serien als Kachelreihe.
        /// ObservableCollection, damit das ListView mit CanReorderItems die Sammlung
        /// direkt per Drag &amp; Drop umsortieren kann.
        /// </summary>
        public ObservableCollection<FavoriteSeriesCardViewModel> FavoriteSeries
        {
            get => _favoriteSeries;
            private set
            {
                // Alten Handler abmelden, damit kein Listener auf einer verwaisten Collection hängt
                _favoriteSeries.CollectionChanged -= OnFavoriteSeriesReordered;

                if (SetProperty(ref _favoriteSeries, value))
                {
                    OnPropertyChanged(nameof(FavoriteSectionVisibility));

                    // CollectionChanged feuert zuverlässig wenn ListView per Drag & Drop
                    // Items in der Collection verschiebt (Remove + Insert).
                    value.CollectionChanged += OnFavoriteSeriesReordered;
                }
            }
        }

        /// <summary>
        /// Episoden, die aktuell gehört werden – Wiedergabestand > 0, noch nicht abgeschlossen.
        /// Sortiert nach dem Zeitpunkt der letzten Wiedergabe, neueste zuerst.
        /// </summary>
        public IReadOnlyList<NewEpisodeCardViewModel> InProgressEpisodes
        {
            get => _inProgressEpisodes;
            private set
            {
                if (SetProperty(ref _inProgressEpisodes, value))
                {
                    OnPropertyChanged(nameof(InProgressSectionVisibility));
                }
            }
        }

        /// <summary>
        /// Zuletzt gehörte Serien – sortiert nach Zeitpunkt der letzten Wiedergabeänderung, neueste zuerst.
        /// Wird als horizontale Kachelreihe unterhalb der anderen Abschnitte angezeigt.
        /// </summary>
        public IReadOnlyList<RecentSeriesCardViewModel> RecentSeries
        {
            get => _recentSeries;
            private set
            {
                if (SetProperty(ref _recentSeries, value))
                {
                    OnPropertyChanged(nameof(RecentSectionVisibility));
                }
            }
        }


        /// <summary>
        /// Angefangene Serien mit noch ungehörten Folgen.
        /// Der Nutzer hat mindestens eine Folge gehört, aber noch nicht alle.
        /// Klick auf eine Kachel navigiert zur Seriendetailseite.
        /// </summary>
        public IReadOnlyList<UnheardSeriesCardViewModel> UnheardSeries
        {
            get => _unheardSeries;
            private set
            {
                if (SetProperty(ref _unheardSeries, value))
                {
                    OnPropertyChanged(nameof(UnheardSectionVisibility));
                }
            }
        }

        /// <summary>
        /// Steuert die Sichtbarkeit des „Weiterhören"-Abschnitts.
        /// Wird <see cref="Visibility.Visible"/>, sobald mindestens eine angefangene Serie geladen wurde.
        /// </summary>
        public Visibility UnheardSectionVisibility =>
            _unheardSeries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;


        /// <summary>
        /// Steuert die Sichtbarkeit des „Favoriten"-Abschnitts.
        /// Wird <see cref="Visibility.Visible"/>, sobald mindestens eine favorisierte Serie geladen wurde.
        /// </summary>
        public Visibility FavoriteSectionVisibility =>
            _favoriteSeries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Steuert die Sichtbarkeit des „In Progress"-Abschnitts.
        /// Wird <see cref="Visibility.Visible"/>, sobald mindestens eine laufende Episode geladen wurde.
        /// </summary>
        public Visibility InProgressSectionVisibility =>
            _inProgressEpisodes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Steuert die Sichtbarkeit des „Zuletzt gehört"-Abschnitts.
        /// Wird <see cref="Visibility.Visible"/>, sobald mindestens ein Eintrag geladen wurde.
        /// </summary>
        public Visibility RecentSectionVisibility =>
            _recentSeries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Hinweis, wenn abonnierte Serien vorhanden sind, aber noch keine favorisiert wurden.
        /// Steuert die Sichtbarkeit des Hinweistexts im Neuerscheinungen-Abschnitt.
        /// </summary>
        public Visibility NoFavoritesHintVisibility =>
            _hasSubscribedSeries && !_hasFavoriteSeries ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Gibt an, ob mindestens eine abonnierte Serie vorhanden ist.
        /// Wird auf <see langword="false"/> gesetzt, wenn die Datenbank keine abonnierten Serien enthält –
        /// dann navigiert die Startseite automatisch zur Suche (Onboarding).
        /// </summary>
        public bool HasSubscribedSeries
        {
            get => _hasSubscribedSeries;
            private set => SetProperty(ref _hasSubscribedSeries, value);
        }

        /// <summary>
        /// Gibt an, ob gerade ein Ladevorgang läuft.
        /// Steuert den ProgressRing auf der Startseite.
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Gibt an, ob gerade Neuerscheinungen aus der iTunes API geladen werden.
        /// Steuert den Lade-Hinweis im Neuerscheinungen-Abschnitt, damit der Nutzer
        /// weiß, dass die Daten im Hintergrund abgerufen werden (dauert bei vielen Serien mehrere Minuten).
        /// </summary>
        public bool IsLoadingNewReleases
        {
            get => _isLoadingNewReleases;
            private set => SetProperty(ref _isLoadingNewReleases, value);
        }

        /// <summary>
        /// Sichtbarkeit des Lade-Hinweises für Neuerscheinungen.
        /// Wird <see cref="Visibility.Visible"/> solange die iTunes-API-Abfrage im Hintergrund läuft
        /// und noch keine Neuerscheinungen angezeigt werden.
        /// </summary>
        public Visibility NewReleasesLoadingVisibility =>
            _isLoadingNewReleases && _newEpisodeGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des gesamten Neuerscheinungen-Abschnitts (Überschrift + Inhalt).
        /// Sichtbar wenn Daten vorhanden sind ODER gerade geladen wird.
        /// </summary>
        public Visibility NewReleasesSectionVisibility =>
            _isLoadingNewReleases || _newEpisodeGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Lädt alle Abschnitte der Startseite:
        /// Neuerscheinungen (aus Favoriten), Favoriten-Kacheln, In-Progress-Episoden,
        /// Ankündigungen und zuletzt gehörte Serien.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task LoadAsync()
        {
            IsLoading = true;

            // StartupResult wurde im Splash vorgeladen – enthält den Offline-Status.
            // Serien und Cache werden immer frisch aus der DB geladen, da sich IsWatched
            // und andere Daten während der Session ändern können.
            Services.StartupResult? startupResult = null;
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
                ISeriesDataService seriesService       = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                IEpisodeDataService episodeService     = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
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
                // Serien mit Position kommen zuerst (aufsteigend), Rest alphabetisch.
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

                List<Series> favoriteSeries = favoritesRaw
                    .OrderBy(s => positionBySeriesId.TryGetValue(s.Id, out int pos) ? pos : int.MaxValue)
                    .ThenBy(s => s.Title)
                    .ToList();

                foreach (Series s in favoriteSeries)
                {
                    string posText = positionBySeriesId.TryGetValue(s.Id, out int p)
                        ? p.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : "keine";
                    _logger.Debug($"Favorit: '{s.Title}' – Position={posText}");
                }

                // Onboarding: wenn keine abonnierte Serie vorhanden ist, signalisieren wir das der Seite
                HasSubscribedSeries = subscribedSeries.Count > 0;
                _hasFavoriteSeries  = favoriteSeries.Count > 0;
                OnPropertyChanged(nameof(NoFavoritesHintVisibility));

                // PlaybackStates einmal komplett laden – wird für Weiterhören, In-Progress
                // und Zuletzt-gehört benötigt. Vermeidet N+1-Abfragen pro Episode.
                IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync();
                Dictionary<Guid, PlaybackState> stateByEpisodeId = new(allStates.Count);
                foreach (PlaybackState ps in allStates)
                {
                    stateByEpisodeId[ps.EpisodeId] = ps;
                }

                // Weiterhören: Serien mit mindestens 1 gehörten + ungehörten Folgen.
                // Alle Episoden der Favoriten-Serien in einem Batch-Query laden, statt
                // pro Serie ein separater GetBySeriesIdAsync-Aufruf (N+1).
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
                        if (stateByEpisodeId.TryGetValue(episode.Id, out PlaybackState? state)
                            && state.IsCompleted)
                        {
                            completedCount++;
                        }
                    }

                    int unheardCount = totalEpisodeCount - completedCount;
                    if (completedCount > 0 && unheardCount > 0)
                    {
                        BitmapImage? cover = await BuildCoverImageAsync(series);
                        unheardList.Add(new UnheardSeriesCardViewModel(series.Id, series.Title, cover, unheardCount));
                    }
                }

                UnheardSeries = unheardList;

                // Favoriten-Kacheln: alle favorisierten Serien, für schnellen Zugriff auf die Detailseite.
                // Jede Kachel bekommt ein Kontextmenü zum Entfernen aus Favoriten.
                List<FavoriteSeriesCardViewModel> favoriteCards = [];

                foreach (Series series in favoriteSeries)
                {
                    BitmapImage? cover = await BuildCoverImageAsync(series);
                    FavoriteSeriesCardViewModel card = new(
                        series.Id, series.Title, cover, _scopeFactory, _confirmationDialogService, _localizationService);
                    card.RemovedFromFavorites += OnSeriesRemovedFromFavorites;
                    favoriteCards.Add(card);
                }

                // Benutzerdefinierte Reihenfolge für Favoriten-Kacheln laden.
                // Die Favoriten-Positionen sind unabhängig von den Neuerscheinungen-Positionen,
                // weil der Nutzer beide Abschnitte getrennt per Drag & Drop sortieren kann.
                IReadOnlyList<DashboardPosition> favoritePositions =
                    await positionService.GetBySectionAsync(SectionFavorites);

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

                FavoriteSeries = new ObservableCollection<FavoriteSeriesCardViewModel>(favoriteCards);

                // In-Progress: Episoden mit gestarteter aber unvollständiger Wiedergabe
                InProgressEpisodes = await BuildInProgressEpisodesAsync(episodeService, allStates, subscribedSeries);

                // Zuletzt gehört: alle Serien nach Wiedergabe-Zeitpunkt
                RecentSeries = await BuildRecentSeriesAsync(episodeService, allStates, subscribedSeries);
            }
            finally
            {
                IsLoading = false;
            }

            // Neuerscheinungen: im Offline-Modus komplett überspringen.
            // Gecachte Daten bleiben in der DB erhalten, werden aber nicht angezeigt.
            // Cache wird immer frisch aus der DB geladen, da sich IsWatched während der Session ändern kann.
            if (!offlineMode)
            {
                await BuildNewReleaseTilesFromCacheAsync(subscribedSeries);
            }
        }

        /// <summary>
        /// Erstellt eine neue Episodenkachel aus den übergebenen Rohdaten.
        /// </summary>
        /// <param name="series">Die zugehörige Serie.</param>
        /// <param name="episode">Die darzustellende Episode.</param>
        /// <param name="state">Der gespeicherte Wiedergabestatus, oder null.</param>
        /// <param name="hasLocalTrack">Gibt an, ob lokale Tracks vorhanden sind.</param>
        /// <param name="isAnnounced">Gibt an, ob die Episode noch nicht verfügbar ist.</param>
        /// <returns>Das aufbereitete Kachel-ViewModel.</returns>
        private async Task<NewEpisodeCardViewModel> BuildCardAsync(
            Series series,
            Episode episode,
            PlaybackState? state,
            bool hasLocalTrack,
            bool isAnnounced)
        {
            PlaybackStatus status = DetermineStatus(state);
            double progress       = CalculateProgress(state, episode.Duration);

            // Episoden-Cover bevorzugen, Serien-Cover als Fallback
            BitmapImage? cover = await BuildEpisodeCoverAsync(episode)
                                 ?? await BuildCoverImageAsync(series);

            return new NewEpisodeCardViewModel(
                episodeId:                   episode.Id,
                seriesId:                    series.Id,
                seriesName:                  series.Title,
                episodeTitle:                episode.Title,
                coverImage:                  cover,
                status:                      status,
                progressPercent:             progress,
                hasLocalTrack:               hasLocalTrack,
                isAnnounced:                 isAnnounced,
                scopeFactory:                _scopeFactory,
                errorDialogService:          _errorDialogService,
                confirmationDialogService:   _confirmationDialogService,
                playerService:               _playerService,
                episodeNumber:               episode.EpisodeNumber,
                releaseDate:                 episode.ReleaseDate,
                localizationService:         _localizationService);
        }

        /// <summary>
        /// Erstellt ein Cover-Bild für eine Episode.
        /// Priorität: DB-Cover → cover.jpg im Ordner → ID3-Tag des ersten Tracks → null.
        /// Nutzt denselben CoverLoader wie die lokale Mediathek.
        /// </summary>
        private async Task<BitmapImage?> BuildEpisodeCoverAsync(Episode episode)
        {
            // DB-Cover über CoverService laden (CoverImages-Tabelle)
            if (_coverService is not null)
            {
                BitmapImage? dbCover = await _coverService.GetEpisodeCoverImageAsync(episode.Id);
                if (dbCover is not null)
                {
                    return dbCover;
                }
            }

            // Dateisystem-Cover über den CoverLoader (cover.jpg oder ID3-Tag)
            if (episode.LocalFolderPath is not null)
            {
                try
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    EchoPlay.LocalLibrary.Cover.ILocalCoverLoader coverLoader =
                        scope.ServiceProvider.GetRequiredService<EchoPlay.LocalLibrary.Cover.ILocalCoverLoader>();

                    // Ersten Track für ID3-Fallback ermitteln
                    string? firstTrackPath = null;
                    if (!System.IO.File.Exists(System.IO.Path.Combine(episode.LocalFolderPath, Core.CoverConstants.CoverFileName)))
                    {
                        Data.Services.Interfaces.ILocalTrackDataService trackService =
                            scope.ServiceProvider.GetRequiredService<Data.Services.Interfaces.ILocalTrackDataService>();
                        IReadOnlyList<Data.Entities.Library.LocalTrack> tracks =
                            await trackService.GetByEpisodeIdAsync(episode.Id);
                        firstTrackPath = tracks.OrderBy(t => t.TrackNumber).FirstOrDefault()?.FilePath;
                    }

                    byte[]? coverBytes = await coverLoader.LoadAsync(episode.LocalFolderPath, firstTrackPath);
                    if (coverBytes is not null)
                    {
                        return await CoverService.ConvertToBitmapAsync(coverBytes);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Cover-Laden fehlgeschlagen: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// Leitet den <see cref="PlaybackStatus"/> aus dem gespeicherten Zustand ab.
        /// </summary>
        private static PlaybackStatus DetermineStatus(PlaybackState? state)
        {
            if (state is null || state.LastPosition == TimeSpan.Zero)
            {
                return PlaybackStatus.NotStarted;
            }

            return state.IsCompleted ? PlaybackStatus.Finished : PlaybackStatus.InProgress;
        }

        /// <summary>
        /// Berechnet den Wiedergabefortschritt in Prozent (0–100).
        /// Gibt 0 zurück, wenn kein Zustand vorhanden ist oder die Gesamtdauer unbekannt ist.
        /// </summary>
        private static double CalculateProgress(PlaybackState? state, TimeSpan duration)
        {
            if (state is null || duration == TimeSpan.Zero)
            {
                return 0;
            }

            if (state.IsCompleted)
            {
                return 100;
            }

            return Math.Min(100, state.LastPosition.TotalSeconds / duration.TotalSeconds * 100);
        }

        /// <summary>
        /// Erstellt ein <see cref="BitmapImage"/> aus den Seriendaten.
        /// Priorität: DB-Cover (CoverService) → cover.jpg im Serienordner → URL-Cover → null.
        /// Muss auf dem UI-Thread aufgerufen werden.
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

            // Dateisystem-Fallback: cover.jpg im Serienordner, angelegt vom lokalen Scanner.
            // Ohne diesen Fallback bleiben Favoriten- und Weiterhören-Kacheln ohne Cover,
            // weil diese Sektionen keine Episode als Kontext haben.
            if (series.LocalFolderPath is not null)
            {
                string coverPath = System.IO.Path.Combine(series.LocalFolderPath, Core.CoverConstants.CoverFileName);
                if (System.IO.File.Exists(coverPath))
                {
                    try
                    {
                        byte[] coverBytes = await System.IO.File.ReadAllBytesAsync(coverPath);
                        return await CoverService.ConvertToBitmapAsync(coverBytes);
                    }
                    catch (Exception)
                    {
                        // Datei-Zugriffsfehler (gesperrt, gelöscht) – Serien-Cover bleibt leer
                    }
                }
            }

            // URL-Cover als letzter Fallback (z.B. von Spotify oder Apple Music)
            if (series.CoverImageUrl is not null)
            {
                return new BitmapImage(new Uri(series.CoverImageUrl));
            }

            return null;
        }

        /// <summary>
        /// Ermittelt aktuell laufende Episoden anhand der Wiedergabestände.
        /// „In Progress" bedeutet: Wiedergabestand > 0 und noch nicht abgeschlossen.
        /// Sortiert nach dem aktuellsten Wiedergabezeitpunkt, beschränkt auf <see cref="MaxInProgressEpisodes"/>.
        /// </summary>
        /// <param name="episodeService">Für den Lookup von Episode und Seriendetails.</param>
        /// <param name="allStates">Alle geladenen Wiedergabestände.</param>
        /// <param name="subscribedSeries">Bereits geladene abonnierte Serien (für Seriendetails).</param>
        /// <returns>Liste der In-Progress-Episoden, neueste zuerst.</returns>
        private async Task<IReadOnlyList<NewEpisodeCardViewModel>> BuildInProgressEpisodesAsync(
            IEpisodeDataService episodeService,
            IReadOnlyList<PlaybackState> allStates,
            IReadOnlyList<Series> subscribedSeries)
        {
            // Nur Stände mit tatsächlichem Fortschritt, die noch nicht abgeschlossen sind
            List<PlaybackState> activeStates = [];

            foreach (PlaybackState state in allStates)
            {
                if (state.LastPosition > TimeSpan.Zero && !state.IsCompleted)
                {
                    activeStates.Add(state);
                }
            }

            // Neueste zuerst
            activeStates.Sort((a, b) =>
            {
                DateTime timeA = a.UpdatedAt ?? a.CreatedAt;
                DateTime timeB = b.UpdatedAt ?? b.CreatedAt;
                return timeB.CompareTo(timeA);
            });

            List<NewEpisodeCardViewModel> result = [];

            foreach (PlaybackState state in activeStates)
            {
                if (result.Count >= MaxInProgressEpisodes)
                {
                    break;
                }

                Episode? episode = await episodeService.GetByIdAsync(state.EpisodeId);

                if (episode is null)
                {
                    continue;
                }

                Series? series = FindSeriesById(subscribedSeries, episode.SeriesId);

                if (series is null)
                {
                    continue;
                }

                result.Add(await BuildCardAsync(series, episode, state, episode.LocalTrackCount is > 0, false));
            }

            return result;
        }

        /// <summary>
        /// Ermittelt die zuletzt gehörten Serien anhand der Wiedergabestände.
        /// Sortiert nach dem aktuellsten Änderungszeitpunkt, dedupliziert nach Serie und beschränkt auf <see cref="MaxRecentSeries"/>.
        /// </summary>
        /// <param name="episodeService">Für den Lookup von Episode → SeriesId.</param>
        /// <param name="allStates">Alle geladenen Wiedergabestände.</param>
        /// <param name="subscribedSeries">Bereits geladene abonnierte Serien (für Seriendetails).</param>
        /// <returns>Liste der zuletzt gehörten Serien, neueste zuerst.</returns>
        private async Task<IReadOnlyList<RecentSeriesCardViewModel>> BuildRecentSeriesAsync(
            IEpisodeDataService episodeService,
            IReadOnlyList<PlaybackState> allStates,
            IReadOnlyList<Series> subscribedSeries)
        {
            // Stände mit tatsächlicher Hörzeit ODER als gehört markiert (Online-Folgen via Browser)
            List<PlaybackState> activeStates = [];

            foreach (PlaybackState state in allStates)
            {
                if (state.LastPosition > TimeSpan.Zero || state.IsCompleted)
                {
                    activeStates.Add(state);
                }
            }

            // Neueste zuerst – LastPlayedAt bevorzugt (Online-Folgen setzen nur dieses Feld),
            // dann UpdatedAt, CreatedAt als letzter Fallback
            activeStates.Sort((a, b) =>
            {
                DateTime timeA = a.LastPlayedAt ?? a.UpdatedAt ?? a.CreatedAt;
                DateTime timeB = b.LastPlayedAt ?? b.UpdatedAt ?? b.CreatedAt;
                return timeB.CompareTo(timeA);
            });

            // Für jede Serie nur den neuesten Eintrag behalten
            HashSet<Guid> seenSeriesIds = [];
            List<RecentSeriesCardViewModel> result = [];

            foreach (PlaybackState state in activeStates)
            {
                if (result.Count >= MaxRecentSeries)
                {
                    break;
                }

                // Episode laden um die SeriesId zu ermitteln
                Episode? episode = await episodeService.GetByIdAsync(state.EpisodeId);

                if (episode is null)
                {
                    continue;
                }

                if (!seenSeriesIds.Add(episode.SeriesId))
                {
                    // Diese Serie haben wir schon – nur den neuesten Eintrag anzeigen
                    continue;
                }

                // Seriendetails aus der bereits geladenen Liste holen
                Series? series = FindSeriesById(subscribedSeries, episode.SeriesId);

                if (series is null)
                {
                    continue;
                }

                // Episoden-Cover der zuletzt gehörten Folge bevorzugen, Serien-Cover als Fallback
                BitmapImage? cover = await BuildEpisodeCoverAsync(episode)
                                     ?? await BuildCoverImageAsync(series);

                result.Add(new RecentSeriesCardViewModel(
                    seriesId:         series.Id,
                    seriesName:       series.Title,
                    lastEpisodeTitle: episode.Title,
                    coverImage:       cover));
            }

            return result;
        }

        /// <summary>
        /// Sucht eine Serie in einer Liste anhand ihrer ID.
        /// </summary>
        private static Series? FindSeriesById(IReadOnlyList<Series> series, Guid id)
        {
            foreach (Series s in series)
            {
                if (s.Id == id)
                {
                    return s;
                }
            }

            return null;
        }

        /// <summary>
        /// Reagiert auf das Entfernen einer Serie aus den Favoriten.
        /// Entfernt die Kachel direkt aus der ObservableCollection, damit die UI sofort reagiert.
        /// Die Sichtbarkeit wird manuell aktualisiert, weil Remove() nicht den Property-Setter durchläuft.
        /// </summary>
        /// <param name="seriesId">Die ID der entfernten Serie.</param>
        private void OnSeriesRemovedFromFavorites(Guid seriesId)
        {
            FavoriteSeriesCardViewModel? cardToRemove = null;

            foreach (FavoriteSeriesCardViewModel card in _favoriteSeries)
            {
                if (card.SeriesId == seriesId)
                {
                    cardToRemove = card;
                    break;
                }
            }

            if (cardToRemove is not null)
            {
                _favoriteSeries.Remove(cardToRemove);
                // Sichtbarkeit manuell aktualisieren – bei direkter Collection-Manipulation
                // wird der Property-Setter nicht aufgerufen
                OnPropertyChanged(nameof(FavoriteSectionVisibility));
            }
        }

        /// <summary>
        /// Reagiert auf Umsortierungen der Favoriten-Collection durch das ListView.
        /// ObservableCollection zwei Events (Remove + Add). Gespeichert wird nur beim Add.
        /// </summary>
        private void OnFavoriteSeriesReordered(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                _logger.Info("Favoriten umsortiert – speichere neue Reihenfolge.");
                _ = SaveFavoriteSeriesOrderAsync();
            }
        }

        /// <summary>
        /// Speichert die aktuelle Reihenfolge der Favoriten-Kacheln
        /// über den <see cref="IDashboardPositionDataService"/> in der Datenbank.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task SaveFavoriteSeriesOrderAsync()
        {
            try
            {
                _logger.Info($"Speichere Favoriten-Reihenfolge ({_favoriteSeries.Count} Serien).");

                List<Guid> seriesIds = new(_favoriteSeries.Count);
                foreach (FavoriteSeriesCardViewModel card in _favoriteSeries)
                {
                    seriesIds.Add(card.SeriesId);
                    _logger.Debug($"'{card.SeriesName}' → Position {seriesIds.Count - 1}");
                }

                using IServiceScope scope = _scopeFactory.CreateScope();
                IDashboardPositionDataService positionService =
                    scope.ServiceProvider.GetRequiredService<IDashboardPositionDataService>();

                await positionService.SaveOrderAsync(SectionFavorites, seriesIds);

                _logger.Info("Favoriten-Reihenfolge gespeichert.");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Favoriten-Reihenfolge konnte nicht gespeichert werden: {ex.Message}");
            }
        }

        /// <summary>
        /// Baut die Neuerscheinungs-Kacheln aus den gecachten DB-Einträgen auf.
        /// Nutzt vorgeladene Cache-Daten aus dem Startup oder lädt sie aus der DB.
        /// </summary>
        /// <param name="subscribedSeries">Alle abonnierten Serien (für Cover und lokale Matches).</param>
        /// <param name="preloadedCache">Vorgeladene Cache-Daten aus dem Startup (optional). Wenn null, wird aus der DB geladen.</param>
        private async Task BuildNewReleaseTilesFromCacheAsync(
            IReadOnlyList<Series> subscribedSeries,
            IReadOnlyList<CachedNewRelease>? preloadedCache = null)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IPlaybackStateDataService stateService =
                    scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();
                IEpisodeDataService episodeService =
                    scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();

                IReadOnlyList<CachedNewRelease> cached = preloadedCache
                    ?? await scope.ServiceProvider
                        .GetRequiredService<ICachedNewReleaseDataService>().GetAllAsync();

                if (cached.Count == 0)
                {
                    return;
                }

                await BuildTilesFromEntries(cached, subscribedSeries, episodeService,
                    stateService);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Neuerscheinungen aus Cache konnten nicht geladen werden: {ex.Message}");
            }
        }

        /// <summary>
        /// Wandelt gecachte Neuerscheinungen in Kachel-ViewModels um und gruppiert sie nach Monat.
        /// "Angekündigt" (Zukunft) bildet eine eigene Gruppe ganz oben,
        /// danach folgen die Monate absteigend (neuester zuerst).
        /// Gehörte Folgen werden herausgefiltert.
        /// </summary>
        private async Task BuildTilesFromEntries(
            IReadOnlyList<CachedNewRelease> cached,
            IReadOnlyList<Series> subscribedSeries,
            IEpisodeDataService episodeService,
            IPlaybackStateDataService stateService)
        {
            DateTime today = DateTime.UtcNow.Date;

            // Monatsnamen aus der aktuellen Kultur – funktioniert für DE und EN
            string[] monthNames = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.MonthNames;

            // Episoden und PlaybackStates einmal pro Serie laden (vermeidet N+1-Abfragen).
            // Dictionary SeriesId → Episodenliste: gleiche Serie wird nicht mehrfach abgefragt.
            Dictionary<Guid, IReadOnlyList<Episode>> episodesBySeries = [];
            IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync();
            Dictionary<Guid, PlaybackState> stateByEpisodeId = new(allStates.Count);
            foreach (PlaybackState ps in allStates)
            {
                stateByEpisodeId[ps.EpisodeId] = ps;
            }

            // Kacheln erzeugen und gleichzeitig das ReleaseDate merken (für die Monatsgruppierung).
            // Tupel: (Kachel, ReleaseDate) – gehörte Folgen werden übersprungen.
            List<(NewEpisodeCardViewModel Card, DateTime ReleaseDate)> cardEntries = [];

            foreach (CachedNewRelease entry in cached)
            {
                Series? series = FindSeriesById(subscribedSeries, entry.SeriesId);

                // Nur überwachte Serien anzeigen – nicht-überwachte werden ignoriert
                if (series is null || !series.IsWatched)
                {
                    continue;
                }

                // Lokales Matching: Folgennummer → lokale Episode → Wiedergabestatus
                bool hasLocalTrack = false;
                bool isCompleted = false;
                Guid episodeId = Guid.Empty;

                if (entry.EpisodeNumber.HasValue)
                {
                    // Episodenliste pro Serie nur einmal laden
                    if (!episodesBySeries.TryGetValue(series.Id, out IReadOnlyList<Episode>? localEpisodes))
                    {
                        localEpisodes = await episodeService.GetBySeriesIdAsync(series.Id);
                        episodesBySeries[series.Id] = localEpisodes;
                    }

                    foreach (Episode ep in localEpisodes)
                    {
                        if (ep.EpisodeNumber == entry.EpisodeNumber.Value)
                        {
                            episodeId = ep.Id;
                            hasLocalTrack = ep.LocalTrackCount is > 0;

                            // In-Memory-Lookup statt DB-Abfrage
                            isCompleted = stateByEpisodeId.TryGetValue(ep.Id, out PlaybackState? state)
                                && state.IsCompleted;
                            break;
                        }
                    }
                }

                if (isCompleted)
                {
                    continue;
                }

                bool isAnnounced = entry.ReleaseDate.Date > today;

                // Cover-Priorität: lokales Episoden-Cover → iTunes-Album-Cover → Serien-Cover.
                // iTunes liefert pro Album eine Cover-URL (100×100), die über URL-Pattern
                // auf höhere Auflösung skaliert werden kann (100x100bb → 600x600bb).
                BitmapImage? cover = null;
                if (episodeId != Guid.Empty)
                {
                    Episode? localEp = await episodeService.GetByIdAsync(episodeId);
                    if (localEp is not null)
                    {
                        cover = await BuildEpisodeCoverAsync(localEp);
                    }
                }

                // iTunes-Cover als Fallback: höhere Auflösung per URL-Pattern
                if (cover is null && !string.IsNullOrEmpty(entry.CoverUrl))
                {
                    string highResCoverUrl = entry.CoverUrl.Replace("100x100bb", "600x600bb");
                    cover = new BitmapImage(new Uri(highResCoverUrl));
                }

                cover ??= await BuildCoverImageAsync(series);

                NewEpisodeCardViewModel card = new(
                    episodeId:                   episodeId != Guid.Empty ? episodeId : Guid.NewGuid(),
                    seriesId:                    series.Id,
                    seriesName:                  series.Title,
                    episodeTitle:                entry.Title,
                    coverImage:                  cover,
                    status:                      PlaybackStatus.NotStarted,
                    progressPercent:             0,
                    hasLocalTrack:               hasLocalTrack,
                    isAnnounced:                 isAnnounced,
                    scopeFactory:                _scopeFactory,
                    errorDialogService:          _errorDialogService,
                    confirmationDialogService:   _confirmationDialogService,
                    playerService:               _playerService,
                    episodeNumber:               entry.EpisodeNumber,
                    releaseDate:                 entry.ReleaseDate,
                    localizationService:         _localizationService);

                cardEntries.Add((card, entry.ReleaseDate));
            }

            // Monatliche Gruppierung aufbauen:
            // 1. "Angekündigt" (Datum in der Zukunft) – eigene Gruppe ganz oben
            // 2. Monate absteigend (neuester zuerst)
            List<NewEpisodesGroupViewModel> groups = [];

            List<NewEpisodeCardViewModel> announcedCards = cardEntries
                .Where(e => e.Card.IsAnnounced)
                .Select(e => e.Card)
                .ToList();

            if (announcedCards.Count > 0)
            {
                groups.Add(new NewEpisodesGroupViewModel("Angekündigt", 0, announcedCards));
            }

            // Nicht-angekündigte Kacheln nach Jahr+Monat gruppieren
            List<IGrouping<(int Year, int Month), (NewEpisodeCardViewModel Card, DateTime ReleaseDate)>> monthGroups =
                cardEntries
                    .Where(e => !e.Card.IsAnnounced)
                    .GroupBy(e => (e.ReleaseDate.Year, e.ReleaseDate.Month))
                    .OrderByDescending(g => g.Key.Year)
                    .ThenByDescending(g => g.Key.Month)
                    .ToList();

            foreach (IGrouping<(int Year, int Month), (NewEpisodeCardViewModel Card, DateTime ReleaseDate)> monthGroup in monthGroups)
            {
                // MonthNames ist 0-basiert (Januar = Index 0), Key.Month ist 1-basiert
                string label = $"{monthNames[monthGroup.Key.Month - 1]} {monthGroup.Key.Year}";

                // SortKey: negativer Tageswert des Monatsanfangs → neuester Monat zuerst
                DateTime monthStart = new(monthGroup.Key.Year, monthGroup.Key.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                int sortKey = -(int)(monthStart - DateTime.UnixEpoch).TotalDays;

                List<NewEpisodeCardViewModel> cards = monthGroup
                    .Select(e => e.Card)
                    .ToList();

                groups.Add(new NewEpisodesGroupViewModel(label, sortKey, cards));
            }

            NewEpisodeGroups = new ObservableCollection<NewEpisodesGroupViewModel>(groups);
        }

    }
}

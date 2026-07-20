using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MicrosoftDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Interner Helper für das <see cref="DashboardViewModel"/>.
    /// Enthält die gesamte Card- und Section-Build-Logik, die das Dashboard früher direkt im VM
    /// trug – Cover-Auflösung, Episode-Status-Ableitung, In-Progress-/Recent-Listenaufbau,
    /// Monatsgruppierung der Neuerscheinungen. Hat selbst keinen UI-State; alle Methoden
    /// nehmen ihre Abhängigkeiten als Parameter oder aus dem per Konstruktor übergebenen Scope.
    /// </summary>
    internal sealed class DashboardDataLoader
    {
        // Maximalanzahl der laufenden Episoden im In-Progress-Abschnitt
        private const int MaxInProgressEpisodes = 10;

        // Maximalanzahl der zuletzt gehörten Serien im Dashboard-Abschnitt
        private const int MaxRecentSeries = 8;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IErrorDialogService _errorDialogService;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly IPlayerService _playerService;
        private readonly CoverService? _coverService;
        private readonly ILocalizationService? _localizationService;
        private readonly IClock _clock;
        private readonly ILogger _logger;
        private readonly BackgroundCoverService? _backgroundCoverService;
        private readonly MicrosoftDispatcherQueue? _dispatcherQueue;

        // Zuordnung EpisodenId → offene Kacheln, damit der Hintergrund-Callback
        // den passenden Kachel-Satz mit dem nachgeladenen Cover versorgen kann.
        // Wird pro LoadAsync-Durchlauf vom Aufrufer über ResetPendingEpisodeCoverRefresh() zurückgesetzt.
        private readonly Dictionary<Guid, List<NewEpisodeCardViewModel>> _pendingEpisodeCoverCards = [];

        // Cache der Episoden-Cover-Bytes aus der DB für den aktuellen Load-Durchlauf.
        // Wird zu Beginn von LoadAsync einmal gefüllt und in allen Build*-Schritten wiederverwendet,
        // statt pro Kachel eine eigene Abfrage auf CoverImages auszulösen.
        private IReadOnlyDictionary<Guid, byte[]> _episodeCoverBytesCache =
            new Dictionary<Guid, byte[]>();

        /// <summary>
        /// Initialisiert den Loader mit den für Card- und Section-Building nötigen Services.
        /// </summary>
        public DashboardDataLoader(
            IServiceScopeFactory scopeFactory,
            IErrorDialogService errorDialogService,
            IConfirmationDialogService confirmationDialogService,
            IPlayerService playerService,
            CoverService? coverService,
            ILocalizationService? localizationService,
            IClock clock,
            ILogger logger,
            BackgroundCoverService? backgroundCoverService = null,
            MicrosoftDispatcherQueue? dispatcherQueue = null)
        {
            _scopeFactory = scopeFactory;
            _errorDialogService = errorDialogService;
            _confirmationDialogService = confirmationDialogService;
            _playerService = playerService;
            _coverService = coverService;
            _localizationService = localizationService;
            _clock = clock;
            _logger = logger;
            _backgroundCoverService = backgroundCoverService;
            _dispatcherQueue = dispatcherQueue;
        }

        /// <summary>
        /// Setzt die für den aktuellen Load-Durchlauf gesammelten Hintergrund-Anfragen zurück
        /// und lädt die bekannten Episoden-Cover-Bytes neu aus der DB (einmalige Batch-Query).
        /// </summary>
        public async Task BeginLoadSessionAsync(IReadOnlyList<Guid> relevantEpisodeIds, CancellationToken cancellationToken = default)
        {
            _pendingEpisodeCoverCards.Clear();
            _episodeCoverBytesCache = _coverService is not null && relevantEpisodeIds.Count > 0
                ? await _coverService.GetEpisodeCoverBytesAsync(relevantEpisodeIds, cancellationToken)
                : new Dictionary<Guid, byte[]>();
        }

        /// <summary>
        /// Reicht die während des Load-Durchlaufs gesammelten Kacheln an den
        /// <see cref="BackgroundCoverService"/> weiter, damit deren Folgen-Cover progressiv
        /// nachgeladen werden. Ohne registrierten Service (z. B. Unit-Tests) passiert nichts.
        /// </summary>
        public void FlushPendingEpisodeCoverRefresh()
        {
            if (_backgroundCoverService is null || _pendingEpisodeCoverCards.Count == 0)
            {
                return;
            }

            Dictionary<Guid, List<NewEpisodeCardViewModel>> snapshot = new(_pendingEpisodeCoverCards);
            List<Guid> ids = [.. snapshot.Keys];
            _pendingEpisodeCoverCards.Clear();

            _backgroundCoverService.EnqueueForEpisodes(ids, (episodeId, bytes) =>
            {
                if (!snapshot.TryGetValue(episodeId, out List<NewEpisodeCardViewModel>? cards))
                {
                    return;
                }

                DispatchCoverUpdate(cards, bytes);
            });
        }

        /// <summary>
        /// Marshallt die Bitmap-Erzeugung auf den UI-Thread und aktualisiert jede registrierte Kachel.
        /// Ohne Dispatcher (Unit-Tests) wird synchron auf einem Hintergrund-Task konvertiert,
        /// was für Tests ausreicht (WinUI-BitmapImage ist dort ohnehin nicht wirklich nutzbar).
        /// </summary>
        private void DispatchCoverUpdate(IReadOnlyList<NewEpisodeCardViewModel> cards, byte[] bytes)
        {
            if (_dispatcherQueue is not null)
            {
                _ = _dispatcherQueue.TryEnqueue(async () =>
                {
                    BitmapImage? bitmap = await CoverService.ConvertToBitmapAsync(bytes);
                    if (bitmap is null) return;

                    foreach (NewEpisodeCardViewModel card in cards)
                    {
                        card.UpdateCoverImage(bitmap);
                    }
                });
            }
            else
            {
                _ = Task.Run(async () =>
                {
                    BitmapImage? bitmap = await CoverService.ConvertToBitmapAsync(bytes);
                    if (bitmap is null) return;

                    foreach (NewEpisodeCardViewModel card in cards)
                    {
                        card.UpdateCoverImage(bitmap);
                    }
                });
            }
        }

        /// <summary>
        /// Registriert eine Kachel, die ein spezifisches Folgen-Cover nachgereicht bekommen soll,
        /// sobald der Hintergrund-Service es geladen hat.
        /// </summary>
        private void TrackPendingEpisodeCover(Guid episodeId, NewEpisodeCardViewModel card)
        {
            if (episodeId == Guid.Empty) return;

            if (!_pendingEpisodeCoverCards.TryGetValue(episodeId, out List<NewEpisodeCardViewModel>? list))
            {
                list = [];
                _pendingEpisodeCoverCards[episodeId] = list;
            }
            list.Add(card);
        }

        /// <summary>
        /// Erstellt eine neue Episodenkachel aus den übergebenen Rohdaten.
        /// Cover-Strategie (Startpfad-optimiert): Episoden-Cover nur aus der DB,
        /// sonst Serien-Cover als Fallback. Fehlende Folgen-Cover werden zum
        /// Nachladen an die Hintergrund-Queue übergeben (siehe <see cref="FlushPendingEpisodeCoverRefresh"/>).
        /// </summary>
        public async Task<NewEpisodeCardViewModel> BuildCardAsync(
            Series series,
            Episode episode,
            PlaybackState? state,
            bool hasLocalTrack,
            bool isAnnounced)
        {
            PlaybackStatus status = DetermineStatus(state);
            double progress = CalculateProgress(state, episode.Duration);

            (BitmapImage? cover, bool hasEpisodeCover) =
                await ResolveCardCoverAsync(series, episode.Id);

            NewEpisodeCardViewModel card = new(
                episodeId: episode.Id,
                seriesId: series.Id,
                seriesName: series.Title,
                episodeTitle: episode.Title,
                coverImage: cover,
                status: status,
                progressPercent: progress,
                hasLocalTrack: hasLocalTrack,
                isAnnounced: isAnnounced,
                scopeFactory: _scopeFactory,
                errorDialogService: _errorDialogService,
                confirmationDialogService: _confirmationDialogService,
                playerService: _playerService,
                episodeNumber: episode.EpisodeNumber,
                releaseDate: episode.ReleaseDate,
                localizationService: _localizationService,
                clock: _clock);

            if (!hasEpisodeCover)
            {
                TrackPendingEpisodeCover(episode.Id, card);
            }

            return card;
        }

        /// <summary>
        /// Ermittelt das initial anzuzeigende Cover für eine Kachel.
        /// Rückgabewert: (Bitmap, HasEpisodeCover) — der zweite Wert zeigt an,
        /// ob das echte Folgen-Cover geladen wurde (<see langword="true"/>) oder das Serien-Fallback.
        /// Liest ausschließlich aus der DB (CoverImages); Dateisystem/ID3/Online-Lookup laufen
        /// über <see cref="BackgroundCoverService.EnqueueForEpisodes"/> nach dem Rendern.
        /// </summary>
        private async Task<(BitmapImage? Cover, bool HasEpisodeCover)> ResolveCardCoverAsync(
            Series series, Guid episodeId)
        {
            if (episodeId != Guid.Empty)
            {
                if (_episodeCoverBytesCache.TryGetValue(episodeId, out byte[]? bytes))
                {
                    BitmapImage? episodeCover = await CoverService.ConvertToBitmapAsync(bytes);
                    if (episodeCover is not null)
                    {
                        return (episodeCover, true);
                    }
                }
                else if (_coverService is not null)
                {
                    // Kachel war nicht im Pre-Load-Batch – einzelne DB-Abfrage, kein Dateisystem.
                    BitmapImage? episodeCover = await _coverService.GetEpisodeCoverImageAsync(episodeId);
                    if (episodeCover is not null)
                    {
                        return (episodeCover, true);
                    }
                }
            }

            return (await BuildSeriesCoverAsync(series), false);
        }

        /// <summary>
        /// Erstellt ein Cover-Bild für eine Serie.
        /// Priorität: DB-Cover (CoverService) → cover.jpg im Serienordner → URL-Cover → null.
        /// </summary>
        public Task<BitmapImage?> BuildSeriesCoverAsync(Series series) =>
            CoverFactory.BuildSeriesCoverAsync(series);

        /// <summary>
        /// Erstellt ein Cover-Bild für eine Episode.
        /// Priorität: DB-Cover → cover.jpg im Ordner → ID3-Tag des ersten Tracks → null.
        /// </summary>
        public Task<BitmapImage?> BuildEpisodeCoverAsync(Episode episode) =>
            CoverFactory.BuildEpisodeCoverAsync(episode);

        // Lazy-Init der Factory: instanziiert beim ersten Cover-Aufruf, gemeinsam für Loader.
        private ICoverViewModelFactory CoverFactory =>
            _coverFactory ??= new CoverViewModelFactory(_scopeFactory, _coverService);
        private ICoverViewModelFactory? _coverFactory;

        /// <summary>
        /// Ermittelt aktuell laufende Episoden anhand der Wiedergabestände.
        /// „In Progress" bedeutet: Wiedergabestand &gt; 0 und noch nicht abgeschlossen.
        /// Sortiert nach dem aktuellsten Wiedergabezeitpunkt, beschränkt auf <see cref="MaxInProgressEpisodes"/>.
        /// </summary>
        public async Task<IReadOnlyList<NewEpisodeCardViewModel>> BuildInProgressEpisodesAsync(
            IEpisodeDataService episodeService,
            IReadOnlyList<PlaybackState> allStates,
            IReadOnlyList<Series> subscribedSeries,
            CancellationToken cancellationToken = default)
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

            // Batch-Lookup: alle benötigten Episoden in einem Roundtrip statt einer pro State.
            IReadOnlyDictionary<Guid, Episode> episodes =
                await LoadEpisodesByIdsAsync(episodeService, activeStates, cancellationToken);

            foreach (PlaybackState state in activeStates)
            {
                if (result.Count >= MaxInProgressEpisodes)
                {
                    break;
                }

                if (!episodes.TryGetValue(state.EpisodeId, out Episode? episode))
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
        /// Lädt alle <see cref="Episode"/>-Datensätze, deren IDs in den übergebenen Wiedergabeständen vorkommen,
        /// in einer einzigen <c>WHERE Id IN (...)</c>-Abfrage.
        /// </summary>
        private static async Task<IReadOnlyDictionary<Guid, Episode>> LoadEpisodesByIdsAsync(
            IEpisodeDataService episodeService,
            List<PlaybackState> states,
            CancellationToken cancellationToken = default)
        {
            if (states.Count == 0)
            {
                return new Dictionary<Guid, Episode>(0);
            }

            HashSet<Guid> uniqueIds = new(states.Count);
            foreach (PlaybackState state in states)
            {
                _ = uniqueIds.Add(state.EpisodeId);
            }

            return await episodeService.GetByIdsAsync([.. uniqueIds], cancellationToken);
        }

        /// <summary>
        /// Ermittelt die zuletzt gehörten Serien anhand der Wiedergabestände.
        /// Sortiert nach aktuellstem Änderungszeitpunkt, dedupliziert nach Serie,
        /// beschränkt auf <see cref="MaxRecentSeries"/>.
        /// </summary>
        public async Task<IReadOnlyList<RecentSeriesCardViewModel>> BuildRecentSeriesAsync(
            IEpisodeDataService episodeService,
            IReadOnlyList<PlaybackState> allStates,
            IReadOnlyList<Series> subscribedSeries,
            CancellationToken cancellationToken = default)
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

            // Neueste zuerst – LastPlayedAt bevorzugt, dann UpdatedAt, CreatedAt als letzter Fallback
            activeStates.Sort((a, b) =>
            {
                DateTime timeA = a.LastPlayedAt ?? a.UpdatedAt ?? a.CreatedAt;
                DateTime timeB = b.LastPlayedAt ?? b.UpdatedAt ?? b.CreatedAt;
                return timeB.CompareTo(timeA);
            });

            // Für jede Serie nur den neuesten Eintrag behalten
            HashSet<Guid> seenSeriesIds = [];
            List<RecentSeriesCardViewModel> result = [];

            // Batch-Lookup: alle benötigten Episoden in einem Roundtrip statt einer pro State.
            IReadOnlyDictionary<Guid, Episode> episodes =
                await LoadEpisodesByIdsAsync(episodeService, activeStates, cancellationToken);

            foreach (PlaybackState state in activeStates)
            {
                if (result.Count >= MaxRecentSeries)
                {
                    break;
                }

                if (!episodes.TryGetValue(state.EpisodeId, out Episode? episode))
                {
                    continue;
                }

                if (!seenSeriesIds.Add(episode.SeriesId))
                {
                    // Diese Serie haben wir schon – nur den neuesten Eintrag anzeigen
                    continue;
                }

                Series? series = FindSeriesById(subscribedSeries, episode.SeriesId);
                if (series is null)
                {
                    continue;
                }

                // Startpfad-Strategie: Episoden-Cover nur aus dem DB-Cache,
                // sonst Serien-Cover als Fallback. Das Nachladen aus Dateisystem/ID3/Provider
                // übernimmt die Hintergrund-Queue, nicht der Dashboard-Startpfad.
                BitmapImage? cover = null;
                if (_episodeCoverBytesCache.TryGetValue(episode.Id, out byte[]? recentBytes))
                {
                    // ConvertToBitmapAsync ist UI-Thread-bound (WinUI BitmapImage); ein extern propagierter
                    // CT bringt keinen Nutzen, weil die Konvertierung sehr kurz und nicht CT-fähig ist.
                    cover = await CoverService.ConvertToBitmapAsync(recentBytes, CancellationToken.None);
                }
                cover ??= await BuildSeriesCoverAsync(series);

                result.Add(new RecentSeriesCardViewModel(
                    seriesId: series.Id,
                    seriesName: series.Title,
                    lastEpisodeTitle: episode.Title,
                    coverImage: cover));
            }

            return result;
        }

        /// <summary>
        /// Baut die Neuerscheinungs-Gruppen aus den gecachten DB-Einträgen auf.
        /// Bei Fehlern wird eine leere Liste zurückgegeben und der Vorfall geloggt.
        /// </summary>
        public async Task<IReadOnlyList<NewEpisodesGroupViewModel>> BuildNewReleaseGroupsAsync(
            IReadOnlyList<Series> subscribedSeries,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IEpisodeDataService episodeService =
                    scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                IPlaybackStateDataService stateService =
                    scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();
                ICachedNewReleaseDataService cacheService =
                    scope.ServiceProvider.GetRequiredService<ICachedNewReleaseDataService>();

                IReadOnlyList<CachedNewRelease> cached = await cacheService.GetAllAsync(cancellationToken);
                if (cached.Count == 0)
                {
                    return [];
                }

                return await BuildTilesFromEntries(cached, subscribedSeries, episodeService, stateService, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.Warning("Neuerscheinungen aus Cache konnten nicht geladen werden: {Reason}", ex.Message);
                return [];
            }
            catch (System.IO.IOException ex)
            {
                _logger.Warning("Neuerscheinungen aus Cache konnten nicht geladen werden: {Reason}", ex.Message);
                return [];
            }
        }

        /// <summary>
        /// Wandelt gecachte Neuerscheinungen in Kachel-ViewModels um und gruppiert sie nach Monat.
        /// "Angekündigt" (Zukunft) bildet eine eigene Gruppe ganz oben,
        /// danach folgen die Monate absteigend (neuester zuerst).
        /// Gehörte Folgen werden herausgefiltert.
        /// </summary>
        private async Task<IReadOnlyList<NewEpisodesGroupViewModel>> BuildTilesFromEntries(
            IReadOnlyList<CachedNewRelease> cached,
            IReadOnlyList<Series> subscribedSeries,
            IEpisodeDataService episodeService,
            IPlaybackStateDataService stateService,
            CancellationToken cancellationToken = default)
        {
            DateTime today = _clock.UtcNow.Date;

            // Monatsnamen aus der aktuellen Kultur – funktioniert für DE und EN
            string[] monthNames = CultureInfo.CurrentCulture.DateTimeFormat.MonthNames;

            // Episoden und PlaybackStates einmal pro Serie laden (vermeidet N+1-Abfragen).
            Dictionary<Guid, IReadOnlyList<Episode>> episodesBySeries = [];
            IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync(cancellationToken);
            Dictionary<Guid, PlaybackState> stateByEpisodeId = new(allStates.Count);
            foreach (PlaybackState ps in allStates)
            {
                stateByEpisodeId[ps.EpisodeId] = ps;
            }

            // Kacheln erzeugen und gleichzeitig das ReleaseDate merken (für die Monatsgruppierung).
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
                        localEpisodes = await episodeService.GetBySeriesIdAsync(series.Id, cancellationToken);
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

                // Cover-Priorität (Startpfad-optimiert):
                // 1) Folgen-Cover aus dem DB-Cache (CoverImages),
                // 2) iTunes-Album-Cover via URL (schon vor dem Start aufgelöst),
                // 3) Serien-Cover.
                // iTunes liefert pro Album eine Cover-URL (100×100), die über URL-Pattern
                // auf höhere Auflösung skaliert werden kann (100x100bb → 600x600bb).
                BitmapImage? cover = null;
                bool hasEpisodeCover = false;
                if (episodeId != Guid.Empty
                    && _episodeCoverBytesCache.TryGetValue(episodeId, out byte[]? newReleaseBytes))
                {
                    cover = await CoverService.ConvertToBitmapAsync(newReleaseBytes, CancellationToken.None);
                    hasEpisodeCover = cover is not null;
                }

                // iTunes-Cover als Fallback: höhere Auflösung per URL-Pattern
                if (cover is null && !string.IsNullOrEmpty(entry.CoverUrl))
                {
                    string highResCoverUrl = entry.CoverUrl.Replace("100x100bb", "600x600bb", StringComparison.Ordinal);
                    cover = new BitmapImage(new Uri(highResCoverUrl));
                }

                cover ??= await BuildSeriesCoverAsync(series);

                Guid cardEpisodeId = episodeId != Guid.Empty ? episodeId : Guid.NewGuid();

                NewEpisodeCardViewModel card = new(
                    episodeId: cardEpisodeId,
                    seriesId: series.Id,
                    seriesName: series.Title,
                    episodeTitle: entry.Title,
                    coverImage: cover,
                    status: PlaybackStatus.NotStarted,
                    progressPercent: 0,
                    hasLocalTrack: hasLocalTrack,
                    isAnnounced: isAnnounced,
                    scopeFactory: _scopeFactory,
                    errorDialogService: _errorDialogService,
                    confirmationDialogService: _confirmationDialogService,
                    playerService: _playerService,
                    episodeNumber: entry.EpisodeNumber,
                    releaseDate: entry.ReleaseDate,
                    localizationService: _localizationService,
                    clock: _clock);

                // Nur lokal bekannte Folgen (echte EpisodeId) kommen in die Hintergrund-Queue –
                // reine Cache-Einträge ohne lokalen Match haben keine durchsuchbare Cover-Quelle.
                if (!hasEpisodeCover && episodeId != Guid.Empty)
                {
                    TrackPendingEpisodeCover(episodeId, card);
                }

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

            return groups;
        }

        /// <summary>Sucht eine Serie in einer Liste anhand ihrer ID.</summary>
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

        /// <summary>Leitet den <see cref="PlaybackStatus"/> aus dem gespeicherten Zustand ab.</summary>
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
    }
}

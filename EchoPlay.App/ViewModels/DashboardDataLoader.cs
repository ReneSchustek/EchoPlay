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
using System.Threading.Tasks;

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
            ILogger logger)
        {
            _scopeFactory              = scopeFactory;
            _errorDialogService        = errorDialogService;
            _confirmationDialogService = confirmationDialogService;
            _playerService             = playerService;
            _coverService              = coverService;
            _localizationService       = localizationService;
            _clock                     = clock;
            _logger                    = logger;
        }

        /// <summary>
        /// Erstellt eine neue Episodenkachel aus den übergebenen Rohdaten.
        /// </summary>
        public async Task<NewEpisodeCardViewModel> BuildCardAsync(
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
                                 ?? await BuildSeriesCoverAsync(series);

            return new NewEpisodeCardViewModel(
                episodeId:                 episode.Id,
                seriesId:                  series.Id,
                seriesName:                series.Title,
                episodeTitle:              episode.Title,
                coverImage:                cover,
                status:                    status,
                progressPercent:           progress,
                hasLocalTrack:             hasLocalTrack,
                isAnnounced:               isAnnounced,
                scopeFactory:              _scopeFactory,
                errorDialogService:        _errorDialogService,
                confirmationDialogService: _confirmationDialogService,
                playerService:             _playerService,
                episodeNumber:             episode.EpisodeNumber,
                releaseDate:               episode.ReleaseDate,
                localizationService:       _localizationService,
                clock:                     _clock);
        }

        /// <summary>
        /// Erstellt ein Cover-Bild für eine Serie.
        /// Priorität: DB-Cover (CoverService) → cover.jpg im Serienordner → URL-Cover → null.
        /// </summary>
        public async Task<BitmapImage?> BuildSeriesCoverAsync(Series series)
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
                    catch (System.IO.IOException)
                    {
                        // Datei-Zugriffsfehler (gesperrt, gelöscht) – Serien-Cover bleibt leer
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Kein Leserecht – Serien-Cover bleibt leer
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
        /// Erstellt ein Cover-Bild für eine Episode.
        /// Priorität: DB-Cover → cover.jpg im Ordner → ID3-Tag des ersten Tracks → null.
        /// </summary>
        public async Task<BitmapImage?> BuildEpisodeCoverAsync(Episode episode)
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
                        ILocalTrackDataService trackService =
                            scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
                        IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episode.Id);
                        firstTrackPath = tracks.OrderBy(t => t.TrackNumber).FirstOrDefault()?.FilePath;
                    }

                    byte[]? coverBytes = await coverLoader.LoadAsync(episode.LocalFolderPath, firstTrackPath);
                    if (coverBytes is not null)
                    {
                        return await CoverService.ConvertToBitmapAsync(coverBytes);
                    }
                }
                catch (System.IO.IOException ex)
                {
                    _logger.Debug($"Cover-Laden fehlgeschlagen: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.Debug($"Cover-Laden ohne Zugriff: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// Ermittelt aktuell laufende Episoden anhand der Wiedergabestände.
        /// „In Progress" bedeutet: Wiedergabestand &gt; 0 und noch nicht abgeschlossen.
        /// Sortiert nach dem aktuellsten Wiedergabezeitpunkt, beschränkt auf <see cref="MaxInProgressEpisodes"/>.
        /// </summary>
        public async Task<IReadOnlyList<NewEpisodeCardViewModel>> BuildInProgressEpisodesAsync(
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
        /// Sortiert nach aktuellstem Änderungszeitpunkt, dedupliziert nach Serie,
        /// beschränkt auf <see cref="MaxRecentSeries"/>.
        /// </summary>
        public async Task<IReadOnlyList<RecentSeriesCardViewModel>> BuildRecentSeriesAsync(
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

            foreach (PlaybackState state in activeStates)
            {
                if (result.Count >= MaxRecentSeries)
                {
                    break;
                }

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

                Series? series = FindSeriesById(subscribedSeries, episode.SeriesId);
                if (series is null)
                {
                    continue;
                }

                // Episoden-Cover der zuletzt gehörten Folge bevorzugen, Serien-Cover als Fallback
                BitmapImage? cover = await BuildEpisodeCoverAsync(episode)
                                     ?? await BuildSeriesCoverAsync(series);

                result.Add(new RecentSeriesCardViewModel(
                    seriesId:         series.Id,
                    seriesName:       series.Title,
                    lastEpisodeTitle: episode.Title,
                    coverImage:       cover));
            }

            return result;
        }

        /// <summary>
        /// Baut die Neuerscheinungs-Gruppen aus den gecachten DB-Einträgen auf.
        /// Bei Fehlern wird eine leere Liste zurückgegeben und der Vorfall geloggt.
        /// </summary>
        public async Task<IReadOnlyList<NewEpisodesGroupViewModel>> BuildNewReleaseGroupsAsync(
            IReadOnlyList<Series> subscribedSeries)
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

                IReadOnlyList<CachedNewRelease> cached = await cacheService.GetAllAsync();
                if (cached.Count == 0)
                {
                    return [];
                }

                return await BuildTilesFromEntries(cached, subscribedSeries, episodeService, stateService);
            }
            catch (InvalidOperationException ex)
            {
                _logger.Warning($"Neuerscheinungen aus Cache konnten nicht geladen werden: {ex.Message}");
                return [];
            }
            catch (System.IO.IOException ex)
            {
                _logger.Warning($"Neuerscheinungen aus Cache konnten nicht geladen werden: {ex.Message}");
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
            IPlaybackStateDataService stateService)
        {
            DateTime today = _clock.UtcNow.Date;

            // Monatsnamen aus der aktuellen Kultur – funktioniert für DE und EN
            string[] monthNames = CultureInfo.CurrentCulture.DateTimeFormat.MonthNames;

            // Episoden und PlaybackStates einmal pro Serie laden (vermeidet N+1-Abfragen).
            Dictionary<Guid, IReadOnlyList<Episode>> episodesBySeries = [];
            IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync();
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
                bool isCompleted   = false;
                Guid episodeId     = Guid.Empty;

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
                            episodeId     = ep.Id;
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
                    string highResCoverUrl = entry.CoverUrl.Replace("100x100bb", "600x600bb", StringComparison.Ordinal);
                    cover = new BitmapImage(new Uri(highResCoverUrl));
                }

                cover ??= await BuildSeriesCoverAsync(series);

                NewEpisodeCardViewModel card = new(
                    episodeId:                 episodeId != Guid.Empty ? episodeId : Guid.NewGuid(),
                    seriesId:                  series.Id,
                    seriesName:                series.Title,
                    episodeTitle:              entry.Title,
                    coverImage:                cover,
                    status:                    PlaybackStatus.NotStarted,
                    progressPercent:           0,
                    hasLocalTrack:             hasLocalTrack,
                    isAnnounced:               isAnnounced,
                    scopeFactory:              _scopeFactory,
                    errorDialogService:        _errorDialogService,
                    confirmationDialogService: _confirmationDialogService,
                    playerService:             _playerService,
                    episodeNumber:             entry.EpisodeNumber,
                    releaseDate:               entry.ReleaseDate,
                    localizationService:       _localizationService,
                    clock:                     _clock);

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

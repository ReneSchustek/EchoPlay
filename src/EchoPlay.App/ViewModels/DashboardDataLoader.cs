using EchoPlay.App.Models;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Baut die Kacheln der beiden Wiedergabe-Abschnitte der Startseite: „läuft gerade"
    /// und „zuletzt gehört". Beide leiten sich aus denselben Wiedergabeständen ab und
    /// unterscheiden sich nur in Auswahl und Sortierung.
    /// </summary>
    /// <remarks>
    /// Der Typ hält keinen Zustand der Oberfläche. Die Cover kommen vom
    /// <see cref="DashboardCoverProvider"/>, die Neuerscheinungen baut der
    /// <see cref="NewReleaseGroupBuilder"/>.
    /// </remarks>
    internal sealed class DashboardDataLoader
    {
        /// <summary>Höchstzahl der Kacheln im Abschnitt „läuft gerade".</summary>
        private const int MaxInProgressEpisodes = 10;

        /// <summary>Höchstzahl der Kacheln im Abschnitt „zuletzt gehört".</summary>
        private const int MaxRecentSeries = 8;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DashboardCoverProvider _coverProvider;
        private readonly NewEpisodeCardServices _cardServices;
        private readonly IClock _clock;

        /// <summary>
        /// Initialisiert den Aufbau mit den Diensten, die Kacheln und Cover brauchen.
        /// </summary>
        /// <param name="scopeFactory">Für datenbanknahe Befehle der Kacheln.</param>
        /// <param name="coverProvider">Beschafft die Cover.</param>
        /// <param name="cardServices">Die Dienste, die jede Kachel für ihre Befehle braucht.</param>
        /// <param name="clock">Zeitquelle der Kacheln.</param>
        public DashboardDataLoader(
            IServiceScopeFactory scopeFactory,
            DashboardCoverProvider coverProvider,
            NewEpisodeCardServices cardServices,
            IClock clock)
        {
            _scopeFactory = scopeFactory;
            _coverProvider = coverProvider;
            _cardServices = cardServices;
            _clock = clock;
        }

        /// <summary>
        /// Erstellt eine Folgen-Kachel aus den übergebenen Rohdaten. Fehlt das Folgen-Cover,
        /// wird die Kachel zum Nachtragen vorgemerkt.
        /// </summary>
        /// <param name="series">Die Serie der Folge.</param>
        /// <param name="episode">Die Folge.</param>
        /// <param name="state">Ihr Wiedergabestand, sofern vorhanden.</param>
        /// <param name="hasLocalTrack">Ob Dateien dazu vorliegen.</param>
        /// <param name="isAnnounced">Ob die Folge erst angekündigt ist.</param>
        /// <returns>Die fertige Kachel.</returns>
        public async Task<NewEpisodeCardViewModel> BuildCardAsync(
            Series series,
            Episode episode,
            PlaybackState? state,
            bool hasLocalTrack,
            bool isAnnounced)
        {
            (BitmapImage? cover, bool hasEpisodeCover) =
                await _coverProvider.ResolveCardCoverAsync(series, episode.Id);

            NewEpisodeCardViewModel card = new(
                episodeId: episode.Id,
                seriesId: series.Id,
                seriesName: series.Title,
                episodeTitle: episode.Title,
                coverImage: cover,
                status: PlaybackStatusResolver.Resolve(state),
                progressPercent: CalculateProgress(state, episode.Duration),
                hasLocalTrack: hasLocalTrack,
                isAnnounced: isAnnounced,
                scopeFactory: _scopeFactory,
                errorDialogService: _cardServices.ErrorDialogService,
                confirmationDialogService: _cardServices.ConfirmationDialogService,
                playerService: _cardServices.PlayerService,
                episodeNumber: episode.EpisodeNumber,
                releaseDate: episode.ReleaseDate,
                localizationService: _cardServices.LocalizationService,
                clock: _clock,
                spotifyAlbumId: episode.SpotifyAlbumId,
                appleMusicAlbumId: episode.AppleMusicAlbumId);

            if (!hasEpisodeCover)
            {
                _coverProvider.TrackPending(episode.Id, card);
            }

            return card;
        }

        /// <summary>
        /// Ermittelt die gerade laufenden Folgen: Wiedergabestand größer null und noch nicht
        /// abgeschlossen. Neueste zuerst, begrenzt auf <see cref="MaxInProgressEpisodes"/>.
        /// </summary>
        /// <param name="episodeService">Zugriff auf die Folgen.</param>
        /// <param name="allStates">Alle Wiedergabestände.</param>
        /// <param name="subscribedSeries">Die abonnierten Serien.</param>
        /// <param name="cancellationToken">Bricht den Aufbau ab.</param>
        /// <returns>Die Kacheln in Anzeigereihenfolge.</returns>
        public async Task<IReadOnlyList<NewEpisodeCardViewModel>> BuildInProgressEpisodesAsync(
            IEpisodeDataService episodeService,
            IReadOnlyList<PlaybackState> allStates,
            IReadOnlyList<Series> subscribedSeries,
            CancellationToken cancellationToken = default)
        {
            // Eine offene Stelle zählt, nicht der Hörstatus: Wer eine gehörte Folge erneut
            // angefangen hat, findet sie hier wieder. Ob die Stelle wirklich offen ist,
            // entscheidet sich erst mit der Dauer der Folge — die steht unten beim Bauen
            // der Kachel zur Verfügung, hier noch nicht.
            List<PlaybackState> activeStates = SelectStates(allStates, s => s.LastPosition > TimeSpan.Zero);
            SortByMostRecent(activeStates, s => s.UpdatedAt ?? s.CreatedAt);

            // Ein Roundtrip für alle benötigten Folgen statt einer je Stand.
            IReadOnlyDictionary<Guid, Episode> episodes =
                await LoadEpisodesByIdsAsync(episodeService, activeStates, cancellationToken);

            List<NewEpisodeCardViewModel> result = [];

            foreach (PlaybackState state in activeStates)
            {
                if (result.Count >= MaxInProgressEpisodes)
                {
                    break;
                }

                if (!TryResolve(episodes, subscribedSeries, state, out Episode? episode, out Series? series)
                    || !PlaybackStatusResolver.HasOpenPosition(state, episode.Duration))
                {
                    continue;
                }

                result.Add(await BuildCardAsync(series, episode, state, episode.LocalTrackCount is > 0, false));
            }

            return result;
        }

        /// <summary>
        /// Ermittelt die zuletzt gehörten Serien. Neueste zuerst, je Serie nur der jüngste
        /// Eintrag, begrenzt auf <see cref="MaxRecentSeries"/>.
        /// </summary>
        /// <param name="episodeService">Zugriff auf die Folgen.</param>
        /// <param name="allStates">Alle Wiedergabestände.</param>
        /// <param name="subscribedSeries">Die abonnierten Serien.</param>
        /// <param name="cancellationToken">Bricht den Aufbau ab.</param>
        /// <returns>Die Kacheln in Anzeigereihenfolge.</returns>
        public async Task<IReadOnlyList<RecentSeriesCardViewModel>> BuildRecentSeriesAsync(
            IEpisodeDataService episodeService,
            IReadOnlyList<PlaybackState> allStates,
            IReadOnlyList<Series> subscribedSeries,
            CancellationToken cancellationToken = default)
        {
            // Stände mit echter Hörzeit oder als gehört gekennzeichnet — Letzteres trifft
            // Folgen, die außerhalb der Anwendung gehört wurden.
            List<PlaybackState> activeStates = SelectStates(allStates, s => s.LastPosition > TimeSpan.Zero || s.IsCompleted);
            SortByMostRecent(activeStates, s => s.LastPlayedAt ?? s.UpdatedAt ?? s.CreatedAt);

            IReadOnlyDictionary<Guid, Episode> episodes =
                await LoadEpisodesByIdsAsync(episodeService, activeStates, cancellationToken);

            HashSet<Guid> seenSeriesIds = [];
            List<RecentSeriesCardViewModel> result = [];

            foreach (PlaybackState state in activeStates)
            {
                if (result.Count >= MaxRecentSeries)
                {
                    break;
                }

                if (!TryResolve(episodes, subscribedSeries, state, out Episode? episode, out Series? series)
                    || !seenSeriesIds.Add(episode.SeriesId))
                {
                    continue;
                }

                // Startpfad: Folgen-Cover nur aus der Datenbank, sonst das Serien-Cover.
                // Das Nachladen übernimmt der Hintergrunddienst, nicht der Startpfad.
                BitmapImage? cover = await _coverProvider.TryGetCachedEpisodeCoverAsync(episode.Id)
                    ?? await _coverProvider.BuildSeriesCoverAsync(series);

                result.Add(new RecentSeriesCardViewModel(
                    seriesId: series.Id,
                    seriesName: series.Title,
                    lastEpisodeTitle: episode.Title,
                    coverImage: cover,
                    localizationService: _cardServices.LocalizationService));
            }

            return result;
        }

        /// <summary>
        /// Berechnet den Wiedergabefortschritt in Prozent (0–100).
        /// Ohne Zustand oder ohne bekannte Gesamtdauer sind es null Prozent.
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

        /// <summary>Filtert die Wiedergabestände nach der übergebenen Bedingung.</summary>
        private static List<PlaybackState> SelectStates(IReadOnlyList<PlaybackState> allStates, Func<PlaybackState, bool> predicate)
        {
            List<PlaybackState> selected = [];
            foreach (PlaybackState state in allStates)
            {
                if (predicate(state))
                {
                    selected.Add(state);
                }
            }

            return selected;
        }

        /// <summary>Sortiert die Stände absteigend nach dem gewählten Zeitpunkt.</summary>
        private static void SortByMostRecent(List<PlaybackState> states, Func<PlaybackState, DateTime> timestamp)
            => states.Sort((a, b) => timestamp(b).CompareTo(timestamp(a)));

        /// <summary>
        /// Lädt alle Folgen, deren Kennungen in den Ständen vorkommen, in einer einzigen
        /// Abfrage.
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
        /// Ordnet einem Wiedergabestand Folge und Serie zu. Fehlt eines von beidem, gehört
        /// der Stand zu einem Bestand, den die Startseite nicht zeigt.
        /// </summary>
        private static bool TryResolve(
            IReadOnlyDictionary<Guid, Episode> episodes,
            IReadOnlyList<Series> subscribedSeries,
            PlaybackState state,
            out Episode episode,
            out Series series)
        {
            episode = null!;
            series = null!;

            if (!episodes.TryGetValue(state.EpisodeId, out Episode? found))
            {
                return false;
            }

            Series? owner = FindSeriesById(subscribedSeries, found.SeriesId);
            if (owner is null)
            {
                return false;
            }

            episode = found;
            series = owner;
            return true;
        }

        /// <summary>Sucht eine Serie in einer Liste anhand ihrer Kennung.</summary>
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
    }
}

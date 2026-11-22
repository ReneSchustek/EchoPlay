using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Globalization;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Liest eine Serie samt ihrer Folgen aus der Datenbank und baut daraus die Kacheln
    /// der Detailansicht mit Hörstatus, Fortschritt und Cover.
    /// </summary>
    /// <remarks>
    /// Die Wiedergabestände kommen in einer einzigen Abfrage; je Folge einzeln nachzusehen
    /// hieße bei einer Serie mit zweihundert Folgen zweihundert Abfragen.
    /// </remarks>
    internal sealed class SeriesEpisodeTileBuilder
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ICoverService? _coverService;
        private readonly ILocalizationService? _localizationService;

        private ICoverViewModelFactory? _coverFactory;
        private readonly ILogger? _logger;

        /// <summary>
        /// Initialisiert den Kachelbau.
        /// </summary>
        /// <param name="scopeFactory">Für die Abfragen und die Cover-Fabrik.</param>
        /// <param name="coverService">Zentraler Cover-Dienst. Nullable für Tests.</param>
        /// <param name="localizationService">Liefert die Namen für die Bedienhilfen. Nullable für Tests.</param>
        /// <param name="logger">Protokolliert übersprungene Cover-Quellen. Nullable für Tests.</param>
        public SeriesEpisodeTileBuilder(
            IServiceScopeFactory scopeFactory,
            ICoverService? coverService,
            ILocalizationService? localizationService,
            ILogger? logger = null)
        {
            _scopeFactory = scopeFactory;
            _coverService = coverService;
            _localizationService = localizationService;
            _logger = logger;
        }

        /// <summary>
        /// Liest die Serie und baut die Kacheln ihrer Folgen.
        /// </summary>
        /// <param name="seriesId">Die anzuzeigende Serie.</param>
        /// <param name="playEpisode">Wird von einer Kachel aufgerufen, um ihre Folge zu starten.</param>
        /// <param name="cancellationToken">Bricht das Laden ab.</param>
        /// <returns>Die Serie, die Kacheln und der Gesamtfortschritt.</returns>
        public async Task<SeriesDetailData> BuildAsync(
            Guid seriesId,
            Action<Guid> playEpisode,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(playEpisode);

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IPlaybackStateDataService playbackService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            Series? series = await seriesService.GetByIdAsync(seriesId, cancellationToken);
            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(seriesId, cancellationToken);
            Dictionary<Guid, PlaybackState> stateById = await LoadStatesAsync(playbackService, cancellationToken);

            List<EpisodeTileViewModel> tiles = new(episodes.Count);
            int completedCount = 0;

            foreach (Episode episode in episodes)
            {
                _ = stateById.TryGetValue(episode.Id, out PlaybackState? episodeState);
                PlaybackStatus playbackStatus = PlaybackStatusResolver.Resolve(episodeState);

                if (playbackStatus == PlaybackStatus.Finished)
                {
                    completedCount++;
                }

                tiles.Add(await BuildTileAsync(episode, series, episodeState, playbackStatus, playEpisode));
            }

            return new SeriesDetailData(
                series,
                tiles,
                BuildProgressText(completedCount, episodes.Count),
                episodes.Count > 0 ? (double)completedCount / episodes.Count * 100 : 0);
        }

        /// <summary>Liest alle Wiedergabestände in einer Abfrage und macht sie auffindbar.</summary>
        private static async Task<Dictionary<Guid, PlaybackState>> LoadStatesAsync(
            IPlaybackStateDataService playbackService,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<PlaybackState> allStates = await playbackService.GetAllAsync(cancellationToken);
            Dictionary<Guid, PlaybackState> stateById = new(allStates.Count);

            foreach (PlaybackState state in allStates)
            {
                stateById[state.EpisodeId] = state;
            }

            return stateById;
        }

        /// <summary>Baut eine einzelne Kachel samt Cover.</summary>
        private async Task<EpisodeTileViewModel> BuildTileAsync(
            Episode episode,
            Series? series,
            PlaybackState? episodeState,
            PlaybackStatus playbackStatus,
            Action<Guid> playEpisode)
        {
            Guid capturedId = episode.Id;

            // Cover der Folge, sonst das der Serie.
            BitmapImage? cover = await CoverFactory.BuildEpisodeCoverAsync(episode)
                                 ?? await CoverFactory.BuildSeriesCoverAsync(series);

            EpisodeTileData data = new()
            {
                EpisodeId = episode.Id,
                EpisodeNumber = episode.EpisodeNumber,
                Title = episode.Title,
                TotalDuration = episode.Duration > TimeSpan.Zero ? episode.Duration : null,
                PlaybackStatus = playbackStatus,
                ReleaseDate = episode.ReleaseDate,
                ProgressPercent = PlaybackStatusResolver.ProgressPercent(episodeState, episode.Duration),
                IsSpecialEpisode = episode.EpisodeNumber is null or 0,
                SpotifyAlbumId = episode.SpotifyAlbumId,
                HasLocalTrack = episode.LocalTrackCount is > 0,
                AppleMusicAlbumId = episode.AppleMusicAlbumId,
                SeriesTitle = series?.Title ?? string.Empty,
                HasOpenPosition = PlaybackStatusResolver.HasOpenPosition(episodeState, episode.Duration),
            };
            return new EpisodeTileViewModel(data, () => playEpisode(capturedId), cover, _localizationService);
        }

        /// <summary>Baut den Fortschrittstext, etwa „42 von 229 Folgen gehört".</summary>
        private string BuildProgressText(int completedCount, int episodeCount)
        {
            if (episodeCount == 0)
            {
                return string.Empty;
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                EchoPlay.App.Helpers.PluralText.Pattern(
                    _localizationService,
                    episodeCount,
                    "SeriesProgressTextSingular",
                    "SeriesProgressTextPlural",
                    "{0} von {1} Folge gehört",
                    "{0} von {1} Folgen gehört"),
                completedCount,
                episodeCount);
        }

        // Wird beim ersten Cover-Aufruf erzeugt und danach weiterverwendet.
        private ICoverViewModelFactory CoverFactory =>
            _coverFactory ??= new CoverViewModelFactory(_scopeFactory, _coverService, _logger);
    }

    /// <summary>
    /// Das Ergebnis eines Ladelaufs der Serienansicht.
    /// </summary>
    /// <param name="Series">Die geladene Serie, oder <see langword="null"/>, wenn es sie nicht mehr gibt.</param>
    /// <param name="Tiles">Die Kacheln aller Folgen, Sonderfolgen eingeschlossen.</param>
    /// <param name="ProgressText">Der Fortschrittstext für die Kopfzeile.</param>
    /// <param name="ProgressPercent">Der Gesamtfortschritt in Prozent (0–100).</param>
    internal sealed record SeriesDetailData(
        Series? Series,
        IReadOnlyList<EpisodeTileViewModel> Tiles,
        string ProgressText,
        double ProgressPercent);
}

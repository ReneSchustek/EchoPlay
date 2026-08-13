using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Baut die beiden Serien-Abschnitte der Startseite: „Weiterhören" und die Favoriten.
    /// Beide zeigen Serien statt Folgen, beide brauchen dafür nur das Serien-Cover und die
    /// vom Nutzer gespeicherte Reihenfolge.
    /// </summary>
    internal sealed class DashboardSeriesSectionBuilder
    {
        /// <summary>Abschnittsname, unter dem die Reihenfolge der Favoriten liegt.</summary>
        private const string SectionFavorites = "Favoriten";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DashboardCoverProvider _coverProvider;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly ILocalizationService? _localizationService;

        /// <summary>
        /// Initialisiert den Aufbau.
        /// </summary>
        /// <param name="scopeFactory">Für die datenbanknahen Befehle der Favoriten-Kacheln.</param>
        /// <param name="coverProvider">Beschafft die Serien-Cover.</param>
        /// <param name="confirmationDialogService">Fragt vor dem Entfernen eines Favoriten nach.</param>
        /// <param name="localizationService">Liefert die Anzeigetexte. Ohne ihn gelten die Vorgabewerte.</param>
        public DashboardSeriesSectionBuilder(
            IServiceScopeFactory scopeFactory,
            DashboardCoverProvider coverProvider,
            IConfirmationDialogService confirmationDialogService,
            ILocalizationService? localizationService)
        {
            _scopeFactory = scopeFactory;
            _coverProvider = coverProvider;
            _confirmationDialogService = confirmationDialogService;
            _localizationService = localizationService;
        }

        /// <summary>
        /// Baut „Weiterhören": favorisierte Serien mit mindestens einer gehörten und
        /// mindestens einer ungehörten Folge. Die Folgen aller Favoriten kommen in einer
        /// Abfrage, nicht je Serie einzeln.
        /// </summary>
        /// <param name="favoriteSeries">Die favorisierten Serien.</param>
        /// <param name="episodeService">Zugriff auf die Folgen.</param>
        /// <param name="stateByEpisodeId">Die Wiedergabestände nach Folgen-Kennung.</param>
        /// <returns>Die Kacheln in Anzeigereihenfolge.</returns>
        public async Task<IReadOnlyList<UnheardSeriesCardViewModel>> BuildUnheardSeriesAsync(
            IReadOnlyList<Series> favoriteSeries,
            IEpisodeDataService episodeService,
            IReadOnlyDictionary<Guid, PlaybackState> stateByEpisodeId)
        {
            IReadOnlyList<Episode> allFavoriteEpisodes =
                await episodeService.GetBySeriesIdsAsync([.. favoriteSeries.Select(s => s.Id)]);

            Dictionary<Guid, List<Episode>> episodesBySeriesId = GroupBySeries(allFavoriteEpisodes, favoriteSeries.Count);
            List<UnheardSeriesCardViewModel> unheardList = [];

            foreach (Series series in favoriteSeries)
            {
                if (!episodesBySeriesId.TryGetValue(series.Id, out List<Episode>? episodes))
                {
                    continue;
                }

                int completedCount = CountCompleted(episodes, stateByEpisodeId);
                int unheardCount = episodes.Count - completedCount;

                // Nur angefangene Serien: ohne gehörte Folge ist es kein „weiter", ohne
                // ungehörte Folge gibt es nichts mehr zu hören.
                if (completedCount > 0 && unheardCount > 0)
                {
                    BitmapImage? cover = await _coverProvider.BuildSeriesCoverAsync(series);
                    unheardList.Add(new UnheardSeriesCardViewModel(
                        series.Id, series.Title, cover, unheardCount, _localizationService));
                }
            }

            return unheardList;
        }

        /// <summary>
        /// Baut die Favoriten-Kacheln samt Cover und bringt sie in die gespeicherte
        /// Reihenfolge. Sie ist unabhängig von der der Neuerscheinungen — der Nutzer sortiert
        /// beide Abschnitte getrennt. Serien ohne gespeicherte Position hängen alphabetisch an.
        /// </summary>
        /// <param name="favoriteSeries">Die favorisierten Serien.</param>
        /// <param name="positionService">Zugriff auf die gespeicherten Reihenfolgen.</param>
        /// <returns>Die Kacheln in Anzeigereihenfolge.</returns>
        public async Task<IReadOnlyList<FavoriteSeriesCardViewModel>> BuildFavoriteCardsAsync(
            IReadOnlyList<Series> favoriteSeries,
            IDashboardPositionDataService positionService)
        {
            List<FavoriteSeriesCardViewModel> favoriteCards = [];

            foreach (Series series in favoriteSeries)
            {
                BitmapImage? cover = await _coverProvider.BuildSeriesCoverAsync(series);
                favoriteCards.Add(new FavoriteSeriesCardViewModel(
                    series.Id, series.Title, cover, _scopeFactory, _confirmationDialogService, _localizationService));
            }

            IReadOnlyList<DashboardPosition> favoritePositions = await positionService.GetBySectionAsync(SectionFavorites);
            Dictionary<Guid, int> positionBySeriesId = new(favoritePositions.Count);
            foreach (DashboardPosition dp in favoritePositions)
            {
                positionBySeriesId[dp.SeriesId] = dp.Position;
            }

            favoriteCards.Sort((a, b) => CompareByPosition(a, b, positionBySeriesId));
            return favoriteCards;
        }

        /// <summary>Ordnet die Folgen ihren Serien zu.</summary>
        private static Dictionary<Guid, List<Episode>> GroupBySeries(IReadOnlyList<Episode> episodes, int seriesCount)
        {
            Dictionary<Guid, List<Episode>> bySeriesId = new(seriesCount);

            foreach (Episode episode in episodes)
            {
                if (!bySeriesId.TryGetValue(episode.SeriesId, out List<Episode>? bucket))
                {
                    bucket = [];
                    bySeriesId[episode.SeriesId] = bucket;
                }

                bucket.Add(episode);
            }

            return bySeriesId;
        }

        /// <summary>Zählt, wie viele der Folgen vollständig gehört sind.</summary>
        private static int CountCompleted(List<Episode> episodes, IReadOnlyDictionary<Guid, PlaybackState> stateByEpisodeId)
        {
            int completed = 0;

            foreach (Episode episode in episodes)
            {
                if (stateByEpisodeId.TryGetValue(episode.Id, out PlaybackState? state) && state.IsCompleted)
                {
                    completed++;
                }
            }

            return completed;
        }

        /// <summary>
        /// Vergleicht zwei Kacheln: gespeicherte Position schlägt Titel, und eine Serie mit
        /// Position steht vor jeder ohne.
        /// </summary>
        private static int CompareByPosition(
            FavoriteSeriesCardViewModel a,
            FavoriteSeriesCardViewModel b,
            Dictionary<Guid, int> positionBySeriesId)
        {
            bool aHasPos = positionBySeriesId.TryGetValue(a.SeriesId, out int posA);
            bool bHasPos = positionBySeriesId.TryGetValue(b.SeriesId, out int posB);

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
        }
    }
}

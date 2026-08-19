using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die beiden Serien-Abschnitte der Startseite: „Weiterhören" und die Favoriten
    /// samt der vom Anwender gespeicherten Reihenfolge.
    /// </summary>
    /// <remarks>
    /// „Weiterhören" ist der Abschnitt, der am meisten falsch machen kann: Eine Serie ohne
    /// gehörte Folge ist kein Weiterhören, eine durchgehörte auch nicht. Steht dort das
    /// Falsche, ist der wichtigste Weg der Startseite entwertet.
    /// </remarks>
    public sealed class DashboardSeriesSectionBuilderTests
    {
        [Fact]
        public async Task BuildUnheardSeries_WithHeardAndUnheardEpisodes_ShowsTheSeries()
        {
            FakeEpisodeDataService episodes = new();
            Series series = new() { Title = "TKKG" };
            Episode heard = await AddEpisodeAsync(episodes, series.Id, "Folge 1");
            _ = await AddEpisodeAsync(episodes, series.Id, "Folge 2");

            DashboardSeriesSectionBuilder sut = Build();

            IReadOnlyList<UnheardSeriesCardViewModel> cards = await sut.BuildUnheardSeriesAsync(
                [series], episodes, Completed(heard.Id));

            UnheardSeriesCardViewModel card = Assert.Single(cards);
            Assert.Equal("TKKG", card.SeriesName);
            Assert.Equal(1, card.UnheardCount);
        }

        [Fact]
        public async Task BuildUnheardSeries_WithoutAnyHeardEpisode_SkipsTheSeries()
        {
            FakeEpisodeDataService episodes = new();
            Series series = new() { Title = "TKKG" };
            _ = await AddEpisodeAsync(episodes, series.Id, "Folge 1");

            DashboardSeriesSectionBuilder sut = Build();

            IReadOnlyList<UnheardSeriesCardViewModel> cards = await sut.BuildUnheardSeriesAsync(
                [series], episodes, new Dictionary<Guid, PlaybackState>());

            // Nichts gehört heißt nichts weiterzuhören — die Serie gehört in den Bestand,
            // nicht in diesen Abschnitt.
            Assert.Empty(cards);
        }

        [Fact]
        public async Task BuildUnheardSeries_WhenEverythingIsHeard_SkipsTheSeries()
        {
            FakeEpisodeDataService episodes = new();
            Series series = new() { Title = "TKKG" };
            Episode only = await AddEpisodeAsync(episodes, series.Id, "Folge 1");

            DashboardSeriesSectionBuilder sut = Build();

            IReadOnlyList<UnheardSeriesCardViewModel> cards = await sut.BuildUnheardSeriesAsync(
                [series], episodes, Completed(only.Id));

            Assert.Empty(cards);
        }

        [Fact]
        public async Task BuildUnheardSeries_WithoutEpisodes_SkipsTheSeries()
        {
            DashboardSeriesSectionBuilder sut = Build();

            IReadOnlyList<UnheardSeriesCardViewModel> cards = await sut.BuildUnheardSeriesAsync(
                [new Series { Title = "Leere Serie" }],
                new FakeEpisodeDataService(),
                new Dictionary<Guid, PlaybackState>());

            Assert.Empty(cards);
        }

        [Fact]
        public async Task BuildFavoriteCards_WithoutSavedOrder_SortsByTitle()
        {
            DashboardSeriesSectionBuilder sut = Build();

            IReadOnlyList<FavoriteSeriesCardViewModel> cards = await sut.BuildFavoriteCardsAsync(
                [new Series { Title = "TKKG" }, new Series { Title = "Bibi Blocksberg" }],
                new FakeDashboardPositionDataService());

            Assert.Equal(["Bibi Blocksberg", "TKKG"], [.. Titles(cards)]);
        }

        [Fact]
        public async Task BuildFavoriteCards_WithSavedOrder_FollowsIt()
        {
            FakeSeriesDataService store = new();
            Series first = await AddSeriesAsync(store, "Bibi Blocksberg");
            Series second = await AddSeriesAsync(store, "TKKG");

            FakeDashboardPositionDataService positions = new();
            await positions.SaveOrderAsync(
                "Favoriten", [second.Id, first.Id], TestContext.Current.CancellationToken);

            DashboardSeriesSectionBuilder sut = Build();

            IReadOnlyList<FavoriteSeriesCardViewModel> cards = await sut.BuildFavoriteCardsAsync(
                [first, second], positions);

            // Die selbst gelegte Reihenfolge schlägt die alphabetische — sonst wäre das
            // Sortieren per Ziehen und Ablegen beim nächsten Öffnen vergessen.
            Assert.Equal(["TKKG", "Bibi Blocksberg"], [.. Titles(cards)]);
        }

        [Fact]
        public async Task BuildFavoriteCards_WithPartialOrder_PutsPlacedSeriesFirst()
        {
            FakeSeriesDataService store = new();
            Series placed = await AddSeriesAsync(store, "TKKG");
            Series unplaced = await AddSeriesAsync(store, "Bibi Blocksberg");

            FakeDashboardPositionDataService positions = new();
            await positions.SaveOrderAsync(
                "Favoriten", [placed.Id], TestContext.Current.CancellationToken);

            DashboardSeriesSectionBuilder sut = Build();

            IReadOnlyList<FavoriteSeriesCardViewModel> cards = await sut.BuildFavoriteCardsAsync(
                [unplaced, placed], positions);

            // Eine neu hinzugekommene Serie hängt hinten an, statt die gelegte Reihenfolge
            // durcheinanderzubringen.
            Assert.Equal(["TKKG", "Bibi Blocksberg"], [.. Titles(cards)]);
        }

        [Fact]
        public async Task BuildFavoriteCards_WithoutFavorites_ReturnsNothing()
        {
            DashboardSeriesSectionBuilder sut = Build();

            IReadOnlyList<FavoriteSeriesCardViewModel> cards = await sut.BuildFavoriteCardsAsync(
                [], new FakeDashboardPositionDataService());

            Assert.Empty(cards);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static IEnumerable<string> Titles(IReadOnlyList<FavoriteSeriesCardViewModel> cards)
        {
            foreach (FavoriteSeriesCardViewModel card in cards)
            {
                yield return card.SeriesName;
            }
        }

        private static Dictionary<Guid, PlaybackState> Completed(Guid episodeId)
            => new() { [episodeId] = new PlaybackState { EpisodeId = episodeId, IsCompleted = true } };

        private static async Task<Series> AddSeriesAsync(FakeSeriesDataService store, string title)
        {
            Series series = new() { Title = title };
            await store.AddAsync(series, TestContext.Current.CancellationToken);
            return series;
        }

        private static async Task<Episode> AddEpisodeAsync(
            FakeEpisodeDataService episodes, Guid seriesId, string title)
        {
            Episode episode = new() { SeriesId = seriesId, Title = title };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);
            return episode;
        }

        private static DashboardSeriesSectionBuilder Build()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            DashboardCoverProvider coverProvider = new(
                scopeFactory, coverService: null, backgroundCoverService: null, dispatcherQueue: null);

            return new DashboardSeriesSectionBuilder(
                scopeFactory,
                coverProvider,
                new FakeConfirmationDialogService(),
                new FakeLocalizationService());
        }
    }
}

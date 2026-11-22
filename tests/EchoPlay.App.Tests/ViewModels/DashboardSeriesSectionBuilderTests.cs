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
    /// Prüft die beiden Serien-Abschnitte der Startseite: „Weiterhören" und die Favoriten.
    /// Die gespeicherte Reihenfolge prüft <see cref="DashboardSnapshotTests"/>.
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

            // Nichts gehört heißt nichts weiterzuhören; die Serie gehört in den Bestand,
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
        public async Task BuildFavoriteCards_KeepsTheGivenOrder()
        {
            DashboardSeriesSectionBuilder sut = Build();

            IReadOnlyList<FavoriteSeriesCardViewModel> cards = await sut.BuildFavoriteCardsAsync(
                [new Series { Title = "TKKG" }, new Series { Title = "Bibi Blocksberg" }]);

            // Die Reihenfolge legt DashboardSnapshot fest; ein zweites Sortieren hier liefe
            // auseinander, sobald sich eine der beiden Regeln ändert.
            Assert.Equal(["TKKG", "Bibi Blocksberg"], [.. Titles(cards)]);
        }

        [Fact]
        public async Task BuildFavoriteCards_WithoutFavorites_ReturnsNothing()
        {
            DashboardSeriesSectionBuilder sut = Build();

            IReadOnlyList<FavoriteSeriesCardViewModel> cards = await sut.BuildFavoriteCardsAsync([]);

            Assert.Empty(cards);
        }

        private static IEnumerable<string> Titles(IReadOnlyList<FavoriteSeriesCardViewModel> cards)
        {
            foreach (FavoriteSeriesCardViewModel card in cards)
            {
                yield return card.SeriesName;
            }
        }

        private static Dictionary<Guid, PlaybackState> Completed(Guid episodeId)
            => new() { [episodeId] = new PlaybackState { EpisodeId = episodeId, IsCompleted = true } };

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

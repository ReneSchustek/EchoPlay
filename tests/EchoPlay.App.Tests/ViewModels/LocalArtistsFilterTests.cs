using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System.Reflection;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Sichert Suche und Filter der lokalen Mediathek ab, samt der Unterscheidung, ob gar
    /// nichts da ist oder ob nur die Suche nichts findet. Beide Fälle sehen gleich leer aus
    /// und verlangen vom Nutzer Gegenteiliges.
    /// </summary>
    public sealed class LocalArtistsFilterTests
    {
        private static LocalArtistsViewModel BuildViewModel(out FakeEpisodeDataService episodeService)
            => BuildViewModel(out episodeService, out _);

        private static LocalArtistsViewModel BuildViewModel(out FakeEpisodeDataService episodeService, out FakeSeriesDataService seriesService)
        {
            episodeService = new FakeEpisodeDataService();
            seriesService = new FakeSeriesDataService();

            ServiceCollection services = new();
            IEpisodeDataService episodes = episodeService;
            ISeriesDataService series = seriesService;
            _ = services.AddScoped<IEpisodeDataService>(_ => episodes);
            _ = services.AddScoped<ISeriesDataService>(_ => series);
            ServiceProvider provider = services.BuildServiceProvider();

            return new LocalArtistsViewModel(provider.GetRequiredService<IServiceScopeFactory>());
        }

        private static Series MakeSeries(Guid id, string title, bool isFavorite = false, bool isWatched = false)
        {
            Series series = new()
            {
                Title = title,
                LocalFolderPath = $@"C:\Serie\{title}",
                IsFavorite = isFavorite,
                IsWatched = isWatched
            };

            // Id hat nur einen protected Setter; im Test deterministisch per Reflection setzen.
            PropertyInfo idProp = typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!;
            idProp.SetValue(series, id);
            return series;
        }

        private static Episode MakeEpisode(Guid seriesId, int number, bool local) => new()
        {
            SeriesId = seriesId,
            Title = $"Folge {number}",
            EpisodeNumber = number,
            LocalFolderPath = local ? $@"C:\Serie\Folge{number}" : null
        };

        /// <summary>
        /// Legt drei Serien an: „Bibi" (Favorit, vollständig), „Benjamin" (überwacht, Lücke bei
        /// Folge 2) und „TKKG" (schlicht, vollständig), und lädt die Mediathek wie die App.
        /// </summary>
        private static async Task<LocalArtistsViewModel> BuildLibraryAsync()
        {
            LocalArtistsViewModel vm = BuildViewModel(out FakeEpisodeDataService episodes, out FakeSeriesDataService seriesService);

            Series bibi = MakeSeries(TestIds.SeriesA, "Bibi Blocksberg", isFavorite: true);
            Series benjamin = MakeSeries(TestIds.SeriesB, "Benjamin Blümchen", isWatched: true);
            Series tkkg = MakeSeries(TestIds.SeriesC, "TKKG");

            // Der Fake vergibt beim Anlegen eigene Kennungen; die Folgen kommen deshalb danach.
            foreach (Series series in new[] { bibi, benjamin, tkkg })
            {
                await seriesService.AddAsync(series, TestContext.Current.CancellationToken);
            }

            await episodes.AddRangeAsync(
                [MakeEpisode(bibi.Id, 1, local: true), MakeEpisode(bibi.Id, 2, local: true)], TestContext.Current.CancellationToken);
            await episodes.AddRangeAsync(
                [MakeEpisode(benjamin.Id, 1, local: true), MakeEpisode(benjamin.Id, 3, local: true)],
                TestContext.Current.CancellationToken);
            await episodes.AddRangeAsync(
                [MakeEpisode(tkkg.Id, 1, local: true)], TestContext.Current.CancellationToken);

            await vm.LoadFromDatabaseAsync();
            return vm;
        }

        private static IEnumerable<string> TitlesOf(LocalArtistsViewModel vm) =>
            vm.Artists.Select(a => a.Title);

        [Fact]
        public async Task Search_MatchesTitlesRegardlessOfCase()
        {
            LocalArtistsViewModel vm = await BuildLibraryAsync();

            vm.LocalSearchText = "bibi";

            Assert.Equal(["Bibi Blocksberg"], TitlesOf(vm));
        }

        [Fact]
        public async Task FavoritesOnly_KeepsOnlyFavourites()
        {
            LocalArtistsViewModel vm = await BuildLibraryAsync();

            vm.FavoritesOnly = true;

            Assert.Equal(["Bibi Blocksberg"], TitlesOf(vm));
        }

        [Fact]
        public async Task WatchedOnly_KeepsOnlyWatchedSeries()
        {
            LocalArtistsViewModel vm = await BuildLibraryAsync();

            vm.WatchedOnly = true;

            Assert.Equal(["Benjamin Blümchen"], TitlesOf(vm));
        }

        /// <summary>
        /// Unvollständig heißt: eine Lücke in der Nummernfolge oder eine neue Folge beim Anbieter.
        /// Eine Serie ganz ohne bekannte Folgen zählt nicht dazu, denn dort fehlt die Auskunft,
        /// nicht der Bestand.
        /// </summary>
        [Fact]
        public async Task IncompleteOnly_KeepsSeriesWithMissingEpisodes()
        {
            LocalArtistsViewModel vm = await BuildLibraryAsync();

            vm.IncompleteOnly = true;

            Assert.Equal(["Benjamin Blümchen"], TitlesOf(vm));
        }

        [Fact]
        public async Task SeriesWithoutKnownEpisodes_IsNotCountedAsIncomplete()
        {
            LocalArtistsViewModel vm = BuildViewModel(out _, out FakeSeriesDataService seriesService);
            await seriesService.AddAsync(MakeSeries(TestIds.SeriesA, "Ohne Folgen"), TestContext.Current.CancellationToken);
            await vm.LoadFromDatabaseAsync();

            vm.IncompleteOnly = true;

            Assert.Empty(vm.Artists);
        }

        /// <summary>
        /// Mehrere Filter suchen die Schnittmenge, nicht die Vereinigung: Wer „Favoriten" und
        /// „Überwacht" wählt, will beides zugleich.
        /// </summary>
        [Fact]
        public async Task SeveralFilters_NarrowDownTogether()
        {
            LocalArtistsViewModel vm = await BuildLibraryAsync();

            vm.FavoritesOnly = true;
            vm.WatchedOnly = true;

            Assert.Empty(vm.Artists);
        }

        [Fact]
        public async Task SearchAndFilter_ApplyTogether()
        {
            LocalArtistsViewModel vm = await BuildLibraryAsync();

            vm.LocalSearchText = "B";
            vm.IncompleteOnly = true;

            Assert.Equal(["Benjamin Blümchen"], TitlesOf(vm));
        }

        /// <summary>
        /// Der Kern der Unterscheidung: Bei gefülltem Bestand ohne Treffer erscheint der
        /// „Nichts gefunden"-Hinweis, nicht der Hinweis auf die leere Bibliothek.
        /// </summary>
        [Fact]
        public async Task FilterWithoutMatches_ShowsNoResultsInsteadOfEmptyLibrary()
        {
            LocalArtistsViewModel vm = await BuildLibraryAsync();

            vm.LocalSearchText = "Gibt es nicht";

            Assert.Equal(Visibility.Visible, vm.NoResultsVisibility);
            Assert.Equal(Visibility.Collapsed, vm.ArtistsEmptyVisibility);
        }

        [Fact]
        public void EmptyLibrary_ShowsEmptyHintAndNotTheNoResultsHint()
        {
            LocalArtistsViewModel vm = BuildViewModel(out _);

            Assert.Equal(Visibility.Visible, vm.ArtistsEmptyVisibility);
            Assert.Equal(Visibility.Collapsed, vm.NoResultsVisibility);
        }

        [Fact]
        public async Task ResetFilters_BringsBackTheWholeLibrary()
        {
            LocalArtistsViewModel vm = await BuildLibraryAsync();
            vm.LocalSearchText = "Bibi";
            vm.FavoritesOnly = true;
            vm.IncompleteOnly = true;

            vm.ResetFilters();

            Assert.Equal(3, vm.Artists.Count);
            Assert.False(vm.HasActiveFilter);
            Assert.Equal(string.Empty, vm.LocalSearchText);
            Assert.False(vm.FavoritesOnly);
            Assert.False(vm.IncompleteOnly);
        }

        [Fact]
        public async Task HasActiveFilter_ReportsWhetherTheViewIsNarrowed()
        {
            LocalArtistsViewModel vm = await BuildLibraryAsync();
            Assert.False(vm.HasActiveFilter);

            vm.WatchedOnly = true;

            Assert.True(vm.HasActiveFilter);
        }
    }
}

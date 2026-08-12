using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Sichert Suche und Filter der lokalen Mediathek ab — samt der Unterscheidung, ob gar
    /// nichts da ist oder ob nur die Suche nichts findet. Beide Fälle sehen gleich leer aus
    /// und verlangen vom Nutzer Gegenteiliges.
    /// </summary>
    public sealed class LocalArtistsFilterTests
    {
        private static LocalArtistsViewModel BuildViewModel(out FakeEpisodeDataService episodeService)
        {
            episodeService = new FakeEpisodeDataService();

            ServiceCollection services = new();
            IEpisodeDataService episodes = episodeService;
            _ = services.AddScoped<IEpisodeDataService>(_ => episodes);
            ServiceProvider provider = services.BuildServiceProvider();

            return new LocalArtistsViewModel(provider.GetRequiredService<IServiceScopeFactory>());
        }

        private static Series MakeSeries(Guid id, string title, bool isFavorite = false, bool isWatched = false)
        {
            Series series = new()
            {
                Title = title,
                LocalFolderPath = null,
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
        /// Legt drei Serien an: „Bibi" (Favorit, vollständig), „Benjamin" (überwacht,
        /// unvollständig) und „TKKG" (schlicht, vollständig).
        /// </summary>
        private static async Task<LocalArtistsViewModel> BuildLibraryAsync()
        {
            LocalArtistsViewModel vm = BuildViewModel(out FakeEpisodeDataService episodes);

            Series bibi = MakeSeries(TestIds.SeriesA, "Bibi Blocksberg", isFavorite: true);
            Series benjamin = MakeSeries(TestIds.SeriesB, "Benjamin Blümchen", isWatched: true);
            Series tkkg = MakeSeries(TestIds.SeriesC, "TKKG");

            await episodes.AddRangeAsync(
                [MakeEpisode(bibi.Id, 1, local: true)], TestContext.Current.CancellationToken);
            await episodes.AddRangeAsync(
                [MakeEpisode(benjamin.Id, 1, local: true), MakeEpisode(benjamin.Id, 2, local: false)],
                TestContext.Current.CancellationToken);
            await episodes.AddRangeAsync(
                [MakeEpisode(tkkg.Id, 1, local: true)], TestContext.Current.CancellationToken);

            await vm.AppendArtistCardAsync(bibi);
            await vm.AppendArtistCardAsync(benjamin);
            await vm.AppendArtistCardAsync(tkkg);

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
        /// Unvollständig heißt: Es sind Folgen bekannt, von denen welche fehlen. Eine Serie
        /// ganz ohne bekannte Folgen zählt nicht dazu — dort fehlt die Auskunft, nicht der
        /// Bestand.
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
            LocalArtistsViewModel vm = BuildViewModel(out _);
            await vm.AppendArtistCardAsync(MakeSeries(TestIds.SeriesA, "Ohne Folgen"));

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

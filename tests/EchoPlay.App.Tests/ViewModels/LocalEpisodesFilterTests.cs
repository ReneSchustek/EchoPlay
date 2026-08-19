using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft Reiter, Filter und Sortierung der Folgenliste in der lokalen Mediathek — und
    /// was die Liste zeigt, solange nichts geladen ist.
    /// </summary>
    /// <remarks>
    /// Reiter, Filter und Sortierung greifen auf denselben Bestand. Wirkt eine der drei
    /// Einstellungen nicht sofort, sieht der Anwender eine Liste, die zu seiner Auswahl
    /// nicht passt — und hält die Filter für kaputt.
    /// </remarks>
    public sealed class LocalEpisodesFilterTests
    {
        [Fact]
        public async Task EmptyList_ShowsThePlaceholderAndHidesTheSortBox()
        {
            using LocalEpisodesViewModel sut = await LoadAsync();

            Assert.Equal(Visibility.Visible, sut.EpisodesEmptyVisibility);
            Assert.Equal(Visibility.Collapsed, sut.EpisodesLoadedVisibility);
        }

        [Fact]
        public async Task WithEpisodes_ShowsTheSortBoxInsteadOfThePlaceholder()
        {
            using LocalEpisodesViewModel sut = await LoadAsync(Episode(1, "Der Fall des Jahres"));

            Assert.Equal(Visibility.Collapsed, sut.EpisodesEmptyVisibility);
            Assert.Equal(Visibility.Visible, sut.EpisodesLoadedVisibility);
        }

        [Fact]
        public async Task EpisodeSortIndex_WhenChanged_ReordersTheListAtOnce()
        {
            using LocalEpisodesViewModel sut = await LoadAsync(
                Episode(1, "Erste"), Episode(2, "Zweite"), Episode(3, "Dritte"));

            sut.EpisodeSortIndex = 1;

            Assert.Equal(1, sut.EpisodeSortIndex);
            Assert.Equal(3, sut.Episodes[0].EpisodeNumber);
        }

        [Fact]
        public async Task EpisodeFilterIndex_ForHeardEpisodes_ShowsOnlyThose()
        {
            Episode heard = Episode(1, "Erste");
            Episode unheard = Episode(2, "Zweite");
            using LocalEpisodesViewModel sut = await LoadAsync([heard, unheard], completed: [heard]);

            sut.EpisodeFilterIndex = 2;

            LocalEpisodeCardViewModel only = Assert.Single(sut.Episodes);
            Assert.Equal(2, sut.EpisodeFilterIndex);
            Assert.Equal(1, only.EpisodeNumber);
        }

        [Fact]
        public async Task EpisodeFilterIndex_ForUnheardEpisodes_LeavesOutStartedOnes()
        {
            Episode heard = Episode(1, "Erste");
            Episode started = Episode(2, "Zweite");
            Episode untouched = Episode(3, "Dritte");
            using LocalEpisodesViewModel sut = await LoadAsync(
                [heard, started, untouched], completed: [heard], inProgress: [started]);

            sut.EpisodeFilterIndex = 1;

            // „Ungehört" heißt: noch nie angefangen. Eine angefangene Folge gehört in den
            // eigenen Filter, sonst findet der Anwender sie zweimal.
            LocalEpisodeCardViewModel only = Assert.Single(sut.Episodes);
            Assert.Equal(3, only.EpisodeNumber);
        }

        [Fact]
        public async Task EpisodeFilterIndex_ForStartedEpisodes_ShowsOnlyThose()
        {
            Episode started = Episode(2, "Zweite");
            using LocalEpisodesViewModel sut = await LoadAsync(
                [Episode(1, "Erste"), started], inProgress: [started]);

            sut.EpisodeFilterIndex = 3;

            LocalEpisodeCardViewModel only = Assert.Single(sut.Episodes);
            Assert.Equal(2, only.EpisodeNumber);
        }

        [Fact]
        public async Task EpisodeTabIndex_WhenChanged_ReappliesFilterAndSorting()
        {
            using LocalEpisodesViewModel sut = await LoadAsync(
                Episode(1, "Erste"), Episode(null, "Sonderfolge"));

            sut.EpisodeTabIndex = 1;

            // Der zweite Reiter zeigt die Sonderfolgen — sie tragen keine Nummer.
            Assert.Equal(1, sut.EpisodeTabIndex);
            LocalEpisodeCardViewModel only = Assert.Single(sut.Episodes);
            Assert.Null(only.EpisodeNumber);
        }

        [Fact]
        public async Task Clear_EmptiesTheListAndShowsThePlaceholderAgain()
        {
            using LocalEpisodesViewModel sut = await LoadAsync(Episode(1, "Erste"));

            sut.Clear();

            Assert.Empty(sut.Episodes);
            Assert.Equal(Visibility.Visible, sut.EpisodesEmptyVisibility);
        }

        [Fact]
        public async Task LoadForSeries_TwiceInARow_ReplacesTheList()
        {
            ServiceProvider provider = BuildProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            using LocalEpisodesViewModel sut = new(scopeFactory, new FakeLocalCoverLoader(), new FakeClock());

            await sut.LoadForSeriesAsync(BuildArtist(scopeFactory), [Episode(1, "Erste")], [], []);
            await sut.LoadForSeriesAsync(BuildArtist(scopeFactory), [Episode(2, "Zweite")], [], []);

            // Beim Serienwechsel bricht das Nachladen der Cover der vorigen Serie ab —
            // sonst landeten deren Bilder auf den neuen Kacheln.
            LocalEpisodeCardViewModel only = Assert.Single(sut.Episodes);
            Assert.Equal(2, only.EpisodeNumber);
        }

        [Fact]
        public async Task LoadForSeries_WithoutArtist_ThrowsArgumentNullException()
        {
            ServiceProvider provider = BuildProvider();
            using LocalEpisodesViewModel sut = new(
                provider.GetRequiredService<IServiceScopeFactory>(), new FakeLocalCoverLoader(), new FakeClock());

            _ = await Assert.ThrowsAsync<ArgumentNullException>(
                () => sut.LoadForSeriesAsync(null!, [], [], []));
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static ServiceProvider BuildProvider()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            return services.BuildServiceProvider();
        }

        private static LocalArtistCardViewModel BuildArtist(IServiceScopeFactory scopeFactory) => new(
            seriesId: TestIds.SeriesA,
            title: "TKKG",
            coverImage: null,
            localFolderPath: @"C:\Hörspiele\TKKG",
            localEpisodeCount: 0,
            totalEpisodeCount: 0,
            isFavorite: false,
            isWatched: false,
            scopeFactory: scopeFactory);

        private static Episode Episode(int? number, string title) => new()
        {
            SeriesId = TestIds.SeriesA,
            EpisodeNumber = number,
            Title = title,
            LocalFolderPath = $@"C:\Hörspiele\TKKG\{title}",
        };

        private static Task<LocalEpisodesViewModel> LoadAsync(params Episode[] episodes)
            => LoadAsync(episodes, [], []);

        private static async Task<LocalEpisodesViewModel> LoadAsync(
            IReadOnlyList<Episode> episodes,
            IReadOnlyList<Episode>? completed = null,
            IReadOnlyList<Episode>? inProgress = null)
        {
            // Die Kennungen vergibt erst die Ablage. Ohne sie trügen alle Folgen dieselbe
            // leere Kennung, und die Filter könnten sie nicht auseinanderhalten.
            FakeEpisodeDataService idSource = new();
            foreach (Episode episode in episodes)
            {
                await idSource.AddAsync(episode, TestContext.Current.CancellationToken);
            }

            ServiceProvider provider = BuildProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            LocalEpisodesViewModel viewModel = new(scopeFactory, new FakeLocalCoverLoader(), new FakeClock());

            await viewModel.LoadForSeriesAsync(
                BuildArtist(scopeFactory),
                episodes,
                completedIds: ToIdSet(completed),
                inProgressIds: ToIdSet(inProgress));

            return viewModel;
        }

        private static HashSet<Guid> ToIdSet(IReadOnlyList<Episode>? episodes)
        {
            HashSet<Guid> ids = [];
            foreach (Episode episode in episodes ?? [])
            {
                _ = ids.Add(episode.Id);
            }
            return ids;
        }
    }
}

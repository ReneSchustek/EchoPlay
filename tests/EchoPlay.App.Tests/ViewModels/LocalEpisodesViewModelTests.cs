using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="LocalEpisodesViewModel"/> – die mittlere Spalte der lokalen
    /// Mediathek. Geprüft werden Laden, Filtern, Sortieren und die Trennung von regulären
    /// Folgen und Sonderfolgen. Das Nachladen der Cover erzeugt <c>BitmapImage</c>-Objekte
    /// und braucht einen UI-Thread; es bleibt dem Klick-Test am laufenden Programm vorbehalten.
    /// </summary>
    public sealed class LocalEpisodesViewModelTests
    {
        private static LocalEpisodesViewModel BuildViewModel()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            return new LocalEpisodesViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeLocalCoverLoader(),
                new FakeClock());
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

        /// <summary>
        /// Baut eine Folge für die Testdaten. Eine Folge ohne Nummer gilt im ViewModel als
        /// Sonderfolge – das Kennzeichen steht nicht in der Datenbank, sondern wird aus der
        /// fehlenden Nummer abgeleitet.
        /// </summary>
        private static Episode Episode(int? number, string title) => new()
        {
            SeriesId = TestIds.SeriesA,
            EpisodeNumber = number,
            Title = title,
            LocalFolderPath = $@"C:\Hörspiele\TKKG\{title}"
        };

        private static async Task<LocalEpisodesViewModel> LoadAsync(
            params Episode[] episodes)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            LocalEpisodesViewModel viewModel = new(
                scopeFactory,
                new FakeLocalCoverLoader(),
                new FakeClock());

            await viewModel.LoadForSeriesAsync(
                BuildArtist(scopeFactory),
                episodes,
                completedIds: [],
                inProgressIds: []);

            return viewModel;
        }

        [Fact]
        public async Task LoadForSeriesAsync_CreatesOneCardPerEpisode()
        {
            using LocalEpisodesViewModel viewModel = await LoadAsync(
                Episode(1, "Der Fall des Jahres"),
                Episode(2, "Die Nacht der Fledermäuse"));

            Assert.Equal(2, viewModel.Episodes.Count);
        }

        [Fact]
        public async Task LoadForSeriesAsync_SkipsEpisodesWithoutLocalFolder()
        {
            // Ohne lokalen Ordner gibt es keine abspielbare Datei – solche Folgen
            // gehören nicht in die lokale Mediathek.
            Episode withoutFolder = Episode(3, "Nur online");
            withoutFolder.LocalFolderPath = null;

            using LocalEpisodesViewModel viewModel = await LoadAsync(
                Episode(1, "Lokal vorhanden"),
                withoutFolder);

            _ = Assert.Single(viewModel.Episodes);
        }

        [Fact]
        public async Task LoadForSeriesAsync_SortsByEpisodeNumberAscending()
        {
            using LocalEpisodesViewModel viewModel = await LoadAsync(
                Episode(3, "Drei"),
                Episode(1, "Eins"),
                Episode(2, "Zwei"));

            Assert.Equal(1, viewModel.Episodes[0].EpisodeNumber);
            Assert.Equal(3, viewModel.Episodes[2].EpisodeNumber);
        }

        [Fact]
        public async Task EpisodeSortIndex_Descending_ReversesOrder()
        {
            using LocalEpisodesViewModel viewModel = await LoadAsync(
                Episode(1, "Eins"),
                Episode(2, "Zwei"),
                Episode(3, "Drei"));

            viewModel.EpisodeSortIndex = 1;

            Assert.Equal(3, viewModel.Episodes[0].EpisodeNumber);
        }

        [Fact]
        public async Task EpisodeSortIndex_ByTitle_SortsAlphabetically()
        {
            using LocalEpisodesViewModel viewModel = await LoadAsync(
                Episode(1, "Zebra"),
                Episode(2, "Anfang"));

            viewModel.EpisodeSortIndex = 2;

            Assert.Equal("Anfang", viewModel.Episodes[0].Title);
        }

        [Fact]
        public async Task EpisodeTabIndex_SpecialTab_ShowsOnlySpecialEpisodes()
        {
            using LocalEpisodesViewModel viewModel = await LoadAsync(
                Episode(1, "Reguläre Folge"),
                Episode(null, "Jubiläumsfolge"));

            // Standard-Tab zeigt nur reguläre Folgen …
            _ = Assert.Single(viewModel.Episodes);

            // … der zweite Tab nur die Sonderfolgen.
            viewModel.EpisodeTabIndex = 1;

            LocalEpisodeCardViewModel special = Assert.Single(viewModel.Episodes);
            Assert.Equal("Jubiläumsfolge", special.Title);
        }

        [Fact]
        public async Task HasSpecialEpisodes_ReflectsLoadedData()
        {
            // Steuert, ob der Sonderfolgen-Tab überhaupt erscheint.
            using LocalEpisodesViewModel withSpecial = await LoadAsync(
                Episode(1, "Regulär"),
                Episode(null, "Sonderfolge"));

            Assert.True(withSpecial.HasSpecialEpisodes);
            Assert.Equal(1, withSpecial.SpecialEpisodeCount);

            using LocalEpisodesViewModel withoutSpecial = await LoadAsync(Episode(1, "Regulär"));

            Assert.False(withoutSpecial.HasSpecialEpisodes);
        }

        [Fact]
        public async Task EpisodeFilterIndex_Unplayed_ShowsEpisodesWithoutProgress()
        {
            using LocalEpisodesViewModel viewModel = await LoadAsync(
                Episode(1, "Eins"),
                Episode(2, "Zwei"));

            viewModel.EpisodeFilterIndex = 1;

            // Ohne Wiedergabestatus gelten beide Folgen als ungehört.
            Assert.Equal(2, viewModel.Episodes.Count);
        }

        [Fact]
        public async Task EpisodeFilterIndex_Completed_HidesEpisodesWithoutProgress()
        {
            using LocalEpisodesViewModel viewModel = await LoadAsync(
                Episode(1, "Eins"),
                Episode(2, "Zwei"));

            viewModel.EpisodeFilterIndex = 2;

            Assert.Empty(viewModel.Episodes);
        }

        [Fact]
        public async Task Clear_EmptiesTheList()
        {
            // Beim Serienwechsel dürfen keine Kacheln der vorherigen Serie stehen bleiben.
            using LocalEpisodesViewModel viewModel = await LoadAsync(Episode(1, "Eins"));

            viewModel.Clear();

            Assert.Empty(viewModel.Episodes);
            Assert.False(viewModel.HasSpecialEpisodes);
        }

        [Fact]
        public async Task ApplyFilterAndSort_NotifiesTheView()
        {
            using LocalEpisodesViewModel viewModel = await LoadAsync(Episode(1, "Eins"));

            List<string> changed = [];
            viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

            viewModel.ApplyFilterAndSort();

            Assert.Contains(nameof(LocalEpisodesViewModel.Episodes), changed);
        }

        [Fact]
        public void Dispose_CalledTwice_IsHarmless()
        {
            LocalEpisodesViewModel viewModel = BuildViewModel();

            viewModel.Dispose();
            viewModel.Dispose();
        }
    }
}

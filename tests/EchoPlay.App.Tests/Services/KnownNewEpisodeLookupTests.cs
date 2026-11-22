using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft, welche neuen Folgen <see cref="KnownNewEpisodeLookup"/> aus der Datenbank meldet:
    /// nur die des Gegenstücks beim Anbieter, nur nach der letzten lokalen Nummer und nur die
    /// ohne lokale Dateien.
    /// </summary>
    public sealed class KnownNewEpisodeLookupTests
    {
        private const string AppleMusicId = "329049924";
        private const string SpotifyId = "sp_ausrufezeichen";

        [Fact]
        public async Task FindAfterAsync_WithAppleMusicTwin_ReturnsEpisodesAfterTheLocalHighest()
        {
            Setup setup = new();
            Series local = await setup.AddLocalAsync(appleMusicId: AppleMusicId);
            Series twin = await setup.AddTwinAsync(appleMusicId: AppleMusicId);
            await setup.AddEpisodesAsync(twin, (88, "Folge 88"), (89, "Folge 89: Der Fluch der Fee"), (100, "Folge 100: Detektivinnen in Gefahr"));

            IReadOnlyList<OnlineEpisodeInfo> result = await setup.Lookup.FindAfterAsync(local, 88, TestContext.Current.CancellationToken);

            Assert.Equal([89, 100], result.Select(episode => episode.EpisodeNumber));
            Assert.Equal("Folge 89: Der Fluch der Fee", result[0].Title);
        }

        [Fact]
        public async Task FindAfterAsync_WithoutAppleMusicTwin_FallsBackToSpotify()
        {
            Setup setup = new();
            Series local = await setup.AddLocalAsync(appleMusicId: AppleMusicId, spotifyId: SpotifyId);
            Series twin = await setup.AddTwinAsync(spotifyId: SpotifyId);
            await setup.AddEpisodesAsync(twin, (5, "Folge 5"));

            IReadOnlyList<OnlineEpisodeInfo> result = await setup.Lookup.FindAfterAsync(local, 4, TestContext.Current.CancellationToken);

            Assert.Equal(5, Assert.Single(result).EpisodeNumber);
        }

        [Fact]
        public async Task FindAfterAsync_WithoutProviderIds_ReturnsNothing()
        {
            Setup setup = new();
            Series local = await setup.AddLocalAsync();

            IReadOnlyList<OnlineEpisodeInfo> result = await setup.Lookup.FindAfterAsync(local, 4, TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }

        [Fact]
        public async Task FindAfterAsync_SkipsEpisodesWithLocalFilesAndWithoutNumber()
        {
            // Ist die lokale Serie selbst der Online-Import, stehen ihre Dateien in derselben Liste.
            Setup setup = new();
            Series twin = await setup.AddTwinAsync(appleMusicId: AppleMusicId);
            await setup.AddEpisodesAsync(twin, (11, "Folge 11"));
            await setup.Episodes.AddAsync(
                new Episode { SeriesId = twin.Id, EpisodeNumber = 12, Title = "Folge 12", LocalTrackCount = 3 },
                TestContext.Current.CancellationToken);
            await setup.Episodes.AddAsync(
                new Episode { SeriesId = twin.Id, EpisodeNumber = null, Title = "Sonderfolge" },
                TestContext.Current.CancellationToken);

            IReadOnlyList<OnlineEpisodeInfo> result = await setup.Lookup.FindAfterAsync(twin, 10, TestContext.Current.CancellationToken);

            Assert.Equal(11, Assert.Single(result).EpisodeNumber);
        }

        [Fact]
        public async Task FindAfterAsync_NumberTakenTwice_ListsItOnce()
        {
            // Eine Nummer kann doppelt belegt sein, etwa Kinofilm-Hörspiel neben der regulären Folge.
            Setup setup = new();
            Series local = await setup.AddLocalAsync(appleMusicId: AppleMusicId);
            Series twin = await setup.AddTwinAsync(appleMusicId: AppleMusicId);
            await setup.AddEpisodesAsync(twin, (90, "Folge 90: Mission Gipfelglück"), (90, "Das Original Hörspiel zum Kinofilm"));

            IReadOnlyList<OnlineEpisodeInfo> result = await setup.Lookup.FindAfterAsync(local, 89, TestContext.Current.CancellationToken);

            Assert.Equal(90, Assert.Single(result).EpisodeNumber);
        }

        [Fact]
        public async Task FindAfterAsync_WithoutSeries_Throws()
        {
            Setup setup = new();

            _ = await Assert.ThrowsAsync<ArgumentNullException>(
                () => setup.Lookup.FindAfterAsync(null!, 1, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void Merge_SameNumberInBothSources_KeepsTheKnownTitle()
        {
            IReadOnlyList<OnlineEpisodeInfo> known = [Info(2, "Folge 2: Zwei")];
            IReadOnlyList<OnlineEpisodeInfo> live = [Info(3, "Album 3"), Info(2, "Album 2")];

            IReadOnlyList<OnlineEpisodeInfo> result = KnownNewEpisodeLookup.Merge(known, live);

            Assert.Equal([2, 3], result.Select(episode => episode.EpisodeNumber));
            Assert.Equal("Folge 2: Zwei", result[0].Title);
        }

        [Fact]
        public void Merge_BothEmpty_ReturnsEmpty()
        {
            Assert.Empty(KnownNewEpisodeLookup.Merge([], []));
        }

        private static OnlineEpisodeInfo Info(int number, string title) => new() { EpisodeNumber = number, Title = title };

        /// <summary>Datendienste und Suche eines Testfalls.</summary>
        private sealed class Setup
        {
            public Setup()
            {
                Lookup = new KnownNewEpisodeLookup(Series, Episodes);
            }

            public FakeSeriesDataService Series { get; } = new();

            public FakeEpisodeDataService Episodes { get; } = new();

            public KnownNewEpisodeLookup Lookup { get; }

            public async Task<Series> AddLocalAsync(string? appleMusicId = null, string? spotifyId = null)
            {
                Series series = new()
                {
                    Title = "Die drei Ausrufezeichen",
                    LocalFolderPath = @"D:\Hörspiele\Die drei Ausrufezeichen",
                    AppleMusicArtistId = appleMusicId,
                    SpotifyArtistId = spotifyId,
                };
                await Series.AddAsync(series, TestContext.Current.CancellationToken);
                return series;
            }

            public async Task<Series> AddTwinAsync(string? appleMusicId = null, string? spotifyId = null)
            {
                Series series = new()
                {
                    Title = "Die drei !!!",
                    AppleMusicArtistId = appleMusicId,
                    SpotifyArtistId = spotifyId,
                    IsOnlineImported = true,
                };
                await Series.AddAsync(series, TestContext.Current.CancellationToken);
                return series;
            }

            public async Task AddEpisodesAsync(Series series, params (int Number, string Title)[] episodes)
            {
                foreach ((int number, string title) in episodes)
                {
                    await Episodes.AddAsync(
                        new Episode { SeriesId = series.Id, EpisodeNumber = number, Title = title },
                        TestContext.Current.CancellationToken);
                }
            }
        }
    }
}

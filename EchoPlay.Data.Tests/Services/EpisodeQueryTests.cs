using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für Abfrage-Methoden von <see cref="EpisodeDataService"/>.
    /// </summary>
    public sealed class EpisodeQueryTests : DbTestBase
    {
        [Fact]
        public async Task GetBySeriesIdAsync_ReturnsEpisodesForSeries()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            _ = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            _ = await DataBuilder.PersistEpisodeAsync(series, "Folge 2");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            IReadOnlyList<Episode> result = await service.GetBySeriesIdAsync(series.Id);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task GetBySeriesIdAsync_ReturnsEmpty_WhenNoEpisodes()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Leere Serie");
            EpisodeDataService service = new(Context, NullLoggerFactory);

            IReadOnlyList<Episode> result = await service.GetBySeriesIdAsync(series.Id);

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsEpisode_WhenExists()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            Episode? result = await service.GetByIdAsync(episode.Id);

            Assert.NotNull(result);
            Assert.Equal("Folge 1", result.Title);
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
        {
            EpisodeDataService service = new(Context, NullLoggerFactory);

            Episode? result = await service.GetByIdAsync(new Guid("99999999-9999-9999-9999-999999999996"));

            Assert.Null(result);
        }

        [Fact]
        public async Task GetBySeriesIdsAsync_ReturnsCombinedResults()
        {
            Series seriesA = await DataBuilder.PersistSeriesAsync("Serie A");
            Series seriesB = await DataBuilder.PersistSeriesAsync("Serie B");
            _ = await DataBuilder.PersistEpisodeAsync(seriesA, "A-Folge");
            _ = await DataBuilder.PersistEpisodeAsync(seriesB, "B-Folge");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            IReadOnlyList<Episode> result = await service.GetBySeriesIdsAsync([seriesA.Id, seriesB.Id]);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task UpdateAsync_PersistsChanges()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Alt");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            Episode? loaded = await service.GetByIdAsync(episode.Id);
            loaded!.Title = "Neu";
            await service.UpdateAsync(loaded);
            Context.ChangeTracker.Clear();

            Episode? reloaded = await service.GetByIdAsync(episode.Id);
            Assert.Equal("Neu", reloaded!.Title);
        }

        [Fact]
        public async Task GetHighestLocalEpisodeNumberAsync_ReturnsMax()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");

            Episode ep1 = new() { SeriesId = series.Id, Title = "F1", EpisodeNumber = 10, LocalFolderPath = @"C:\1" };
            Episode ep2 = new() { SeriesId = series.Id, Title = "F2", EpisodeNumber = 20, LocalFolderPath = @"C:\2" };
            Episode ep3 = new() { SeriesId = series.Id, Title = "F3", EpisodeNumber = 5 }; // kein lokaler Pfad
            Context.Episodes.AddRange(ep1, ep2, ep3);
            _ = await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            int? highest = await service.GetHighestLocalEpisodeNumberAsync(series.Id);

            Assert.Equal(20, highest);
        }

        [Fact]
        public async Task GetHighestLocalEpisodeNumberAsync_ReturnsNull_WhenNoLocal()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Nur Online");
            Episode ep = new() { SeriesId = series.Id, Title = "Online", EpisodeNumber = 5 };
            _ = Context.Episodes.Add(ep);
            _ = await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            int? highest = await service.GetHighestLocalEpisodeNumberAsync(series.Id);

            Assert.Null(highest);
        }

        [Fact]
        public async Task GetEpisodeCountsForSeriesAsync_ReturnsCounts()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");

            Episode ep1 = new() { SeriesId = series.Id, Title = "Lokal", LocalFolderPath = @"C:\Lokal" };
            Episode ep2 = new() { SeriesId = series.Id, Title = "Online" };
            Context.Episodes.AddRange(ep1, ep2);
            _ = await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            IReadOnlyDictionary<Guid, (int Total, int Local)> counts =
                await service.GetEpisodeCountsForSeriesAsync([series.Id]);

            Assert.True(counts.ContainsKey(series.Id));
            Assert.Equal(2, counts[series.Id].Total);
            Assert.Equal(1, counts[series.Id].Local);
        }

        [Fact]
        public async Task GetByIdsAsync_Single_ReturnsOneEntry()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            IReadOnlyDictionary<Guid, Episode> result = await service.GetByIdsAsync([episode.Id]);

            _ = Assert.Single(result);
            Assert.True(result.ContainsKey(episode.Id));
            Assert.Equal("Folge 1", result[episode.Id].Title);
        }

        [Fact]
        public async Task GetByIdsAsync_Multiple_ReturnsAllRequestedEpisodes()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode1 = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            Episode episode2 = await DataBuilder.PersistEpisodeAsync(series, "Folge 2");
            Episode episode3 = await DataBuilder.PersistEpisodeAsync(series, "Folge 3");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            IReadOnlyDictionary<Guid, Episode> result = await service.GetByIdsAsync(
                [episode1.Id, episode2.Id, episode3.Id]);

            Assert.Equal(3, result.Count);
            Assert.True(result.ContainsKey(episode1.Id));
            Assert.True(result.ContainsKey(episode2.Id));
            Assert.True(result.ContainsKey(episode3.Id));
        }

        [Fact]
        public async Task GetByIdsAsync_EmptyInput_ReturnsEmptyDictionary()
        {
            EpisodeDataService service = new(Context, NullLoggerFactory);

            IReadOnlyDictionary<Guid, Episode> result = await service.GetByIdsAsync([]);

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetByIdsAsync_NotFoundIds_AreOmittedFromDictionary()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            Guid missingId = new("99999999-9999-9999-9999-999999999991");

            IReadOnlyDictionary<Guid, Episode> result = await service.GetByIdsAsync([episode.Id, missingId]);

            _ = Assert.Single(result);
            Assert.True(result.ContainsKey(episode.Id));
            Assert.False(result.ContainsKey(missingId));
        }

        [Fact]
        public async Task UpdateRangeAsync_PersistsAllChanges()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode ep1 = await DataBuilder.PersistEpisodeAsync(series, "Alt 1");
            Episode ep2 = await DataBuilder.PersistEpisodeAsync(series, "Alt 2");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            Episode? loaded1 = await service.GetByIdAsync(ep1.Id);
            Episode? loaded2 = await service.GetByIdAsync(ep2.Id);
            loaded1!.CoverImageUrl = "https://example.org/cover1.jpg";
            loaded2!.CoverImageUrl = "https://example.org/cover2.jpg";

            await service.UpdateRangeAsync([loaded1, loaded2]);
            Context.ChangeTracker.Clear();

            Episode? reloaded1 = await service.GetByIdAsync(ep1.Id);
            Episode? reloaded2 = await service.GetByIdAsync(ep2.Id);
            Assert.Equal("https://example.org/cover1.jpg", reloaded1!.CoverImageUrl);
            Assert.Equal("https://example.org/cover2.jpg", reloaded2!.CoverImageUrl);
        }

        [Fact]
        public async Task UpdateRangeAsync_EmptyInput_DoesNotThrow()
        {
            EpisodeDataService service = new(Context, NullLoggerFactory);

            await service.UpdateRangeAsync([]);
        }

        [Fact]
        public async Task SetCoverLastCheckedAsync_SetsTimestamp()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            DateTime now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            await service.SetCoverLastCheckedAsync(episode.Id, now);
            Context.ChangeTracker.Clear();

            Episode? reloaded = await service.GetByIdAsync(episode.Id);
            _ = Assert.NotNull(reloaded!.CoverLastChecked);
        }
    }
}

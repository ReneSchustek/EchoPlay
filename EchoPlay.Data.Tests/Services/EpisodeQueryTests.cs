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
            await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            await DataBuilder.PersistEpisodeAsync(series, "Folge 2");
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

            Episode? result = await service.GetByIdAsync(Guid.NewGuid());

            Assert.Null(result);
        }

        [Fact]
        public async Task GetBySeriesIdsAsync_ReturnsCombinedResults()
        {
            Series seriesA = await DataBuilder.PersistSeriesAsync("Serie A");
            Series seriesB = await DataBuilder.PersistSeriesAsync("Serie B");
            await DataBuilder.PersistEpisodeAsync(seriesA, "A-Folge");
            await DataBuilder.PersistEpisodeAsync(seriesB, "B-Folge");
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
            await Context.SaveChangesAsync();
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
            Context.Episodes.Add(ep);
            await Context.SaveChangesAsync();
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
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            IReadOnlyDictionary<Guid, (int Total, int Local)> counts =
                await service.GetEpisodeCountsForSeriesAsync([series.Id]);

            Assert.True(counts.ContainsKey(series.Id));
            Assert.Equal(2, counts[series.Id].Total);
            Assert.Equal(1, counts[series.Id].Local);
        }

        [Fact]
        public async Task SetCoverLastCheckedAsync_SetsTimestamp()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            DateTime now = DateTime.UtcNow;
            await service.SetCoverLastCheckedAsync(episode.Id, now);
            Context.ChangeTracker.Clear();

            Episode? reloaded = await service.GetByIdAsync(episode.Id);
            Assert.NotNull(reloaded!.CoverLastChecked);
        }
    }
}

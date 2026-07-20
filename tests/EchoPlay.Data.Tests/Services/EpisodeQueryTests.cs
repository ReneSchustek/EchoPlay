using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Internal;
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
            IReadOnlyList<Episode> result = await service.GetBySeriesIdAsync(series.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task GetBySeriesIdAsync_ReturnsEmpty_WhenNoEpisodes()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Leere Serie");
            EpisodeDataService service = new(Context, NullLoggerFactory);

            IReadOnlyList<Episode> result = await service.GetBySeriesIdAsync(series.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsEpisode_WhenExists()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            Episode? result = await service.GetByIdAsync(episode.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.Equal("Folge 1", result.Title);
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
        {
            EpisodeDataService service = new(Context, NullLoggerFactory);

            Episode? result = await service.GetByIdAsync(new Guid("99999999-9999-9999-9999-999999999996"), cancellationToken: TestContext.Current.CancellationToken);

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
            IReadOnlyList<Episode> result = await service.GetBySeriesIdsAsync([seriesA.Id, seriesB.Id], cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task UpdateAsync_PersistsChanges()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Alt");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            Episode? loaded = await service.GetByIdAsync(episode.Id, cancellationToken: TestContext.Current.CancellationToken);
            loaded!.Title = "Neu";
            await service.UpdateAsync(loaded, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            Episode? reloaded = await service.GetByIdAsync(episode.Id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("Neu", reloaded!.Title);
        }

        [Fact]
        public async Task GetEpisodeCountsForSeriesAsync_ReturnsCounts()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");

            Episode ep1 = new() { SeriesId = series.Id, Title = "Lokal", LocalFolderPath = @"C:\Lokal" };
            Episode ep2 = new() { SeriesId = series.Id, Title = "Online" };
            Context.Episodes.AddRange(ep1, ep2);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            IReadOnlyDictionary<Guid, (int Total, int Local)> counts =
                await service.GetEpisodeCountsForSeriesAsync([series.Id], cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(counts.ContainsKey(series.Id));
            Assert.Equal(2, counts[series.Id].Total);
            Assert.Equal(1, counts[series.Id].Local);
        }

        /// <summary>
        /// Verifiziert, dass die Compiled-Query-Variante den globalen Soft-Delete-Filter
        /// weiterhin anwendet: logisch gelöschte Episoden dürfen nicht in die Zähler einfließen.
        /// </summary>
        [Fact]
        public async Task GetEpisodeCountsForSeriesAsync_ExcludesSoftDeletedEpisodes()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");

            Episode active = new() { SeriesId = series.Id, Title = "Aktiv", LocalFolderPath = @"C:\Aktiv" };
            Episode deleted = new() { SeriesId = series.Id, Title = "Gelöscht", LocalFolderPath = @"C:\Geloescht" };
            Context.Episodes.AddRange(active, deleted);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            deleted.MarkAsDeleted(EntityClock.Current.UtcNow);
            _ = Context.Episodes.Update(deleted);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            IReadOnlyDictionary<Guid, (int Total, int Local)> counts =
                await service.GetEpisodeCountsForSeriesAsync([series.Id], cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(counts.ContainsKey(series.Id));
            Assert.Equal(1, counts[series.Id].Total);
            Assert.Equal(1, counts[series.Id].Local);
        }

        /// <summary>
        /// Eine ausschließlich aus Soft-Delete bestehende Serie liefert keinen Eintrag im
        /// Ergebnis – Schutz vor Geistereinträgen im Dashboard.
        /// </summary>
        [Fact]
        public async Task GetEpisodeCountsForSeriesAsync_OnlyDeletedEpisodes_OmitsSeriesFromResult()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");

            Episode deleted = new() { SeriesId = series.Id, Title = "Gelöscht" };
            _ = Context.Episodes.Add(deleted);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            deleted.MarkAsDeleted(EntityClock.Current.UtcNow);
            _ = Context.Episodes.Update(deleted);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            IReadOnlyDictionary<Guid, (int Total, int Local)> counts =
                await service.GetEpisodeCountsForSeriesAsync([series.Id], cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(counts.ContainsKey(series.Id));
        }

        /// <summary>
        /// Wiederholte Aufrufe der compiled Query auf demselben Service liefern stabile Ergebnisse;
        /// sichert ab, dass das einmal erzeugte Delegate kein versehentliches Caching von Zustand
        /// zwischen Aufrufen einführt.
        /// </summary>
        [Fact]
        public async Task GetEpisodeCountsForSeriesAsync_RepeatedCalls_ReturnStableResults()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");

            Episode ep = new() { SeriesId = series.Id, Title = "Folge" };
            _ = Context.Episodes.Add(ep);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);

            IReadOnlyDictionary<Guid, (int Total, int Local)> first =
                await service.GetEpisodeCountsForSeriesAsync([series.Id], cancellationToken: TestContext.Current.CancellationToken);
            IReadOnlyDictionary<Guid, (int Total, int Local)> second =
                await service.GetEpisodeCountsForSeriesAsync([series.Id], cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(first[series.Id], second[series.Id]);
        }

        [Fact]
        public async Task GetByIdsAsync_Single_ReturnsOneEntry()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            IReadOnlyDictionary<Guid, Episode> result = await service.GetByIdsAsync([episode.Id], cancellationToken: TestContext.Current.CancellationToken);

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
                [episode1.Id, episode2.Id, episode3.Id], cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, result.Count);
            Assert.True(result.ContainsKey(episode1.Id));
            Assert.True(result.ContainsKey(episode2.Id));
            Assert.True(result.ContainsKey(episode3.Id));
        }

        [Fact]
        public async Task GetByIdsAsync_EmptyInput_ReturnsEmptyDictionary()
        {
            EpisodeDataService service = new(Context, NullLoggerFactory);

            IReadOnlyDictionary<Guid, Episode> result = await service.GetByIdsAsync([], cancellationToken: TestContext.Current.CancellationToken);

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

            IReadOnlyDictionary<Guid, Episode> result = await service.GetByIdsAsync([episode.Id, missingId], cancellationToken: TestContext.Current.CancellationToken);

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
            Episode? loaded1 = await service.GetByIdAsync(ep1.Id, cancellationToken: TestContext.Current.CancellationToken);
            Episode? loaded2 = await service.GetByIdAsync(ep2.Id, cancellationToken: TestContext.Current.CancellationToken);
            loaded1!.CoverImageUrl = "https://example.org/cover1.jpg";
            loaded2!.CoverImageUrl = "https://example.org/cover2.jpg";

            await service.UpdateRangeAsync([loaded1, loaded2], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            Episode? reloaded1 = await service.GetByIdAsync(ep1.Id, cancellationToken: TestContext.Current.CancellationToken);
            Episode? reloaded2 = await service.GetByIdAsync(ep2.Id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("https://example.org/cover1.jpg", reloaded1!.CoverImageUrl);
            Assert.Equal("https://example.org/cover2.jpg", reloaded2!.CoverImageUrl);
        }

        [Fact]
        public async Task UpdateRangeAsync_EmptyInput_DoesNotThrow()
        {
            EpisodeDataService service = new(Context, NullLoggerFactory);

            await service.UpdateRangeAsync([], cancellationToken: TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task SetCoverLastCheckedAsync_SetsTimestamp()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");
            Context.ChangeTracker.Clear();

            EpisodeDataService service = new(Context, NullLoggerFactory);
            DateTime now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            await service.SetCoverLastCheckedAsync(episode.Id, now, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            Episode? reloaded = await service.GetByIdAsync(episode.Id, cancellationToken: TestContext.Current.CancellationToken);
            _ = Assert.NotNull(reloaded!.CoverLastChecked);
        }
    }
}

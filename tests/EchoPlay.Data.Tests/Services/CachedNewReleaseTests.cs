using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="CachedNewReleaseDataService"/>.
    /// Prüft Speicherung, Upsert-Logik, Bereinigung und Lesen
    /// von gecachten iTunes-Neuerscheinungen.
    /// </summary>
    public sealed class CachedNewReleaseTests : DbTestBase
    {
        [Fact]
        public async Task GetAllAsync_ReturnsEmptyList_WhenCacheEmpty()
        {
            CachedNewReleaseDataService service = new(Context, NullLoggerFactory);

            IReadOnlyList<CachedNewRelease> result = await service.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }

        [Fact]
        public async Task UpsertRangeAsync_InsertsNewEntries()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Die drei ???");
            CachedNewReleaseDataService service = new(Context, NullLoggerFactory);

            List<CachedNewRelease> entries =
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Folge 238 – Der dunkle Taipan",
                    EpisodeNumber = 238,
                    ReleaseDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                    CollectionId = 100001,
                    CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                },
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Folge 239 – Der Zeitreisende",
                    EpisodeNumber = 239,
                    ReleaseDate = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
                    CollectionId = 100002,
                    CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                }
            ];

            await service.UpsertRangeAsync(entries, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            IReadOnlyList<CachedNewRelease> result = await service.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task GetAllAsync_ReturnsSortedByReleaseDateDescending()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            CachedNewReleaseDataService service = new(Context, NullLoggerFactory);

            // Einträge absichtlich in aufsteigender Reihenfolge einfügen
            List<CachedNewRelease> entries =
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Ältere Folge",
                    ReleaseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    CollectionId = 200001,
                    CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                },
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Neuere Folge",
                    ReleaseDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    CollectionId = 200002,
                    CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                }
            ];

            await service.UpsertRangeAsync(entries, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            IReadOnlyList<CachedNewRelease> result = await service.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);

            // Neueste zuerst
            Assert.Equal("Neuere Folge", result[0].Title);
            Assert.Equal("Ältere Folge", result[1].Title);
        }

        [Fact]
        public async Task UpsertRangeAsync_UpdatesExistingEntryByCollectionId()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Bibi Blocksberg");
            CachedNewReleaseDataService service = new(Context, NullLoggerFactory);

            // Erster Insert
            List<CachedNewRelease> initial =
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Alter Titel",
                    EpisodeNumber = 150,
                    ReleaseDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    CollectionId = 300001,
                    CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc).AddDays(-1)
                }
            ];

            await service.UpsertRangeAsync(initial, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            // Zweiter Insert: gleiche CollectionId, neuer Titel
            List<CachedNewRelease> updated =
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Neuer Titel",
                    EpisodeNumber = 150,
                    ReleaseDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    CollectionId = 300001,
                    CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                }
            ];

            await service.UpsertRangeAsync(updated, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            IReadOnlyList<CachedNewRelease> result = await service.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);

            // Nur ein Eintrag (Update statt zweiter Insert)
            _ = Assert.Single(result);
            Assert.Equal("Neuer Titel", result[0].Title);
        }

        [Fact]
        public async Task RemoveOlderThanAsync_RemovesExpiredEntries()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Benjamin Blümchen");
            CachedNewReleaseDataService service = new(Context, NullLoggerFactory);

            DateTime cutoff = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

            List<CachedNewRelease> entries =
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Zu alt",
                    ReleaseDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                    CollectionId = 400001,
                    CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                },
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Aktuell",
                    ReleaseDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                    CollectionId = 400002,
                    CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                }
            ];

            await service.UpsertRangeAsync(entries, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            int removed = await service.RemoveOlderThanAsync(cutoff, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, removed);

            Context.ChangeTracker.Clear();
            IReadOnlyList<CachedNewRelease> remaining = await service.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);

            _ = Assert.Single(remaining);
            Assert.Equal("Aktuell", remaining[0].Title);
        }

        [Fact]
        public async Task GetLatestCheckTimeAsync_ReturnsNull_WhenCacheEmpty()
        {
            CachedNewReleaseDataService service = new(Context, NullLoggerFactory);

            DateTime? latest = await service.GetLatestCheckTimeAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(latest);
        }

        [Fact]
        public async Task GetLatestCheckTimeAsync_ReturnsNewestCheckTime()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Fünf Freunde");
            CachedNewReleaseDataService service = new(Context, NullLoggerFactory);

            DateTime olderCheck = new(2026, 3, 28, 10, 0, 0, DateTimeKind.Utc);
            DateTime newerCheck = new(2026, 3, 29, 14, 0, 0, DateTimeKind.Utc);

            List<CachedNewRelease> entries =
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Älterer Check",
                    ReleaseDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    CollectionId = 500001,
                    CheckedAtUtc = olderCheck
                },
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Neuerer Check",
                    ReleaseDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                    CollectionId = 500002,
                    CheckedAtUtc = newerCheck
                }
            ];

            await service.UpsertRangeAsync(entries, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            DateTime? latest = await service.GetLatestCheckTimeAsync(cancellationToken: TestContext.Current.CancellationToken);

            _ = Assert.NotNull(latest);
            Assert.Equal(newerCheck, latest.Value);
        }

        [Fact]
        public async Task ClearAllAsync_RemovesAllEntries()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Hanni und Nanni");
            CachedNewReleaseDataService service = new(Context, NullLoggerFactory);

            List<CachedNewRelease> entries =
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Folge 1",
                    ReleaseDate = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                    CollectionId = 600001,
                    CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                },
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Folge 2",
                    ReleaseDate = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                    CollectionId = 600002,
                    CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                }
            ];

            await service.UpsertRangeAsync(entries, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            await service.ClearAllAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            IReadOnlyList<CachedNewRelease> result = await service.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetBySeriesIdAsync_ReturnsOnlyMatchingSeries()
        {
            Series seriesA = await DataBuilder.PersistSeriesAsync("Serie A");
            Series seriesB = await DataBuilder.PersistSeriesAsync("Serie B");
            CachedNewReleaseDataService service = new(Context, NullLoggerFactory);

            List<CachedNewRelease> entries =
            [
                new CachedNewRelease
                {
                    SeriesId = seriesA.Id,
                    Title = "Folge von A",
                    ReleaseDate = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                    CollectionId = 700001,
                    CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                },
                new CachedNewRelease
                {
                    SeriesId = seriesB.Id,
                    Title = "Folge von B",
                    ReleaseDate = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                    CollectionId = 700002,
                    CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                }
            ];

            await service.UpsertRangeAsync(entries, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            IReadOnlyList<CachedNewRelease> resultA = await service.GetBySeriesIdAsync(seriesA.Id, cancellationToken: TestContext.Current.CancellationToken);
            IReadOnlyList<CachedNewRelease> resultB = await service.GetBySeriesIdAsync(seriesB.Id, cancellationToken: TestContext.Current.CancellationToken);

            _ = Assert.Single(resultA);
            Assert.Equal("Folge von A", resultA[0].Title);
            _ = Assert.Single(resultB);
            Assert.Equal("Folge von B", resultB[0].Title);
        }

        [Fact]
        public async Task GetAllAsync_IncludesSeriesNavigation()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Die drei ??? Kids");
            CachedNewReleaseDataService service = new(Context, NullLoggerFactory);

            List<CachedNewRelease> entries =
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Title = "Folge 100",
                    ReleaseDate = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                    CollectionId = 800001,
                    CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                }
            ];

            await service.UpsertRangeAsync(entries, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            IReadOnlyList<CachedNewRelease> result = await service.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);

            // Series-Navigation muss geladen sein (Include in GetAllAsync)
            Assert.NotNull(result[0].Series);
            Assert.Equal("Die drei ??? Kids", result[0].Series.Title);
        }

        [Fact]
        public async Task UpsertRangeAsync_DoesNothing_WhenEmptyList()
        {
            CachedNewReleaseDataService service = new(Context, NullLoggerFactory);

            // Leere Liste: kein Fehler, kein Insert
            await service.UpsertRangeAsync([], cancellationToken: TestContext.Current.CancellationToken);

            IReadOnlyList<CachedNewRelease> result = await service.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Empty(result);
        }

        [Fact]
        public async Task RemoveBySeriesIdsAsync_RemovesOnlyMatchingEntries()
        {
            Series seriesA = await DataBuilder.PersistSeriesAsync("Serie A");
            Series seriesB = await DataBuilder.PersistSeriesAsync("Serie B");
            CachedNewReleaseDataService service = new(Context, NullLoggerFactory);

            List<CachedNewRelease> entries =
            [
                new CachedNewRelease
                {
                    SeriesId = seriesA.Id, Title = "Folge A", CollectionId = 900001,
                    ReleaseDate = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc), CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                },
                new CachedNewRelease
                {
                    SeriesId = seriesB.Id, Title = "Folge B", CollectionId = 900002,
                    ReleaseDate = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc), CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
                }
            ];

            await service.UpsertRangeAsync(entries, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            int removed = await service.RemoveBySeriesIdsAsync([seriesA.Id], cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, removed);
            IReadOnlyList<CachedNewRelease> remaining = await service.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);
            _ = Assert.Single(remaining);
            Assert.Equal(seriesB.Id, remaining[0].SeriesId);
        }

        [Fact]
        public async Task RemoveBySeriesIdsAsync_EmptyList_ReturnsZero()
        {
            CachedNewReleaseDataService service = new(Context, NullLoggerFactory);

            int removed = await service.RemoveBySeriesIdsAsync([], cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, removed);
        }
    }
}

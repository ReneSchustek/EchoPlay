using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Zwei Kontexte auf derselben Datenbankdatei räumen den Neuerscheinungen-Cache auf,
    /// so wie zwei gleichzeitig gestartete Prozesse es tun.
    /// </summary>
    /// <remarks>
    /// Der Fall ist echt: Zwei Starts liefen an, der schnellere entfernte die abgelaufenen
    /// Zeilen, und beim langsameren traf das Löschen keine Zeile mehr. Das brach den ganzen
    /// Start ab. Jeder Test bekommt ein eigenes Verzeichnis, weil xUnit die Tests einer Klasse
    /// parallel ausführt und SQLite-Dateien sich sonst gegenseitig sperren.
    /// </remarks>
    public sealed class CachedNewReleaseSharedFileCleanupTests : IDisposable
    {
        private static readonly EchoPlay.Logger.Abstractions.ILoggerFactory NullLoggerFactory =
            new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

        // Weit genug zurück, dass beide Einträge abgelaufen sind, egal wann der Test läuft.
        private static readonly DateTime Cutoff = new(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly string _directory;
        private readonly string _connectionString;

        /// <summary>
        /// Legt je Test ein eigenes Verzeichnis mit einer frisch angelegten Datenbank an.
        /// </summary>
        public CachedNewReleaseSharedFileCleanupTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "EchoPlaySharedCleanupTests_" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(_directory);
            _connectionString = $"Data Source={Path.Combine(_directory, "echoplay.db")};Pooling=False";
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Fact]
        public async Task RemoveOlderThanAsync_SecondContextFindsNothingLeft_ReturnsZeroWithoutThrowing()
        {
            await SeedTwoExpiredReleasesAsync();

            await using EchoPlayDbContext first = CreateContext();
            await using EchoPlayDbContext second = CreateContext();

            int removedByFirst = await new CachedNewReleaseDataService(first, NullLoggerFactory)
                .RemoveOlderThanAsync(Cutoff, TestContext.Current.CancellationToken);
            int removedBySecond = await new CachedNewReleaseDataService(second, NullLoggerFactory)
                .RemoveOlderThanAsync(Cutoff, TestContext.Current.CancellationToken);

            Assert.Equal(2, removedByFirst);
            Assert.Equal(0, removedBySecond);
            Assert.Equal(0, await CountReleasesAsync());
        }

        [Fact]
        public async Task RemoveOlderThanAsync_TwoContextsAtOnce_NeitherThrowsAndAllRowsAreGone()
        {
            await SeedTwoExpiredReleasesAsync();

            await using EchoPlayDbContext first = CreateContext();
            await using EchoPlayDbContext second = CreateContext();

            int[] removed = await Task.WhenAll(
                new CachedNewReleaseDataService(first, NullLoggerFactory)
                    .RemoveOlderThanAsync(Cutoff, TestContext.Current.CancellationToken),
                new CachedNewReleaseDataService(second, NullLoggerFactory)
                    .RemoveOlderThanAsync(Cutoff, TestContext.Current.CancellationToken));

            // Wer welche Zeile erwischt, entscheidet SQLite. Fest steht nur: zusammen genau zwei.
            Assert.Equal(2, removed.Sum());
            Assert.Equal(0, await CountReleasesAsync());
        }

        private EchoPlayDbContext CreateContext()
        {
            DbContextOptions<EchoPlayDbContext> options = new DbContextOptionsBuilder<EchoPlayDbContext>()
                .UseSqlite(_connectionString)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .Options;
            return new EchoPlayDbContext(options);
        }

        private async Task SeedTwoExpiredReleasesAsync()
        {
            await using EchoPlayDbContext context = CreateContext();
            _ = await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            Series series = await new TestDataBuilder(context).PersistSeriesAsync("Die drei !!!");

            context.CachedNewReleases.AddRange(
                CreateRelease(series.Id, 500001, new DateTime(2020, 1, 10, 0, 0, 0, DateTimeKind.Utc)),
                CreateRelease(series.Id, 500002, new DateTime(2020, 2, 10, 0, 0, 0, DateTimeKind.Utc)));
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        private async Task<int> CountReleasesAsync()
        {
            await using EchoPlayDbContext context = CreateContext();
            return await context.CachedNewReleases.CountAsync(TestContext.Current.CancellationToken);
        }

        private static CachedNewRelease CreateRelease(Guid seriesId, long collectionId, DateTime releaseDate) => new()
        {
            SeriesId = seriesId,
            Title = $"Folge {collectionId}",
            ReleaseDate = releaseDate,
            CollectionId = collectionId,
            CheckedAtUtc = releaseDate,
        };
    }
}

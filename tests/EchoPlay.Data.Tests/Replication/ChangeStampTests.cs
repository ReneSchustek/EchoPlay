using EchoPlay.Core.Replication;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Replication
{
    /// <summary>
    /// Tests für den Änderungszeitpunkt: <see cref="ChangeStampInterceptor"/> beim Speichern und die
    /// Massenänderungen im <see cref="SeriesDataService"/>.
    /// </summary>
    public sealed class ChangeStampTests : IDisposable
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        private readonly CountingClock _clock = new();
        private readonly EchoPlayDbContext _context;

        private static readonly EchoPlay.Logger.Abstractions.ILoggerFactory Loggers =
            new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

        public ChangeStampTests()
        {
            _connection.Open();
            DbContextOptions<EchoPlayDbContext> options = new DbContextOptionsBuilder<EchoPlayDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(new ChangeStampInterceptor(_clock))
                .Options;
            _context = new EchoPlayDbContext(options);
            _ = _context.Database.EnsureCreated();
        }

        [Fact]
        public async Task AddedSeries_GetsAStamp()
        {
            Series series = await AddSeriesAsync();

            Assert.True(HybridTimestamp.TryParse(series.ChangeStamp, out _));
        }

        [Fact]
        public async Task SharedFieldChange_MovesTheStampForward()
        {
            Series series = await AddSeriesAsync();
            string before = series.ChangeStamp!;

            series.IsFavorite = true;
            _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

            Assert.True(string.CompareOrdinal(series.ChangeStamp, before) > 0);
        }

        [Fact]
        public async Task DeviceBoundFieldChange_KeepsTheStamp()
        {
            // Sonst gewänne nach dem Einlesen eine Zeile mit veralteten Favoriten den Abgleich.
            Series series = await AddSeriesAsync();
            string before = series.ChangeStamp!;

            series.LocalFolderPath = @"D:\Mp3\Hörspiele\TKKG";
            series.MarkAsUpdated(DateTime.UtcNow);
            _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

            Assert.Equal(before, series.ChangeStamp);
        }

        [Fact]
        public async Task CacheOnlyTable_GetsNoStamp()
        {
            CoverImage cover = new() { EntityType = CoverEntityTypes.Series, EntityId = Guid.NewGuid(), ImageData = [1, 2, 3] };
            _ = _context.CoverImages.Add(cover);
            _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

            Assert.Null(cover.ChangeStamp);
        }

        [Fact]
        public async Task FavoriteViaBulkUpdate_SetsAStamp_WhenAClockIsGiven()
        {
            Series series = await AddSeriesAsync();
            string before = series.ChangeStamp!;
            SeriesDataService service = new(_context, new WatchedTitleDataService(_context, Loggers), Loggers, _clock);

            await service.SetFavoriteAsync(series.Id, isFavorite: true, TestContext.Current.CancellationToken);

            string? after = await StampOfAsync(series.Id);
            Assert.True(string.CompareOrdinal(after, before) > 0);
        }

        [Fact]
        public async Task FlagViaBulkUpdate_KeepsTheStamp_WithoutAClock()
        {
            Series series = await AddSeriesAsync();
            string before = series.ChangeStamp!;
            SeriesDataService service = new(_context, new WatchedTitleDataService(_context, Loggers), Loggers);

            await service.SetSubscribedAsync(series.Id, isSubscribed: true, TestContext.Current.CancellationToken);

            Assert.Equal(before, await StampOfAsync(series.Id));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }

        private async Task<Series> AddSeriesAsync()
        {
            Series series = new() { Title = "TKKG" };
            _ = _context.Series.Add(series);
            _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
            return series;
        }

        private Task<string?> StampOfAsync(Guid seriesId) => _context.Series
            .AsNoTracking()
            .Where(s => s.Id == seriesId)
            .Select(s => s.ChangeStamp)
            .FirstAsync(TestContext.Current.CancellationToken);

        /// <summary>Eine Uhr, die bei jedem Aufruf eine Millisekunde weitergeht.</summary>
        private sealed class CountingClock : IHybridClock
        {
            private static readonly Guid Device = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
            private long _wall = 1_763_640_000_000;

            public HybridTimestamp Now() => new(Interlocked.Increment(ref _wall), 0, Device);

            public HybridTimestamp Observe(HybridTimestamp remote) => Now();
        }
    }
}

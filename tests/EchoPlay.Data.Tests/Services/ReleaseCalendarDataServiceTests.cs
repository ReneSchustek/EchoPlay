using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="ReleaseCalendarDataService"/>: Zeitraumgrenzen, Reihenfolge,
    /// Aktualisieren per Sammlungs-Kennung, Aufräumen und der Vermerk des letzten Aufbaus.
    /// </summary>
    public sealed class ReleaseCalendarDataServiceTests : DbTestBase
    {
        private static readonly DateTime CheckedAt = new(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);

        private static ReleaseCalendarEntry Entry(Guid seriesId, long collectionId, DateTime releaseDate, string title = "Folge") => new()
        {
            SeriesId = seriesId,
            Title = title,
            ReleaseDate = releaseDate,
            CollectionId = collectionId,
            CheckedAtUtc = CheckedAt
        };

        [Fact]
        public async Task GetBetweenAsync_IncludesTheStartAndExcludesTheEnd()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            ReleaseCalendarDataService service = new(Context, NullLoggerFactory);
            DateTime from = new(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc);
            DateTime to = new(2026, 11, 30, 23, 0, 0, DateTimeKind.Utc);

            await service.UpsertRangeAsync(
            [
                Entry(series.Id, 1, from.AddTicks(-1), "Davor"),
                Entry(series.Id, 2, from, "Erster"),
                Entry(series.Id, 3, to.AddTicks(-1), "Letzter"),
                Entry(series.Id, 4, to, "Danach")
            ], TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            IReadOnlyList<ReleaseCalendarEntry> result = await service.GetBetweenAsync(from, to, TestContext.Current.CancellationToken);

            Assert.Equal(["Erster", "Letzter"], result.Select(e => e.Title));
        }

        [Fact]
        public async Task GetBetweenAsync_SortsByDateThenSeriesTitle()
        {
            Series tkkg = await DataBuilder.PersistSeriesAsync("TKKG");
            Series fragezeichen = await DataBuilder.PersistSeriesAsync("Die drei ???");
            ReleaseCalendarDataService service = new(Context, NullLoggerFactory);
            DateTime day = new(2026, 11, 14, 8, 0, 0, DateTimeKind.Utc);

            await service.UpsertRangeAsync(
            [
                Entry(tkkg.Id, 1, day.AddDays(1), "TKKG später"),
                Entry(tkkg.Id, 2, day, "TKKG"),
                Entry(fragezeichen.Id, 3, day, "Fragezeichen")
            ], TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            IReadOnlyList<ReleaseCalendarEntry> result = await service.GetBetweenAsync(
                day.AddDays(-1), day.AddDays(2), TestContext.Current.CancellationToken);

            Assert.Equal(["Fragezeichen", "TKKG", "TKKG später"], result.Select(e => e.Title));
            Assert.Equal("Die drei ???", result[0].Series.Title);
        }

        [Fact]
        public async Task UpsertRangeAsync_MovesAPostponedRelease()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Bibi Blocksberg");
            ReleaseCalendarDataService service = new(Context, NullLoggerFactory);

            await service.UpsertRangeAsync([Entry(series.Id, 42, new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc))], TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();
            await service.UpsertRangeAsync([Entry(series.Id, 42, new DateTime(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc))], TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            IReadOnlyList<ReleaseCalendarEntry> result = await service.GetBetweenAsync(
                DateTime.MinValue, DateTime.MaxValue, TestContext.Current.CancellationToken);

            ReleaseCalendarEntry entry = Assert.Single(result);
            Assert.Equal(new DateTime(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc), entry.ReleaseDate);
        }

        [Fact]
        public async Task UpsertRangeAsync_WithNothingToWrite_LeavesTheCalendarEmpty()
        {
            ReleaseCalendarDataService service = new(Context, NullLoggerFactory);

            await service.UpsertRangeAsync([], TestContext.Current.CancellationToken);

            Assert.Null(await service.GetLatestReleaseDateAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task UpsertRangeAsync_ReplacesALogicallyDeletedEntry()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Hanni und Nanni");
            ReleaseCalendarDataService service = new(Context, NullLoggerFactory);
            await service.UpsertRangeAsync([Entry(series.Id, 77, new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc), "Alt")], TestContext.Current.CancellationToken);
            ReleaseCalendarEntry stored = Context.ReleaseCalendarEntries.AsTracking().Single();
            stored.MarkAsDeleted(CheckedAt);
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            // Der eindeutige Index zählt die gelöschte Zeile mit; ohne Ersetzen schlüge das Schreiben fehl.
            await service.UpsertRangeAsync([Entry(series.Id, 77, new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc), "Neu")], TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            IReadOnlyList<ReleaseCalendarEntry> result = await service.GetBetweenAsync(
                DateTime.MinValue, DateTime.MaxValue, TestContext.Current.CancellationToken);
            Assert.Equal("Neu", Assert.Single(result).Title);
        }

        [Fact]
        public async Task RemoveOlderThanAsync_KeepsTheCutoffDay()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Benjamin Blümchen");
            ReleaseCalendarDataService service = new(Context, NullLoggerFactory);
            DateTime cutoff = new(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc);

            await service.UpsertRangeAsync(
            [
                Entry(series.Id, 1, cutoff.AddDays(-1), "Alt"),
                Entry(series.Id, 2, cutoff, "Stichtag")
            ], TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            int removed = await service.RemoveOlderThanAsync(cutoff, TestContext.Current.CancellationToken);
            IReadOnlyList<ReleaseCalendarEntry> rest = await service.GetBetweenAsync(
                DateTime.MinValue, DateTime.MaxValue, TestContext.Current.CancellationToken);

            Assert.Equal(1, removed);
            Assert.Equal(["Stichtag"], rest.Select(e => e.Title));
        }

        [Fact]
        public async Task GetLatestReleaseDateAsync_OnEmptyCalendar_IsNull()
        {
            ReleaseCalendarDataService service = new(Context, NullLoggerFactory);

            Assert.Null(await service.GetLatestReleaseDateAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SetLastRefreshAsync_WritesOnlyItsColumn()
        {
            AppSettingsDataService settingsService = new(Context, NullLoggerFactory);
            AppSettings settings = await settingsService.GetAsync(TestContext.Current.CancellationToken);
            settings.NewReleaseDays = 45;
            await settingsService.SaveAsync(settings, TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();
            ReleaseCalendarDataService service = new(Context, NullLoggerFactory);

            await service.SetLastRefreshAsync(CheckedAt, TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            Assert.Equal(CheckedAt, await service.GetLastRefreshAsync(TestContext.Current.CancellationToken));
            AppSettings reloaded = await settingsService.GetAsync(TestContext.Current.CancellationToken);
            Assert.Equal(45, reloaded.NewReleaseDays);
        }
    }
}

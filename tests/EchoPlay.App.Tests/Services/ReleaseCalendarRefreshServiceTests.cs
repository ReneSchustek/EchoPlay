using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="ReleaseCalendarRefreshService"/>: wann ein Aufbau läuft, welche
    /// Serien er prüft, ab welchem Stichtag und was er schreibt.
    /// </summary>
    public sealed class ReleaseCalendarRefreshServiceTests
    {
        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        // Mitte Januar: Der Vormonat beginnt am 1. Dezember um Mitternacht Berliner Zeit.
        private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime ExpectedCutoff = new(2025, 11, 30, 23, 0, 0, DateTimeKind.Utc);

        private readonly AppSettings _appSettings = new() { OfflineMode = false };
        private readonly FakeAppSettingsDataService _settings;
        private readonly FakeSeriesDataService _series = new();
        private readonly FakeReleaseCalendarDataService _calendar = new();
        private readonly FakeClock _clock = new() { UtcNow = Now };

        public ReleaseCalendarRefreshServiceTests()
        {
            _settings = new FakeAppSettingsDataService(_appSettings);
        }

        private ReleaseCalendarRefreshService Build(FakeOnlineEpisodeChecker checker)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => _settings);
            _ = services.AddScoped<ISeriesDataService>(_ => _series);
            _ = services.AddScoped<IReleaseCalendarDataService>(_ => _calendar);
            _ = services.AddScoped<IOnlineEpisodeChecker>(_ => checker);
            ServiceProvider provider = services.BuildServiceProvider();

            return new ReleaseCalendarRefreshService(
                provider.GetRequiredService<IServiceScopeFactory>(), _clock, new FakeLoggerFactory(), Berlin);
        }

        private async Task AddSeriesAsync(int count)
        {
            for (int i = 0; i < count; i++)
            {
                await _series.AddAsync(new Series { Title = $"Serie {i}" }, TestContext.Current.CancellationToken);
            }
        }

        private static OnlineEpisodeCheckResult ResultFor(Guid seriesId, long collectionId, DateTime releaseDate) => new()
        {
            SeriesId = seriesId,
            SeriesTitle = "Serie",
            NewReleaseEpisodes =
            [
                new NewReleaseEpisode
                {
                    Title = "Folge 12: Die Spur",
                    EpisodeNumber = 12,
                    ReleaseDate = releaseDate,
                    CollectionId = collectionId,
                    CoverUrl = new Uri("https://is1-ssl.mzstatic.com/image/cover/100x100bb.jpg")
                }
            ]
        };

        [Fact]
        public async Task RunOnceAsync_InOfflineMode_AsksNobody()
        {
            _appSettings.OfflineMode = true;
            await AddSeriesAsync(3);
            FakeOnlineEpisodeChecker checker = new();

            int written = await Build(checker).RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, written);
            Assert.Equal(0, checker.CheckCallCount);
        }

        [Fact]
        public async Task RunOnceAsync_WithRefreshYoungerThanOneDay_SkipsTheRun()
        {
            await AddSeriesAsync(3);
            _calendar.LastRefresh = Now.AddHours(-23);
            FakeOnlineEpisodeChecker checker = new();

            int written = await Build(checker).RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, written);
            Assert.Equal(0, checker.CheckCallCount);
        }

        [Fact]
        public async Task RunOnceAsync_ChecksAllSeriesInBatches()
        {
            // Nicht nur abonnierte oder überwachte Serien: alle vorhandenen.
            await AddSeriesAsync(ReleaseCalendarRefreshService.BatchSize * 2 + 5);
            FakeOnlineEpisodeChecker checker = new();

            _ = await Build(checker).RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Equal(3, checker.CheckCallCount);
            Assert.Equal(5, checker.LastCheckedSeries.Count);
            Assert.All(checker.LastCheckedSeries, s => Assert.True(s.RequireExactArtistName));
        }

        [Fact]
        public async Task RunOnceAsync_UsesStartOfPreviousMonthInLocalTimeAsCutoff()
        {
            await AddSeriesAsync(1);
            FakeOnlineEpisodeChecker checker = new();

            _ = await Build(checker).RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Equal(ExpectedCutoff, checker.LastCutoffDate);
            Assert.Equal(ExpectedCutoff, _calendar.LastRemoveCutoff);
        }

        [Fact]
        public async Task RunOnceAsync_WritesReleasesAndMarksTheRunComplete()
        {
            await AddSeriesAsync(1);
            Guid seriesId = _series.All[0].Id;
            FakeOnlineEpisodeChecker checker = new([ResultFor(seriesId, 4711, new DateTime(2026, 1, 23, 8, 0, 0, DateTimeKind.Utc))]);
            ReleaseCalendarRefreshService service = Build(checker);
            int changes = 0;
            service.CalendarChanged += () => changes++;

            int written = await service.RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, written);
            ReleaseCalendarEntry entry = Assert.Single(_calendar.Entries);
            Assert.Equal(seriesId, entry.SeriesId);
            Assert.Equal(12, entry.EpisodeNumber);
            Assert.Equal(4711, entry.CollectionId);
            Assert.Equal(Now, entry.CheckedAtUtc);
            Assert.Equal(Now, _calendar.LastRefresh);
            Assert.False(service.IsRefreshing);
            // Einmal für den Stapel, einmal zum Ende des Aufbaus.
            Assert.Equal(2, changes);
        }

        [Fact]
        public async Task RunOnceAsync_WithoutHits_StillMarksTheRunComplete()
        {
            // Sonst liefe bei Serien ohne Ankündigungen jede Stunde ein vollständiger Aufbau.
            await AddSeriesAsync(2);
            FakeOnlineEpisodeChecker checker = new();

            int written = await Build(checker).RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, written);
            Assert.Equal(0, _calendar.UpsertCallCount);
            Assert.Equal(Now, _calendar.LastRefresh);
        }

        [Fact]
        public async Task RunOnceAsync_RemovesEntriesBeforeThePreviousMonth()
        {
            await AddSeriesAsync(1);
            Guid seriesId = _series.All[0].Id;
            await _calendar.UpsertRangeAsync(
            [
                new ReleaseCalendarEntry { SeriesId = seriesId, Title = "Alt", CollectionId = 1, ReleaseDate = new DateTime(2025, 11, 20, 0, 0, 0, DateTimeKind.Utc) },
                new ReleaseCalendarEntry { SeriesId = seriesId, Title = "Vormonat", CollectionId = 2, ReleaseDate = new DateTime(2025, 12, 5, 0, 0, 0, DateTimeKind.Utc) }
            ], TestContext.Current.CancellationToken);

            _ = await Build(new FakeOnlineEpisodeChecker()).RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Equal(["Vormonat"], _calendar.Entries.Select(e => e.Title));
        }
    }
}

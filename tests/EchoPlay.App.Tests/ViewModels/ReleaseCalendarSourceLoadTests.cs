using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für das Laden eines Monats in <see cref="ReleaseCalendarSource"/>: Zeitraum,
    /// Bestandsabgleich je Eintrag und der späteste bekannte Monat.
    /// </summary>
    public sealed class ReleaseCalendarSourceLoadTests
    {
        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        private readonly FakeReleaseCalendarDataService _calendar = new();
        private readonly FakeEpisodeDataService _episodes = new();
        private readonly FakeSeriesDataService _series = new();

        private ReleaseCalendarSource Build()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IReleaseCalendarDataService>(_ => _calendar);
            _ = services.AddScoped<IEpisodeDataService>(_ => _episodes);
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            ServiceProvider provider = services.BuildServiceProvider();

            return new ReleaseCalendarSource(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeClock { UtcNow = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc) },
                Berlin,
                CultureInfo.GetCultureInfo("de-DE"),
                localizationService: null);
        }

        private async Task<Series> AddSeriesAsync(string title)
        {
            Series series = new() { Title = title };
            await _series.AddAsync(series, TestContext.Current.CancellationToken);
            return series;
        }

        private async Task AddEntryAsync(Series series, long collectionId, int number, DateTime releaseUtc)
            => await _calendar.UpsertRangeAsync(
            [
                new ReleaseCalendarEntry
                {
                    SeriesId = series.Id,
                    Series = series,
                    Title = $"Folge {number}",
                    EpisodeNumber = number,
                    ReleaseDate = releaseUtc,
                    CollectionId = collectionId
                }
            ], TestContext.Current.CancellationToken);

        [Fact]
        public async Task LoadMonthAsync_MatchesEachEntryWithTheCollection()
        {
            Series series = await AddSeriesAsync("TKKG");
            await AddEntryAsync(series, 1, 230, new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc));
            await AddEntryAsync(series, 2, 231, new DateTime(2026, 11, 13, 8, 0, 0, DateTimeKind.Utc));
            await AddEntryAsync(series, 3, 232, new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc));
            await _episodes.AddAsync(new Episode { SeriesId = series.Id, Title = "230", EpisodeNumber = 230, LocalTrackCount = 12 }, TestContext.Current.CancellationToken);
            await _episodes.AddAsync(new Episode { SeriesId = series.Id, Title = "231", EpisodeNumber = 231 }, TestContext.Current.CancellationToken);

            IReadOnlyList<ReleaseCalendarItemViewModel> items =
                await Build().LoadMonthAsync(new ReleaseCalendarMonth(2026, 11), TestContext.Current.CancellationToken);

            Assert.Equal(3, items.Count);
            Assert.Equal(ReleaseAvailability.Local, items[0].Availability);
            Assert.Equal("Vorhanden", items[0].BadgeText);
            Assert.Equal(ReleaseAvailability.InLibrary, items[1].Availability);
            Assert.Equal("In der Mediathek", items[1].BadgeText);
            Assert.Equal(ReleaseAvailability.Missing, items[2].Availability);
            Assert.Equal("Angekündigt", items[2].BadgeText);
            Assert.Equal("TKKG, Folge 230, 6. November · Nr. 230", items[0].AutomationName);
        }

        [Fact]
        public async Task LoadMonthAsync_LeavesOutOtherMonths()
        {
            Series series = await AddSeriesAsync("Bibi Blocksberg");
            await AddEntryAsync(series, 1, 167, new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc));

            IReadOnlyList<ReleaseCalendarItemViewModel> items =
                await Build().LoadMonthAsync(new ReleaseCalendarMonth(2026, 11), TestContext.Current.CancellationToken);

            Assert.Empty(items);
        }

        [Fact]
        public async Task LoadAnnouncedAsync_StartsTomorrowInLocalTime()
        {
            // „Jetzt" ist der 19. November, 13 Uhr in Berlin; morgen beginnt dort um 23 Uhr UTC.
            Series series = await AddSeriesAsync("Fünf Freunde");
            await AddEntryAsync(series, 1, 170, new DateTime(2026, 11, 19, 22, 30, 0, DateTimeKind.Utc));
            await AddEntryAsync(series, 2, 171, new DateTime(2026, 11, 19, 23, 30, 0, DateTimeKind.Utc));
            await AddEntryAsync(series, 3, 172, new DateTime(2027, 3, 5, 8, 0, 0, DateTimeKind.Utc));

            IReadOnlyList<ReleaseCalendarItemViewModel> items =
                await Build().LoadAnnouncedAsync(TestContext.Current.CancellationToken);

            Assert.Equal(["Folge 171", "Folge 172"], items.Select(i => i.EpisodeTitle));
            Assert.All(items, item => Assert.True(item.IsAnnounced));
        }

        [Fact]
        public void StartOfTomorrowUtc_FollowsTheLocalDate()
            => Assert.Equal(new DateTime(2026, 11, 19, 23, 0, 0, DateTimeKind.Utc), Build().StartOfTomorrowUtc());

        [Fact]
        public async Task GetLatestMonthAsync_NamesTheMonthOfTheLatestRelease()
        {
            ReleaseCalendarSource source = Build();
            Assert.Null(await source.GetLatestMonthAsync(TestContext.Current.CancellationToken));

            Series series = await AddSeriesAsync("Die drei ???");
            await AddEntryAsync(series, 1, 242, new DateTime(2026, 12, 18, 8, 0, 0, DateTimeKind.Utc));
            await AddEntryAsync(series, 2, 243, new DateTime(2027, 2, 28, 23, 30, 0, DateTimeKind.Utc));

            // 28. Februar, 23:30 UTC ist in Berlin schon der 1. März.
            Assert.Equal(new ReleaseCalendarMonth(2027, 3), await source.GetLatestMonthAsync(TestContext.Current.CancellationToken));
        }
    }
}

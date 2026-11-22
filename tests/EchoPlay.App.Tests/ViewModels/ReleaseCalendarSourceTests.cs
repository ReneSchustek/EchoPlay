using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Globalization;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="ReleaseCalendarSource"/>: Kachelinhalt aus einem Kalendereintrag,
    /// Erscheinungstag in Ortszeit, Kennzeichen und Cover.
    /// </summary>
    public sealed class ReleaseCalendarSourceTests
    {
        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        private readonly FakeClock _clock = new() { UtcNow = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc) };

        private ReleaseCalendarSource Build()
            => new(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _clock, Berlin, German, null);

        private static ReleaseCalendarEntry Entry(DateTime releaseUtc, string? coverUrl = "https://is1-ssl.mzstatic.com/image/x/100x100bb.jpg") => new()
        {
            SeriesId = Guid.NewGuid(),
            Series = new Series { Title = "TKKG", CoverImageUrl = "https://example.org/serie.jpg" },
            Title = "Folge 230: Das Phantom",
            EpisodeNumber = 230,
            ReleaseDate = releaseUtc,
            CoverUrl = coverUrl,
            CollectionId = 1
        };

        [Fact]
        public void BuildItem_ShowsLocalDayAndEpisodeNumber()
        {
            // 30. November, 23:30 UTC erscheint in Berlin am 1. Dezember.
            ReleaseCalendarItemViewModel item = Build().BuildItem(
                Entry(new DateTime(2026, 11, 30, 23, 30, 0, DateTimeKind.Utc)), ReleaseAvailability.Missing);

            Assert.Equal("1. Dezember · Nr. 230", item.DateLine);
            Assert.Equal("TKKG", item.SeriesName);
        }

        [Fact]
        public void BuildItem_InTheFuture_IsAnnounced()
        {
            ReleaseCalendarItemViewModel item = Build().BuildItem(
                Entry(new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Utc)), ReleaseAvailability.Missing);

            Assert.True(item.IsAnnounced);
            Assert.Equal("Angekündigt", item.BadgeText);
        }

        [Fact]
        public void BuildItem_ReleasedAndLocal_SaysAvailable()
        {
            ReleaseCalendarItemViewModel item = Build().BuildItem(
                Entry(new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc)), ReleaseAvailability.Local);

            Assert.False(item.IsAnnounced);
            Assert.Equal("Vorhanden", item.BadgeText);
        }

        [Fact]
        public void BuildItem_ReleasedAndMissing_HasNoBadge()
        {
            ReleaseCalendarItemViewModel item = Build().BuildItem(
                Entry(new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc)), ReleaseAvailability.Missing);

            Assert.Null(item.BadgeText);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, item.BadgeVisibility);
        }

        [Fact]
        public void BuildItem_AsksForTheCoverInTileSize()
        {
            ReleaseCalendarItemViewModel item = Build().BuildItem(
                Entry(new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc)), ReleaseAvailability.Missing);

            Assert.Equal(new Uri("https://is1-ssl.mzstatic.com/image/x/300x300bb.jpg"), item.CoverUri);
        }

        [Fact]
        public void BuildItem_WithoutAlbumCover_FallsBackToTheSeriesCover()
        {
            ReleaseCalendarItemViewModel item = Build().BuildItem(
                Entry(new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc), coverUrl: null), ReleaseAvailability.Missing);

            Assert.Equal(new Uri("https://example.org/serie.jpg"), item.CoverUri);
        }

        [Theory]
        [InlineData(true, ReleaseAvailability.Local)]
        [InlineData(false, ReleaseAvailability.InLibrary)]
        public void ToAvailability_WithKnownEpisode_DependsOnTheFile(bool hasLocalTrack, ReleaseAvailability expected)
        {
            LocalEpisodeMatch match = new(Guid.NewGuid(), hasLocalTrack, IsCompleted: false);

            Assert.Equal(expected, ReleaseCalendarSource.ToAvailability(match));
        }

        [Fact]
        public void ToAvailability_WithoutEpisode_IsMissing()
        {
            Assert.Equal(ReleaseAvailability.Missing, ReleaseCalendarSource.ToAvailability(default));
        }
    }
}

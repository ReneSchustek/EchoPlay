using EchoPlay.App.Services;
using System;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="ReleaseCalendarMonth"/>: Monatswechsel über den Jahreswechsel und
    /// Monatsgrenzen in Ortszeit, auch über die Zeitumstellung.
    /// </summary>
    public sealed class ReleaseCalendarMonthTests
    {
        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        [Fact]
        public void Previous_OfJanuary_IsDecemberOfPreviousYear()
        {
            Assert.Equal(new ReleaseCalendarMonth(2025, 12), new ReleaseCalendarMonth(2026, 1).Previous);
        }

        [Fact]
        public void Next_OfDecember_IsJanuaryOfNextYear()
        {
            Assert.Equal(new ReleaseCalendarMonth(2027, 1), new ReleaseCalendarMonth(2026, 12).Next);
        }

        [Fact]
        public void StartUtc_InWinter_IsOneHourBeforeLocalMidnight()
        {
            DateTime start = new ReleaseCalendarMonth(2026, 12).StartUtc(Berlin);

            Assert.Equal(new DateTime(2026, 11, 30, 23, 0, 0, DateTimeKind.Utc), start);
        }

        [Fact]
        public void StartUtc_InSummer_IsTwoHoursBeforeLocalMidnight()
        {
            DateTime start = new ReleaseCalendarMonth(2026, 7).StartUtc(Berlin);

            Assert.Equal(new DateTime(2026, 6, 30, 22, 0, 0, DateTimeKind.Utc), start);
        }

        [Fact]
        public void EndUtc_OfMarch_FollowsTheClockChange()
        {
            // Ende März gilt noch Winterzeit, der 1. April beginnt in Sommerzeit.
            DateTime end = new ReleaseCalendarMonth(2026, 3).EndUtc(Berlin);

            Assert.Equal(new DateTime(2026, 3, 31, 22, 0, 0, DateTimeKind.Utc), end);
        }

        [Fact]
        public void FromUtc_LateEveningOnLastDay_BelongsToNextMonthLocally()
        {
            // 30. November, 23:30 UTC ist in Berlin schon der 1. Dezember.
            ReleaseCalendarMonth month = ReleaseCalendarMonth.FromUtc(
                new DateTime(2026, 11, 30, 23, 30, 0, DateTimeKind.Utc), Berlin);

            Assert.Equal(new ReleaseCalendarMonth(2026, 12), month);
        }

        [Fact]
        public void ToString_NamesYearAndMonth()
        {
            Assert.Equal("2026-03", new ReleaseCalendarMonth(2026, 3).ToString());
        }

        [Fact]
        public void Comparison_OrdersByYearThenMonth()
        {
            Assert.True(new ReleaseCalendarMonth(2025, 12) < new ReleaseCalendarMonth(2026, 1));
            Assert.True(new ReleaseCalendarMonth(2026, 3) > new ReleaseCalendarMonth(2026, 2));
            Assert.True(new ReleaseCalendarMonth(2026, 3) <= new ReleaseCalendarMonth(2026, 3));
            Assert.True(new ReleaseCalendarMonth(2026, 3) >= new ReleaseCalendarMonth(2026, 3));
        }
    }
}

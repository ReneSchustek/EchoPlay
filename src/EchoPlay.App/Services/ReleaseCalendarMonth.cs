using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Ein Kalendermonat in der Zeitzone des Nutzers, mit seinen Grenzen in UTC.
    /// </summary>
    /// <remarks>
    /// Die Erscheinungszeitpunkte liegen in UTC vor, der Monat aber gilt in der Ortszeit.
    /// Eine Folge, die am 1. Dezember um 0:30 Uhr deutscher Zeit erscheint, steht in UTC noch
    /// am 30. November. Grenzen nach UTC-Datum stellten sie in den falschen Monat.
    /// </remarks>
    /// <param name="Year">Das Jahr.</param>
    /// <param name="Month">Der Monat, 1 bis 12.</param>
    internal readonly record struct ReleaseCalendarMonth(int Year, int Month) : IComparable<ReleaseCalendarMonth>
    {
        /// <summary>Der Vormonat.</summary>
        public ReleaseCalendarMonth Previous => Month == 1 ? new(Year - 1, 12) : new(Year, Month - 1);

        /// <summary>Der Folgemonat.</summary>
        public ReleaseCalendarMonth Next => Month == 12 ? new(Year + 1, 1) : new(Year, Month + 1);

        /// <summary>Ermittelt den Monat, in den ein UTC-Zeitpunkt in der gegebenen Zeitzone fällt.</summary>
        /// <param name="utc">Der Zeitpunkt in UTC.</param>
        /// <param name="timeZone">Die Zeitzone des Nutzers.</param>
        /// <returns>Der Monat in Ortszeit.</returns>
        public static ReleaseCalendarMonth FromUtc(DateTime utc, TimeZoneInfo timeZone)
        {
            ArgumentNullException.ThrowIfNull(timeZone);

            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);
            return new ReleaseCalendarMonth(local.Year, local.Month);
        }

        /// <summary>Beginn des Monats (Mitternacht Ortszeit am Ersten) in UTC.</summary>
        /// <param name="timeZone">Die Zeitzone des Nutzers.</param>
        /// <returns>Der Beginn in UTC.</returns>
        public DateTime StartUtc(TimeZoneInfo timeZone)
        {
            ArgumentNullException.ThrowIfNull(timeZone);

            DateTime localStart = new(Year, Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone);
        }

        /// <summary>Ende des Monats (ausgeschlossen), also der Beginn des Folgemonats, in UTC.</summary>
        /// <param name="timeZone">Die Zeitzone des Nutzers.</param>
        /// <returns>Das Ende in UTC.</returns>
        public DateTime EndUtc(TimeZoneInfo timeZone) => Next.StartUtc(timeZone);

        /// <summary>Der Monat als „2026-11“, etwa für das Protokoll.</summary>
        /// <remarks>
        /// Eigene Fassung statt der vom Compiler erzeugten: Die gibt alle Eigenschaften aus, also
        /// auch <see cref="Previous"/> und <see cref="Next"/>, die wieder Monate sind, und liefe
        /// damit bis zum Stapelüberlauf.
        /// </remarks>
        /// <returns>Jahr und Monat, vierstellig und zweistellig.</returns>
        public override string ToString()
            => $"{Year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)}-{Month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)}";

        /// <inheritdoc />
        public int CompareTo(ReleaseCalendarMonth other)
            => Year != other.Year ? Year.CompareTo(other.Year) : Month.CompareTo(other.Month);

        /// <summary>Ob der linke Monat vor dem rechten liegt.</summary>
        /// <param name="left">Linker Monat.</param>
        /// <param name="right">Rechter Monat.</param>
        public static bool operator <(ReleaseCalendarMonth left, ReleaseCalendarMonth right) => left.CompareTo(right) < 0;

        /// <summary>Ob der linke Monat nach dem rechten liegt.</summary>
        /// <param name="left">Linker Monat.</param>
        /// <param name="right">Rechter Monat.</param>
        public static bool operator >(ReleaseCalendarMonth left, ReleaseCalendarMonth right) => left.CompareTo(right) > 0;

        /// <summary>Ob der linke Monat vor dem rechten liegt oder ihm gleicht.</summary>
        /// <param name="left">Linker Monat.</param>
        /// <param name="right">Rechter Monat.</param>
        public static bool operator <=(ReleaseCalendarMonth left, ReleaseCalendarMonth right) => left.CompareTo(right) <= 0;

        /// <summary>Ob der linke Monat nach dem rechten liegt oder ihm gleicht.</summary>
        /// <param name="left">Linker Monat.</param>
        /// <param name="right">Rechter Monat.</param>
        public static bool operator >=(ReleaseCalendarMonth left, ReleaseCalendarMonth right) => left.CompareTo(right) >= 0;
    }
}

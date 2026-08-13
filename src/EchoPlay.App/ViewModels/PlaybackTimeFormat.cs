using System;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Rechnung hinter jeder Zeitanzeige der Wiedergabe: wie eine Zeitspanne geschrieben
    /// wird und wie weit die Folge fortgeschritten ist.
    /// </summary>
    /// <remarks>
    /// Beide Wiedergabe-Ansichten zeigen dieselben Zahlen, sehen aber verschieden aus: Der
    /// Mini-Player hat keinen Suchlauf und keine Umschaltung zwischen Rest- und Gesamtzeit.
    /// Was sie teilen, ist die Rechnung — und die stand vorher zweimal im Quelltext, mit je
    /// eigener Formatierung.
    /// </remarks>
    internal static class PlaybackTimeFormat
    {
        /// <summary>
        /// Schreibt eine Zeitspanne lesbar: unter einer Stunde „m:ss", ab einer Stunde
        /// „h:mm:ss".
        /// </summary>
        /// <param name="time">Die Zeitspanne.</param>
        /// <returns>Der Text für die Anzeige.</returns>
        public static string Format(TimeSpan time)
        {
            if (time.TotalHours >= 1)
            {
                return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
            }

            return $"{(int)time.TotalMinutes}:{time.Seconds:D2}";
        }

        /// <summary>
        /// Der Fortschritt in der ganzen Folge, in Prozent.
        /// </summary>
        /// <param name="position">Die Stelle in der Folge.</param>
        /// <param name="duration">Die Gesamtdauer der Folge; ohne sie ist der Wert 0.</param>
        /// <returns>Ein Wert zwischen 0 und 100.</returns>
        public static double Percent(TimeSpan position, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                return 0;
            }

            return Math.Min(100, position.TotalSeconds / duration.TotalSeconds * 100);
        }

        /// <summary>
        /// Die verbleibende Zeit, nie negativ — beim Spurwechsel kann die gemeldete Position
        /// die Dauer für einen Augenblick überschreiten.
        /// </summary>
        /// <param name="position">Die aktuelle Stelle.</param>
        /// <param name="duration">Die Gesamtdauer.</param>
        /// <returns>Die verbleibende Zeitspanne.</returns>
        public static TimeSpan Remaining(TimeSpan position, TimeSpan duration)
        {
            TimeSpan remaining = duration - position;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }
}

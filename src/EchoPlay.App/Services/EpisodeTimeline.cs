using System;
using System.Collections.Generic;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Rechnet zwischen der Stelle in einer einzelnen Spur und der Stelle in der ganzen
    /// Folge um.
    /// </summary>
    /// <remarks>
    /// Eine Folge besteht aus mehreren Dateien; die Wiedergabe kennt aber nur die Position
    /// in der gerade laufenden. Wer nur diese speichert, verliert die Spur — im Wortsinn:
    /// Beim Fortsetzen landete man mit der Minutenzahl aus Spur 3 wieder in Spur 1. Die
    /// Zeitachse hält die Spurdauern und macht aus beidem eine Angabe, die für sich steht.
    ///
    /// Ohne bekannte Spurdauern bleibt die Zeitachse leer und rechnet nicht um — dann gilt
    /// wieder die Stelle in der ersten Spur. Das ist genau das bisherige Verhalten und
    /// damit für den Bestand unauffällig.
    /// </remarks>
    internal sealed class EpisodeTimeline
    {
        /// <summary>Eine Zeitachse ohne bekannte Spurdauern.</summary>
        public static readonly EpisodeTimeline Empty = new([]);

        private readonly TimeSpan[] _trackDurations;

        /// <summary>
        /// Legt die Zeitachse aus den Spurdauern an.
        /// </summary>
        /// <param name="trackDurations">Die Dauern der Spuren in Abspielreihenfolge.</param>
        public EpisodeTimeline(IReadOnlyList<TimeSpan> trackDurations)
        {
            ArgumentNullException.ThrowIfNull(trackDurations);

            _trackDurations = new TimeSpan[trackDurations.Count];
            for (int i = 0; i < trackDurations.Count; i++)
            {
                // Unbekannte oder unsinnige Dauern zählen als null, damit die Summe nicht kippt.
                _trackDurations[i] = trackDurations[i] > TimeSpan.Zero ? trackDurations[i] : TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Ob die Zeitachse rechnen kann. Sie kann es, sobald mindestens eine Spurdauer
        /// bekannt ist.
        /// </summary>
        public bool IsKnown
        {
            get
            {
                foreach (TimeSpan duration in _trackDurations)
                {
                    if (duration > TimeSpan.Zero)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Die Gesamtdauer der Folge; <see cref="TimeSpan.Zero"/>, wenn unbekannt.</summary>
        public TimeSpan TotalDuration
        {
            get
            {
                TimeSpan total = TimeSpan.Zero;
                foreach (TimeSpan duration in _trackDurations)
                {
                    total += duration;
                }

                return total;
            }
        }

        /// <summary>
        /// Gibt an, ob eine gespeicherte Stelle noch offen ist — ob es also etwas
        /// weiterzuhören gibt.
        /// </summary>
        /// <remarks>
        /// Die eine Stelle, an der diese Regel steht. Sie wird an zwei Enden gebraucht: Die
        /// Ansichten fragen, ob eine Folge unter „Angefangen" gehört, der Wiedergabestart
        /// fragt, ob er fortsetzt. Als der Wiedergabestart eine eigene Fassung hatte, fiel
        /// er auf den Hörstatus zurück und begann eine wieder aufgenommene Folge stets von
        /// vorn — während die Liste sie korrekt als angefangen führte.
        ///
        /// Ohne bekannte Gesamtdauer entscheidet die Stelle allein: Es gibt dann nichts,
        /// wogegen sie sich prüfen ließe.
        /// </remarks>
        /// <param name="lastPosition">Die gespeicherte Stelle in der Folge.</param>
        /// <param name="totalDuration">Die Gesamtdauer; <see cref="TimeSpan.Zero"/>, wenn unbekannt.</param>
        /// <returns><see langword="true"/>, wenn die Stelle offen ist.</returns>
        public static bool HasOpenPosition(TimeSpan lastPosition, TimeSpan totalDuration)
            => lastPosition > TimeSpan.Zero
               && (totalDuration <= TimeSpan.Zero || lastPosition < totalDuration);

        /// <summary>
        /// Rechnet die Stelle in einer Spur auf die Stelle in der ganzen Folge um.
        /// </summary>
        /// <param name="trackIndex">Die laufende Spur, nullbasiert.</param>
        /// <param name="positionInTrack">Die Stelle innerhalb dieser Spur.</param>
        /// <returns>Die Stelle in der Folge.</returns>
        public TimeSpan ToOverall(int trackIndex, TimeSpan positionInTrack)
        {
            if (!IsKnown || trackIndex <= 0)
            {
                return positionInTrack;
            }

            TimeSpan offset = TimeSpan.Zero;
            int last = Math.Min(trackIndex, _trackDurations.Length);
            for (int i = 0; i < last; i++)
            {
                offset += _trackDurations[i];
            }

            return offset + positionInTrack;
        }

        /// <summary>
        /// Rechnet die Stelle in der Folge auf Spur und Stelle darin um.
        /// </summary>
        /// <param name="overallPosition">Die Stelle in der Folge.</param>
        /// <returns>Die Spur (nullbasiert) und die Stelle darin.</returns>
        public (int TrackIndex, TimeSpan PositionInTrack) ToTrack(TimeSpan overallPosition)
        {
            if (!IsKnown || overallPosition <= TimeSpan.Zero)
            {
                return (0, overallPosition > TimeSpan.Zero ? overallPosition : TimeSpan.Zero);
            }

            TimeSpan rest = overallPosition;
            for (int i = 0; i < _trackDurations.Length; i++)
            {
                TimeSpan duration = _trackDurations[i];

                // Eine Spur ohne bekannte Dauer lässt sich nicht überspringen — dort endet
                // die Rechnung, sonst liefe der Rest ins Leere.
                if (duration <= TimeSpan.Zero)
                {
                    return (i, rest);
                }

                if (rest < duration)
                {
                    return (i, rest);
                }

                rest -= duration;
            }

            // Jenseits des Endes: an den Anfang der letzten Spur, statt ins Nichts zu springen.
            return (Math.Max(0, _trackDurations.Length - 1), TimeSpan.Zero);
        }
    }
}

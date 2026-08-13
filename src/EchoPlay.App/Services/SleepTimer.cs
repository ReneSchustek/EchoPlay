using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Der Einschlaf-Zeitgeber: zählt die eingestellte Dauer herunter und meldet, wenn sie
    /// abgelaufen ist.
    /// </summary>
    /// <remarks>
    /// Er hängt am selben halbsekündlichen Takt wie die Positionsanzeige, hat aber nichts mit
    /// ihr zu tun. Als eigener Typ ist er ohne laufende Wiedergabe prüfbar.
    /// </remarks>
    public sealed class SleepTimer
    {
        private readonly object _lock = new();
        private TimeSpan? _remaining;

        /// <summary>
        /// Die verbleibende Zeit, oder <see langword="null"/>, wenn kein Zeitgeber läuft.
        /// </summary>
        public TimeSpan? Remaining
        {
            get
            {
                lock (_lock)
                {
                    return _remaining;
                }
            }
        }

        /// <summary>
        /// Stellt den Zeitgeber ein. <see langword="null"/> schaltet ihn ab.
        /// </summary>
        /// <param name="duration">Die Dauer bis zum Anhalten der Wiedergabe.</param>
        public void Set(TimeSpan? duration)
        {
            lock (_lock)
            {
                _remaining = duration;
            }
        }

        /// <summary>
        /// Zieht die verstrichene Zeit ab.
        /// </summary>
        /// <param name="elapsed">Die Zeit seit dem letzten Takt.</param>
        /// <returns>
        /// <see langword="true"/>, sobald der Zeitgeber abgelaufen ist — dann soll die
        /// Wiedergabe anhalten. Danach ist er abgeschaltet und meldet nicht erneut.
        /// </returns>
        public bool Tick(TimeSpan elapsed)
        {
            lock (_lock)
            {
                if (!_remaining.HasValue)
                {
                    return false;
                }

                _remaining = _remaining.Value - elapsed;

                if (_remaining.Value > TimeSpan.Zero)
                {
                    return false;
                }

                _remaining = null;
                return true;
            }
        }
    }
}

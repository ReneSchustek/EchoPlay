using EchoPlay.Core.Abstractions.Time;

namespace EchoPlay.Core.Replication
{
    /// <summary>
    /// Vergibt Zeitpunkte der hybriden logischen Uhr für ein Gerät.
    /// </summary>
    public interface IHybridClock
    {
        /// <summary>Ein neuer Zeitpunkt für eine Änderung auf diesem Gerät.</summary>
        /// <returns>Ein Zeitpunkt, später als jeder bisher vergebene oder beobachtete.</returns>
        HybridTimestamp Now();

        /// <summary>
        /// Nimmt einen Zeitpunkt eines anderen Geräts zur Kenntnis, damit spätere eigene Zeitpunkte
        /// nach ihm liegen, auch wenn die eigene Wanduhr nachgeht.
        /// </summary>
        /// <param name="remote">Der fremde Zeitpunkt.</param>
        /// <returns>Ein eigener Zeitpunkt, später als <paramref name="remote"/>.</returns>
        HybridTimestamp Observe(HybridTimestamp remote);
    }

    /// <summary>
    /// Die hybride logische Uhr nach Kulkarni et al. (2014): Wanduhr, solange sie vorwärts läuft,
    /// sonst ein Zähler.
    /// </summary>
    /// <remarks>
    /// Geht die Wanduhr zurück, etwa nach einer Zeitkorrektur, vergibt die Uhr trotzdem nur
    /// wachsende Zeitpunkte: Sie bleibt auf der zuletzt gesehenen Zeit stehen und zählt weiter.
    /// Ein fremder Zeitpunkt aus der Zukunft zieht die Uhr mit, damit eine eigene Änderung danach
    /// nie als älter gilt als die, auf die sie antwortet. Die Uhr ist threadsicher; Speichern und
    /// Abgleich können gleichzeitig Zeitpunkte holen.
    /// </remarks>
    public sealed class HybridLogicalClock : IHybridClock
    {
        private readonly IClock _clock;
        private readonly Guid _deviceId;
        private readonly Lock _gate = new();
        private long _lastWall;
        private int _counter;

        /// <summary>
        /// Erzeugt die Uhr eines Geräts.
        /// </summary>
        /// <param name="clock">Die Wanduhr.</param>
        /// <param name="device">Die Kennung des Geräts.</param>
        public HybridLogicalClock(IClock clock, IDeviceIdentity device)
        {
            ArgumentNullException.ThrowIfNull(clock);
            ArgumentNullException.ThrowIfNull(device);
            _clock = clock;
            _deviceId = device.DeviceId;
        }

        /// <inheritdoc/>
        public HybridTimestamp Now()
        {
            long wall = WallNow();
            lock (_gate)
            {
                if (wall > _lastWall)
                {
                    _lastWall = wall;
                    _counter = 0;
                }
                else
                {
                    _counter = checked(_counter + 1);
                }

                return new HybridTimestamp(_lastWall, _counter, _deviceId);
            }
        }

        /// <inheritdoc/>
        public HybridTimestamp Observe(HybridTimestamp remote)
        {
            long wall = WallNow();
            lock (_gate)
            {
                long next = Math.Max(wall, Math.Max(_lastWall, remote.WallMilliseconds));

                if (next == _lastWall && next == remote.WallMilliseconds)
                {
                    _counter = checked(Math.Max(_counter, remote.Counter) + 1);
                }
                else if (next == _lastWall)
                {
                    _counter = checked(_counter + 1);
                }
                else if (next == remote.WallMilliseconds)
                {
                    _counter = checked(remote.Counter + 1);
                }
                else
                {
                    _counter = 0;
                }

                _lastWall = next;
                return new HybridTimestamp(_lastWall, _counter, _deviceId);
            }
        }

        private long WallNow() => new DateTimeOffset(DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    }
}

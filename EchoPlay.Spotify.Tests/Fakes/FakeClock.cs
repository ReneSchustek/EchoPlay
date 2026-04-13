using EchoPlay.Core.Abstractions.Time;
using System;

namespace EchoPlay.Spotify.Tests.Fakes
{
    /// <summary>
    /// Deterministische Uhr für Spotify-Tests. Der Startwert (2026-01-15 10:00 UTC) ist so
    /// gewählt, dass er weit genug in der Vergangenheit liegt, um keine Release-Daten
    /// in Test-Fixtures zufällig zu treffen, aber nicht so weit, dass Datumsarithmetik
    /// offensichtlich "historisch" wirkt.
    /// </summary>
    internal sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
    }
}

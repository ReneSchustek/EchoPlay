using System;

namespace EchoPlay.Core.Abstractions.Time
{
    /// <summary>
    /// Liefert die aktuelle Zeit. Wird in Tests durch einen FakeClock ersetzt,
    /// damit zeitabhängige Logik reproduzierbar geprüft werden kann.
    /// </summary>
    public interface IClock
    {
        /// <summary>Aktuelle UTC-Zeit.</summary>
        DateTime UtcNow { get; }
    }

    /// <summary>Standard-Implementierung – liest die System-Zeit.</summary>
    public sealed class SystemClock : IClock
    {
        /// <inheritdoc />
        public DateTime UtcNow => DateTime.UtcNow;
    }
}

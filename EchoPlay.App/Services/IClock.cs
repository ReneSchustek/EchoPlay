namespace EchoPlay.App.Services
{
    /// <summary>
    /// Liefert die aktuelle Zeit. Wird in Tests durch einen <c>FakeClock</c> ersetzt,
    /// damit zeitabhängige Logik reproduzierbar geprüft werden kann.
    /// </summary>
    public interface IClock
    {
        /// <summary>Aktuelle UTC-Zeit.</summary>
        System.DateTime UtcNow { get; }
    }

    /// <summary>Standard-Implementierung – liest die System-Zeit.</summary>
    public sealed class SystemClock : IClock
    {
        /// <inheritdoc />
        public System.DateTime UtcNow => System.DateTime.UtcNow;
    }
}

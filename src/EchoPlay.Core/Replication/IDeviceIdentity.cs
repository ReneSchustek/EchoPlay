namespace EchoPlay.Core.Replication
{
    /// <summary>
    /// Die Kennung dieser Installation, einmal vergeben und danach unverändert.
    /// </summary>
    /// <remarks>
    /// Sie gehört zum Gerät, nicht zu den Daten: Wird eine Datenbank auf einem anderen Rechner
    /// eingespielt, behält jeder Rechner seine eigene Kennung. Sonst hielten sich nach einem Umzug
    /// zwei Geräte für dasselbe, und der Abgleich verlöre Änderungen.
    /// </remarks>
    public interface IDeviceIdentity
    {
        /// <summary>Die Kennung, nie <see cref="Guid.Empty"/>.</summary>
        Guid DeviceId { get; }
    }
}

using EchoPlay.Data.Entities.Settings;

namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Die Geräte im Abgleich, wie die Einstellungen sie zeigen: Lesestand, letzter Kontakt, Fehler.
    /// </summary>
    public interface IReplicationPeerDataService
    {
        /// <summary>Liefert alle Geräte außer den entfernten, das zuletzt gesehene zuerst.</summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die Geräte, das eigene eingeschlossen.</returns>
        Task<IReadOnlyList<ReplicationPeer>> GetActiveAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Entfernt ein Gerät aus dem Abgleich: Sein Protokoll wird danach nicht mehr gelesen. Was
        /// schon übernommen ist, bleibt.
        /// </summary>
        /// <param name="deviceId">Das Gerät.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns><see langword="true"/>, wenn es das Gerät gab.</returns>
        Task<bool> RemoveAsync(Guid deviceId, CancellationToken cancellationToken = default);
    }
}

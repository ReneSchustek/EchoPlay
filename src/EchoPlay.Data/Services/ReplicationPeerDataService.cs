using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// Standard-Umsetzung von <see cref="IReplicationPeerDataService"/>. Entfernt wird weich: Die
    /// Zeile bleibt als Merker, dass dieses Gerät nicht mehr gelesen wird.
    /// </summary>
    /// <param name="context">Der Datenbankkontext.</param>
    /// <param name="time">Zeitquelle für den Löschzeitpunkt; ohne Angabe die Systemuhr.</param>
    public sealed class ReplicationPeerDataService(EchoPlayDbContext context, TimeProvider? time = null) : IReplicationPeerDataService
    {
        private readonly EchoPlayDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ReplicationPeer>> GetActiveAsync(CancellationToken cancellationToken = default)
        {
            List<ReplicationPeer> peers = await _context.ReplicationPeers
                .Where(p => !p.IsDeleted)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return [.. peers.OrderByDescending(p => p.LastSeenUtc ?? DateTime.MinValue).ThenBy(p => p.DeviceId)];
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            ReplicationPeer? peer = await _context.ReplicationPeers
                .AsTracking()
                .FirstOrDefaultAsync(p => p.DeviceId == deviceId && !p.IsDeleted, cancellationToken)
                .ConfigureAwait(false);
            if (peer is null)
            {
                return false;
            }

            peer.MarkAsDeleted((time ?? TimeProvider.System).GetUtcNow().UtcDateTime);
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}

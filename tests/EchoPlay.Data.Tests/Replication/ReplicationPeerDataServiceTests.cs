using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Replication
{
    /// <summary>
    /// Tests für <see cref="ReplicationPeerDataService"/>: Reihenfolge der Geräte und weiches Entfernen.
    /// </summary>
    public sealed class ReplicationPeerDataServiceTests : IDisposable
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        private readonly EchoPlayDbContext _context;

        public ReplicationPeerDataServiceTests()
        {
            _connection.Open();
            _context = new EchoPlayDbContext(new DbContextOptionsBuilder<EchoPlayDbContext>().UseSqlite(_connection).Options);
            _ = _context.Database.EnsureCreated();
        }

        [Fact]
        public async Task GetActive_LastSeenFirst_WithoutRemoved()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            Guid early = Guid.NewGuid();
            Guid late = Guid.NewGuid();
            Guid never = Guid.NewGuid();
            ReplicationPeer removed = new() { DeviceId = Guid.NewGuid() };
            removed.MarkAsDeleted(DateTime.UtcNow);
            _context.ReplicationPeers.AddRange(
                new ReplicationPeer { DeviceId = early, LastSeenUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) },
                new ReplicationPeer { DeviceId = never },
                new ReplicationPeer { DeviceId = late, LastSeenUtc = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc) },
                removed);
            _ = await _context.SaveChangesAsync(ct);

            IReadOnlyList<ReplicationPeer> peers = await new ReplicationPeerDataService(_context).GetActiveAsync(ct);

            Assert.Equal([late, early, never], peers.Select(p => p.DeviceId));
        }

        [Fact]
        public async Task Remove_MarksTheDeviceAsDeleted_Once()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            Guid device = Guid.NewGuid();
            _ = _context.ReplicationPeers.Add(new ReplicationPeer { DeviceId = device });
            _ = await _context.SaveChangesAsync(ct);
            ReplicationPeerDataService service = new(_context, TimeProvider.System);

            Assert.True(await service.RemoveAsync(device, ct));
            Assert.False(await service.RemoveAsync(device, ct));
            Assert.True((await _context.ReplicationPeers.SingleAsync(ct)).IsDeleted);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }
    }
}

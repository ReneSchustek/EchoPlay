using EchoPlay.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für <see cref="ReplicationPeer"/>: eine Zeile je Gerät.
    /// </summary>
    internal sealed class ReplicationPeerConfiguration : IEntityTypeConfiguration<ReplicationPeer>
    {
        /// <summary>Konfiguriert das Schema der Tabelle.</summary>
        /// <param name="builder">Der Entity-Type-Builder.</param>
        public void Configure(EntityTypeBuilder<ReplicationPeer> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.ToTable("ReplicationPeers");
            _ = builder.HasKey(p => p.Id);

            _ = builder.Property(p => p.ExportedStamp).HasMaxLength(64);
            _ = builder.Property(p => p.LastError).HasMaxLength(1024);

            // Ein Gerät steht genau einmal da; gelöschte Zeilen zählen nicht mit.
            _ = builder.HasIndex(p => p.DeviceId)
                   .IsUnique()
                   .HasFilter(SoftDeleteIndexFilter.ActiveRows);
        }
    }
}

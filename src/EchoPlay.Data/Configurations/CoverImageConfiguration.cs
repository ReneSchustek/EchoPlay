using System;
using EchoPlay.Data.Entities.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für die Cover-Tabelle.
    /// Binärdaten sind bewusst von den Metadaten-Tabellen getrennt,
    /// damit normale Queries keine MB an Bilddaten mitladen.
    /// </summary>
    public class CoverImageConfiguration : IEntityTypeConfiguration<CoverImage>
    {
        /// <inheritdoc/>
        public void Configure(EntityTypeBuilder<CoverImage> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.ToTable("CoverImages");

            _ = builder.HasKey(c => c.Id);

            _ = builder.Property(c => c.EntityType)
                   .IsRequired()
                   .HasMaxLength(32);

            _ = builder.Property(c => c.EntityId)
                   .IsRequired();

            // Cover maximal 5 MB – gleicher Schutz wie bisher
            _ = builder.Property(c => c.ImageData)
                   .IsRequired()
                   .HasMaxLength(5_242_880);

            // SHA-256 Hex-String: 64 Zeichen
            _ = builder.Property(c => c.SourceHash)
                   .HasMaxLength(64);

            _ = builder.Property(c => c.SourceUrl)
                   .HasMaxLength(512);

            _ = builder.Property(c => c.LastChecked);

            // Ein Cover pro aktiver Entity – Soft-deleted Einträge zählen nicht mit, sonst blockierten sie den Reinsert.
            _ = builder.HasIndex(c => new { c.EntityType, c.EntityId })
                   .IsUnique()
                   .HasFilter("IsDeleted = 0");

            // Background-Worker: "alle Entities ohne/mit abgelaufenem Check"
            _ = builder.HasIndex(c => new { c.EntityType, c.LastChecked });

            // Purge-Index
            builder.HasPurgeIndex();
        }
    }
}

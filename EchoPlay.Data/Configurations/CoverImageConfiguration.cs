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
            builder.ToTable("CoverImages");

            builder.HasKey(c => c.Id);

            builder.Property(c => c.EntityType)
                   .IsRequired()
                   .HasMaxLength(32);

            builder.Property(c => c.EntityId)
                   .IsRequired();

            // Cover maximal 5 MB – gleicher Schutz wie bisher
            builder.Property(c => c.ImageData)
                   .IsRequired()
                   .HasMaxLength(5_242_880);

            builder.Property(c => c.SourceUrl)
                   .HasMaxLength(512);

            builder.Property(c => c.LastChecked);

            // Ein Cover pro Entity – Duplikate verhindern
            builder.HasIndex(c => new { c.EntityType, c.EntityId })
                   .IsUnique();

            // Background-Worker: "alle Entities ohne/mit abgelaufenem Check"
            builder.HasIndex(c => new { c.EntityType, c.LastChecked });

            // Purge-Index
            builder.HasIndex(c => new { c.IsDeleted, c.DeletedAt })
                   .HasFilter("IsDeleted = 1");
        }
    }
}

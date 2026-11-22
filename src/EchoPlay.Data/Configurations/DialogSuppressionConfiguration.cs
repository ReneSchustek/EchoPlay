using EchoPlay.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für die <see cref="DialogSuppression"/>-Entity.
    /// </summary>
    internal sealed class DialogSuppressionConfiguration : IEntityTypeConfiguration<DialogSuppression>
    {
        /// <summary>
        /// Konfiguriert das Datenbankschema für <see cref="DialogSuppression"/>.
        /// </summary>
        /// <param name="builder">Der Entity-Type-Builder.</param>
        public void Configure(EntityTypeBuilder<DialogSuppression> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.ToTable("DialogSuppressions");
            _ = builder.HasKey(d => d.Id);

            _ = builder.Property(d => d.Key)
                   .IsRequired()
                   .HasMaxLength(64);

            // Fachlicher Unique-Key. Filter auf aktive Zeilen, damit ein zurückgeholter
            // Hinweis erneut ausgeblendet werden kann (gleiche Linie wie SecureSettings).
            _ = builder.HasIndex(d => d.Key)
                   .IsUnique()
                   .HasFilter(SoftDeleteIndexFilter.ActiveRows);

            builder.HasPurgeIndex();
        }
    }
}

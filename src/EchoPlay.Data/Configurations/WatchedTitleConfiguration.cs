using EchoPlay.Data.Entities.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für die <see cref="WatchedTitle"/>-Entity.
    /// </summary>
    internal sealed class WatchedTitleConfiguration : IEntityTypeConfiguration<WatchedTitle>
    {
        /// <summary>
        /// Konfiguriert das Datenbankschema für <see cref="WatchedTitle"/>.
        /// </summary>
        /// <param name="builder">Der Entity-Type-Builder.</param>
        public void Configure(EntityTypeBuilder<WatchedTitle> builder)
        {
            _ = builder.ToTable("WatchedTitles");

            _ = builder.Property(w => w.Title)
                .IsRequired()
                .HasMaxLength(512);

            _ = builder.Property(w => w.NormalizedTitle)
                .IsRequired()
                .HasMaxLength(512);

            // Fachlicher Unique-Key. Vergessene Titel löscht der Dienst physisch; der Filter auf
            // aktive Zeilen ist deshalb ohne Wirkung und hält nur die Form der übrigen Merklisten.
            _ = builder.HasIndex(w => w.NormalizedTitle)
                .IsUnique()
                .HasFilter(SoftDeleteIndexFilter.ActiveRows);
        }
    }
}

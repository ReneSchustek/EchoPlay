using System.Diagnostics.CodeAnalysis;
using EchoPlay.Data.Entities.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für die <see cref="WatchedTitle"/>-Entity.
    /// </summary>
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core instanziiert IEntityTypeConfiguration-Implementierungen zur Modell-Erstellung via ApplyConfigurationsFromAssembly-Reflection.")]
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

            // Fachlicher Unique-Key. Filter auf aktive Zeilen, damit ein früher entfernter
            // Titel erneut gemerkt werden kann (gleiche Linie wie CoverImages/SecureSettings).
            _ = builder.HasIndex(w => w.NormalizedTitle)
                .IsUnique()
                .HasFilter("IsDeleted = 0");
        }
    }
}

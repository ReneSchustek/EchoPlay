using System.Diagnostics.CodeAnalysis;
using EchoPlay.Data.Entities.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für die <see cref="CachedNewRelease"/>-Entity.
    /// Definiert Tabellenname, Spalteneinschränkungen, Indizes und die Beziehung zur Serie.
    /// </summary>
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core instanziiert IEntityTypeConfiguration-Implementierungen zur Modell-Erstellung via ApplyConfigurationsFromAssembly-Reflection.")]
    internal sealed class CachedNewReleaseConfiguration : IEntityTypeConfiguration<CachedNewRelease>
    {
        /// <summary>
        /// Konfiguriert das Datenbankschema für <see cref="CachedNewRelease"/>.
        /// </summary>
        /// <param name="builder">Der Entity-Type-Builder.</param>
        public void Configure(EntityTypeBuilder<CachedNewRelease> builder)
        {
            _ = builder.ToTable("CachedNewReleases");

            _ = builder.Property(c => c.Title)
                .IsRequired()
                .HasMaxLength(512);

            _ = builder.Property(c => c.CoverUrl)
                .HasMaxLength(512);

            // Fachlicher Unique-Key: iTunes-Collection-ID identifiziert ein Album eindeutig.
            // Verhindert Duplikate beim wiederholten Abruf derselben Serie.
            _ = builder.HasIndex(c => c.CollectionId)
                .IsUnique();

            // Kombi-Index für schnelle Abfragen: "alle Neuerscheinungen einer Serie im Zeitfenster"
            _ = builder.HasIndex(c => new { c.SeriesId, c.ReleaseDate });

            // Beziehung zur Serie: Cascade Delete – wenn eine Serie gelöscht wird,
            // werden auch ihre gecachten Neuerscheinungen entfernt.
            _ = builder.HasOne(c => c.Series)
                .WithMany()
                .HasForeignKey(c => c.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}

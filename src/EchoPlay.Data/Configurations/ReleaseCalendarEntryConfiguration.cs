using EchoPlay.Data.Entities.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für <see cref="ReleaseCalendarEntry"/>: Tabelle, Längen,
    /// Indizes und die Beziehung zur Serie.
    /// </summary>
    internal sealed class ReleaseCalendarEntryConfiguration : IEntityTypeConfiguration<ReleaseCalendarEntry>
    {
        /// <summary>Konfiguriert das Schema der Tabelle.</summary>
        /// <param name="builder">Der Entity-Type-Builder.</param>
        public void Configure(EntityTypeBuilder<ReleaseCalendarEntry> builder)
        {
            _ = builder.ToTable("ReleaseCalendarEntries");

            // Dieselben Längen wie bei den Neuerscheinungen; beide Werte kommen aus derselben Quelle.
            _ = builder.Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(512);

            _ = builder.Property(e => e.CoverUrl)
                .HasMaxLength(512);

            // Ein Album steht nur einmal im Kalender, auch wenn eine zweite Prüfung es erneut liefert.
            _ = builder.HasIndex(e => e.CollectionId)
                .IsUnique();

            // Die Ansicht fragt immer einen Monat ab.
            _ = builder.HasIndex(e => e.ReleaseDate);

            // Mit der Serie verschwinden auch ihre Kalendereinträge.
            _ = builder.HasOne(e => e.Series)
                .WithMany()
                .HasForeignKey(e => e.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}

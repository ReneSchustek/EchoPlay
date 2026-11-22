using System;
using EchoPlay.Data.Entities.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für den Wiedergabestatus einer Episode.
    /// </summary>
    internal sealed class PlaybackStateConfiguration : IEntityTypeConfiguration<PlaybackState>
    {
        /// <inheritdoc/>
        public void Configure(EntityTypeBuilder<PlaybackState> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.ToTable("PlaybackStates");

            _ = builder.HasKey(p => p.Id);

            _ = builder.Property(p => p.LastPosition)
                   .IsRequired();

            _ = builder.Property(p => p.LastPlayedAt);

            _ = builder.Property(p => p.CompletedAt);

            // Restrict statt Cascade, da das Projekt ausschließlich Soft-Delete verwendet.
            // Physisches Löschen einer Episode darf nicht automatisch PlaybackStates entfernen.
            _ = builder.HasOne(p => p.Episode)
                   .WithOne()
                   .HasForeignKey<PlaybackState>(p => p.EpisodeId)
                   .OnDelete(DeleteBehavior.Restrict);

            _ = builder.HasIndex(p => p.EpisodeId)
                   .IsUnique();

            // Dashboard und StatusBar fragen "welche Episoden sind gehört?".
            // Bewusst ohne Löschfilter: Als Teilindex schließt er die gelöschten Zeilen aus, und
            // SQLite ging dann bei jedem Durchlauf über alle Hörstände den Index statt der Tabelle
            // durch und holte jede Zeile einzeln nach. Das machte „Zuletzt gehört“ und das Laden
            // aller Hörstände messbar langsamer, ohne GetCompletedEpisodeIdsAsync() schneller zu
            // machen (gemessen an einer Kopie der Produktivdatenbank, 20.11.2026).
            _ = builder.HasIndex(p => new { p.IsCompleted, p.EpisodeId });

            // Dashboard "Zuletzt gehört" sortiert nach LastPlayedAt, der Index spart Full-Table-Scan + Sort.
            _ = builder.HasIndex(p => p.LastPlayedAt)
                   .HasFilter(SoftDeleteIndexFilter.ActiveRows + " AND LastPlayedAt IS NOT NULL");

            // Purge-Index für DatabaseMaintenanceService
            builder.HasPurgeIndex();
        }
    }
}

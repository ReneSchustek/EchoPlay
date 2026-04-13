using System;
using EchoPlay.Data.Entities.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für den Wiedergabestatus einer Episode.
    /// </summary>
    public class PlaybackStateConfiguration : IEntityTypeConfiguration<PlaybackState>
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

            // Dashboard und StatusBar fragen "welche Episoden sind gehört?" –
            // Kombi-Index beschleunigt GetCompletedEpisodeIdsAsync().
            _ = builder.HasIndex(p => new { p.IsCompleted, p.EpisodeId })
                   .HasFilter("IsDeleted = 0");

            // Purge-Index für DatabaseMaintenanceService
            _ = builder.HasIndex(p => new { p.IsDeleted, p.DeletedAt })
                   .HasFilter("IsDeleted = 1");
        }
    }
}

using System;
using EchoPlay.Data.Entities.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für Episoden innerhalb einer Hörspielserie.
    /// </summary>
    public class EpisodeConfiguration : IEntityTypeConfiguration<Episode>
    {
        /// <inheritdoc/>
        public void Configure(EntityTypeBuilder<Episode> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.ToTable("Episodes");

            _ = builder.HasKey(e => e.Id);

            _ = builder.Property(e => e.Title)
                   .IsRequired()
                   .HasMaxLength(256);

            _ = builder.Property(e => e.EpisodeNumber);

            _ = builder.Property(e => e.Duration)
                   .IsRequired();

            _ = builder.Property(e => e.ReleaseDate);

            _ = builder.Property(e => e.LocalFolderPath)
                   .HasMaxLength(512);

            _ = builder.Property(e => e.ProviderUrl)
                   .HasMaxLength(512);

            _ = builder.Property(e => e.LocalTrackCount);

            _ = builder.Property(e => e.TrackMatchKind)
                   .IsRequired()
                   .HasDefaultValue(TrackMatchKind.NotMatched);

            _ = builder.Property(e => e.SpotifyAlbumId)
                   .HasMaxLength(64);

            _ = builder.Property(e => e.AppleMusicAlbumId)
                   .HasMaxLength(64);

            // Restrict statt Cascade, da das Projekt ausschließlich Soft-Delete verwendet.
            // Physisches Löschen einer Serie darf nicht automatisch Episoden entfernen.
            _ = builder.HasOne(e => e.Series)
                   .WithMany()
                   .HasForeignKey(e => e.SeriesId)
                   .OnDelete(DeleteBehavior.Restrict);

            _ = builder.HasIndex(e => new { e.SeriesId, e.EpisodeNumber });

            // Lokal-Bibliothek: "fehlende lokale Episoden" werden über SeriesId + LocalFolderPath gesucht.
            // Ohne diesen Index ist GetMissingLocalEpisodesAsync ein Full-Table-Scan.
            _ = builder.HasIndex(e => new { e.SeriesId, e.LocalFolderPath })
                   .HasFilter("IsDeleted = 0");

            // Dashboard "Neuerscheinungen" + zukünftige Sortier-Pfade lesen nach ReleaseDate.
            _ = builder.HasIndex(e => e.ReleaseDate)
                   .HasFilter("IsDeleted = 0 AND ReleaseDate IS NOT NULL");

            // Metadaten-Lookup: Spotify sucht Episoden über die AlbumId, um Duplikate zu vermeiden.
            _ = builder.HasIndex(e => e.SpotifyAlbumId)
                   .HasFilter("SpotifyAlbumId IS NOT NULL");

            // Metadaten-Lookup: Apple Music sucht Episoden über die AlbumId.
            _ = builder.HasIndex(e => e.AppleMusicAlbumId)
                   .HasFilter("AppleMusicAlbumId IS NOT NULL");

            // Purge-Index für DatabaseMaintenanceService
            _ = builder.HasIndex(e => new { e.IsDeleted, e.DeletedAt })
                   .HasFilter("IsDeleted = 1");
        }
    }
}

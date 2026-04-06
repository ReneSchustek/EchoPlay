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
            builder.ToTable("Episodes");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Title)
                   .IsRequired()
                   .HasMaxLength(256);

            builder.Property(e => e.EpisodeNumber);

            builder.Property(e => e.Duration)
                   .IsRequired();

            builder.Property(e => e.ReleaseDate);

            builder.Property(e => e.LocalFolderPath)
                   .HasMaxLength(512);

            builder.Property(e => e.ProviderUrl)
                   .HasMaxLength(512);

            builder.Property(e => e.LocalTrackCount);

            builder.Property(e => e.TrackMatchKind)
                   .IsRequired()
                   .HasDefaultValue(TrackMatchKind.NotMatched);

            // Coverbilder auf maximal 5 MB begrenzen – gleicher Schutz wie bei Series
            builder.Property(e => e.LocalCoverData)
                   .HasMaxLength(5_242_880);

            // Restrict statt Cascade, da das Projekt ausschließlich Soft-Delete verwendet.
            // Physisches Löschen einer Serie darf nicht automatisch Episoden entfernen.
            builder.HasOne(e => e.Series)
                   .WithMany()
                   .HasForeignKey(e => e.SeriesId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(e => new { e.SeriesId, e.EpisodeNumber });

            // Lokal-Bibliothek: "fehlende lokale Episoden" werden über SeriesId + LocalFolderPath gesucht.
            // Ohne diesen Index ist GetMissingLocalEpisodesAsync ein Full-Table-Scan.
            builder.HasIndex(e => new { e.SeriesId, e.LocalFolderPath })
                   .HasFilter("IsDeleted = 0");

            // Purge-Index für DatabaseMaintenanceService
            builder.HasIndex(e => new { e.IsDeleted, e.DeletedAt })
                   .HasFilter("IsDeleted = 1");
        }
    }
}
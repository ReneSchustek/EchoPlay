using EchoPlay.Data.Entities.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für lokale Audiodateien einer Episode.
    /// </summary>
    internal sealed class LocalTrackConfiguration : IEntityTypeConfiguration<LocalTrack>
    {
        /// <summary>
        /// Konfiguriert das Datenbankschema für <see cref="LocalTrack"/>.
        /// </summary>
        /// <param name="builder">Der Entity-Type-Builder.</param>
        public void Configure(EntityTypeBuilder<LocalTrack> builder)
        {
            builder.ToTable("LocalTracks");

            builder.HasKey(t => t.Id);

            builder.Property(t => t.FilePath)
                .IsRequired()
                .HasMaxLength(512);

            builder.Property(t => t.TrackNumber)
                .IsRequired();

            builder.Property(t => t.Duration)
                .IsRequired();

            // Restrict statt Cascade – LocalTracks dürfen nicht physisch gelöscht werden,
            // wenn eine Episode physisch gelöscht würde. Das Projekt verwendet ausschließlich Soft-Delete.
            builder.HasOne(t => t.Episode)
                .WithMany()
                .HasForeignKey(t => t.EpisodeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Kombi-Index ersetzt den bisherigen einfachen EpisodeId-Index:
            // GetByEpisodeIdAsync() sortiert immer nach TrackNumber – der Index deckt beides ab.
            builder.HasIndex(t => new { t.EpisodeId, t.TrackNumber });

            // Purge-Index für DatabaseMaintenanceService
            builder.HasIndex(t => new { t.IsDeleted, t.DeletedAt })
                   .HasFilter("IsDeleted = 1");
        }
    }
}

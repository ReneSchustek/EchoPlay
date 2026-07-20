using System.Diagnostics.CodeAnalysis;
using EchoPlay.Data.Entities.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für lokale Audiodateien einer Episode.
    /// </summary>
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core instanziiert IEntityTypeConfiguration-Implementierungen zur Modell-Erstellung via ApplyConfigurationsFromAssembly-Reflection.")]
    internal sealed class LocalTrackConfiguration : IEntityTypeConfiguration<LocalTrack>
    {
        /// <summary>
        /// Konfiguriert das Datenbankschema für <see cref="LocalTrack"/>.
        /// </summary>
        /// <param name="builder">Der Entity-Type-Builder.</param>
        public void Configure(EntityTypeBuilder<LocalTrack> builder)
        {
            _ = builder.ToTable("LocalTracks");

            _ = builder.HasKey(t => t.Id);

            _ = builder.Property(t => t.FilePath)
                .IsRequired()
                .HasMaxLength(512);

            _ = builder.Property(t => t.TrackNumber)
                .IsRequired();

            _ = builder.Property(t => t.Duration)
                .IsRequired();

            // Restrict statt Cascade – LocalTracks dürfen nicht physisch gelöscht werden,
            // wenn eine Episode physisch gelöscht würde. Das Projekt verwendet ausschließlich Soft-Delete.
            _ = builder.HasOne(t => t.Episode)
                .WithMany()
                .HasForeignKey(t => t.EpisodeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Kombi-Index ersetzt den bisherigen einfachen EpisodeId-Index:
            // GetByEpisodeIdAsync() sortiert immer nach TrackNumber – der Index deckt beides ab.
            _ = builder.HasIndex(t => new { t.EpisodeId, t.TrackNumber });

            // Purge-Index für DatabaseMaintenanceService
            _ = builder.HasIndex(t => new { t.IsDeleted, t.DeletedAt })
                   .HasFilter("IsDeleted = 1");
        }
    }
}

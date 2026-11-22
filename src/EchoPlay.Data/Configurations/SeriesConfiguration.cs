using EchoPlay.Data.Entities.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für die Series-Entität.
    /// Beschreibt, wie Hörspielserien in der Datenbank gespeichert werden.
    /// </summary>
    internal sealed class SeriesConfiguration : IEntityTypeConfiguration<Series>
    {
        /// <inheritdoc/>
        public void Configure(EntityTypeBuilder<Series> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.ToTable("Series");

            _ = builder.HasKey(s => s.Id);

            ConfigureProperties(builder);
            ConfigureIndexes(builder);

            // Purge-Index: DatabaseMaintenanceService filtert auf IsDeleted + DeletedAt.
            // Partial Index nur für gelöschte Einträge, das spart Speicher im Normalfall.
            builder.HasPurgeIndex();
        }

        private static void ConfigureProperties(EntityTypeBuilder<Series> builder)
        {
            // IsRequired() entspricht NOT NULL in SQL. HasMaxLength() steht im Modell und in
            // der Migration; SQLite selbst erzwingt die Länge nicht.
            _ = builder.Property(s => s.Title)
                   .IsRequired()
                   .HasMaxLength(256);

            _ = builder.Property(s => s.Description)
                   .HasMaxLength(2000);

            _ = builder.Property(s => s.CoverImageUrl)
                   .HasMaxLength(512);

            _ = builder.Property(s => s.SpotifyArtistId)
                   .HasMaxLength(64);

            _ = builder.Property(s => s.AppleMusicArtistId)
                   .HasMaxLength(64);

            _ = builder.Property(s => s.LocalFolderPath)
                   .HasMaxLength(512);

            _ = builder.Property(s => s.FolderPattern)
                   .HasMaxLength(256);
        }

        private static void ConfigureIndexes(EntityTypeBuilder<Series> builder)
        {
            // Index auf Title beschleunigt die häufige ORDER BY Title-Abfrage in GetAllAsync.
            _ = builder.HasIndex(s => s.Title);

            // Abonnierte Serien werden auf dem Dashboard und in der StatusBar gezählt;
            // der Kombi-Index vermeidet Full-Table-Scan bei GetSubscribedAsync().
            _ = builder.HasIndex(s => new { s.IsSubscribed, s.Title })
                   .HasFilter(SoftDeleteIndexFilter.ActiveRows);

            // Favoriten-Bereich auf dem Dashboard: gleicher Zugriffspfad wie Subscribed.
            _ = builder.HasIndex(s => new { s.IsFavorite, s.Title })
                   .HasFilter(SoftDeleteIndexFilter.ActiveRows);

            // Metadaten-Lookup: Spotify sucht Serien über die ArtistId, um Duplikate zu vermeiden.
            _ = builder.HasIndex(s => s.SpotifyArtistId)
                   .HasFilter("SpotifyArtistId IS NOT NULL");

            // Metadaten-Lookup: Apple Music sucht Serien über die ArtistId.
            _ = builder.HasIndex(s => s.AppleMusicArtistId)
                   .HasFilter("AppleMusicArtistId IS NOT NULL");

            // Online-Mediathek: nur Serien mit IsOnlineImported anzeigen.
            _ = builder.HasIndex(s => new { s.IsOnlineImported, s.Title })
                   .HasFilter(SoftDeleteIndexFilter.ActiveRows);
        }
    }
}

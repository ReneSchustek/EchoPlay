using EchoPlay.Data.Entities.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für die Series-Entität.
    /// Beschreibt, wie Hörspielserien in der Datenbank gespeichert werden.
    /// </summary>
    public class SeriesConfiguration : IEntityTypeConfiguration<Series>
    {
        /// <inheritdoc/>
        public void Configure(EntityTypeBuilder<Series> builder)
        {
            builder.ToTable("Series");

            // Primärschlüssel – EF Core würde "Id" auch ohne explizite Angabe erkennen,
            // aber die explizite Konfiguration macht die Absicht klar.
            builder.HasKey(s => s.Id);

            // IsRequired() entspricht NOT NULL in SQL.
            // HasMaxLength() legt die Spaltenlänge in SQLite fest und verhindert
            // versehentlich riesige Strings (z.B. durch fehlerhafte API-Antworten).
            builder.Property(s => s.Title)
                   .IsRequired()
                   .HasMaxLength(256);

            builder.Property(s => s.Description)
                   .HasMaxLength(2000);

            builder.Property(s => s.CoverImageUrl)
                   .HasMaxLength(512);

            builder.Property(s => s.SpotifyArtistId)
                   .HasMaxLength(128);

            builder.Property(s => s.AppleMusicArtistId)
                   .HasMaxLength(128);

            builder.Property(s => s.LocalFolderPath)
                   .HasMaxLength(512);

            builder.Property(s => s.FolderPattern)
                   .HasMaxLength(256);

            // Coverbilder auf maximal 5 MB begrenzen – Schutz gegen DB-Bloat
            // durch versehentlich überdimensionierte Bilder (z.B. unkomprimierte PNGs).
            builder.Property(s => s.LocalCoverData)
                   .HasMaxLength(5_242_880);

            // Index auf Title beschleunigt die häufige ORDER BY Title-Abfrage in GetAllAsync.
            builder.HasIndex(s => s.Title);

            // Abonnierte Serien werden auf dem Dashboard und in der StatusBar gezählt –
            // Kombi-Index vermeidet Full-Table-Scan bei GetSubscribedAsync().
            builder.HasIndex(s => new { s.IsSubscribed, s.Title })
                   .HasFilter("IsDeleted = 0");

            // Favoriten-Bereich auf dem Dashboard: gleicher Zugriffspfad wie Subscribed.
            builder.HasIndex(s => new { s.IsFavorite, s.Title })
                   .HasFilter("IsDeleted = 0");

            // Metadaten-Lookup: Spotify sucht Serien über die ArtistId, um Duplikate zu vermeiden.
            builder.HasIndex(s => s.SpotifyArtistId)
                   .HasFilter("SpotifyArtistId IS NOT NULL");

            // Metadaten-Lookup: Apple Music sucht Serien über die ArtistId.
            builder.HasIndex(s => s.AppleMusicArtistId)
                   .HasFilter("AppleMusicArtistId IS NOT NULL");

            // Online-Mediathek: nur Serien mit IsOnlineImported anzeigen.
            builder.HasIndex(s => new { s.IsOnlineImported, s.Title })
                   .HasFilter("IsDeleted = 0");

            // Purge-Index: DatabaseMaintenanceService filtert auf IsDeleted + DeletedAt.
            // Partial Index nur für gelöschte Einträge – spart Speicher im Normalfall.
            builder.HasIndex(s => new { s.IsDeleted, s.DeletedAt })
                   .HasFilter("IsDeleted = 1");
        }
    }
}
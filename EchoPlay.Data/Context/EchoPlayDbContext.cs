using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Context
{
    /// <summary>
    /// Zentrale EF-Core-Datenbankkontextklasse für EchoPlay.
    /// Konfiguriert das Mapping und implementiert globale Abfragefilter für das Soft-Delete-System.
    ///
    /// Der Primary-Constructor-Syntax (C# 12) übergibt <paramref name="options"/> direkt
    /// an <see cref="DbContext"/> – kein separater Konstruktorkörper notwendig.
    /// </summary>
    public class EchoPlayDbContext(DbContextOptions<EchoPlayDbContext> options) : DbContext(options)
    {
        // DbSet-Properties als Expression-Body (=> Set<T>()): EF Core gibt intern immer
        // dieselbe DbSet-Instanz zurück – kein Backing-Field notwendig.

        /// <summary>Serien.</summary>
        public DbSet<Series> Series => Set<Series>();

        /// <summary>Episoden.</summary>
        public DbSet<Episode> Episodes => Set<Episode>();

        /// <summary>Wiedergabestände.</summary>
        public DbSet<PlaybackState> PlaybackStates => Set<PlaybackState>();

        /// <summary>Verschlüsselte Einstellungswerte (z. B. Spotify-Credentials).</summary>
        public DbSet<SecureSetting> SecureSettings => Set<SecureSetting>();

        /// <summary>Lokale Audio-Tracks.</summary>
        public DbSet<LocalTrack> LocalTracks => Set<LocalTrack>();

        /// <summary>Anwendungseinstellungen.</summary>
        public DbSet<AppSettings> AppSettings => Set<AppSettings>();

        /// <summary>Benutzerdefinierte Sortierposition von Serien in Dashboard-Bereichen.</summary>
        public DbSet<DashboardPosition> DashboardPositions => Set<DashboardPosition>();

        /// <summary>Gecachte Neuerscheinungen aus der iTunes-API.</summary>
        public DbSet<CachedNewRelease> CachedNewReleases => Set<CachedNewRelease>();

        /// <summary>Cover-Binärdaten, getrennt von Metadaten-Tabellen.</summary>
        public DbSet<CoverImage> CoverImages => Set<CoverImage>();

        /// <summary>
        /// Konfiguriert das Modell beim Erstellen.
        /// Wendet Fluent-API-Konfigurationen und globale Filter an.
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            base.OnModelCreating(modelBuilder);

            // Sucht automatisch alle IEntityTypeConfiguration<T>-Klassen im Assembly
            // und wendet sie an – jede Entität hat eine eigene Konfigurationsklasse.
            _ = modelBuilder.ApplyConfigurationsFromAssembly(typeof(EchoPlayDbContext).Assembly);

            // Globaler Query Filter: Jede Abfrage bekommt automatisch "AND IsDeleted = 0"
            // angehängt. Soft-Delete ist damit transparent – kein Aufrufer muss daran denken.
            _ = modelBuilder.Entity<Series>().HasQueryFilter(entity => !entity.IsDeleted);
            _ = modelBuilder.Entity<Episode>().HasQueryFilter(entity => !entity.IsDeleted);
            _ = modelBuilder.Entity<PlaybackState>().HasQueryFilter(entity => !entity.IsDeleted);
            _ = modelBuilder.Entity<LocalTrack>().HasQueryFilter(entity => !entity.IsDeleted);
            _ = modelBuilder.Entity<AppSettings>().HasQueryFilter(entity => !entity.IsDeleted);
            _ = modelBuilder.Entity<DashboardPosition>().HasQueryFilter(entity => !entity.IsDeleted);
            _ = modelBuilder.Entity<CachedNewRelease>().HasQueryFilter(entity => !entity.IsDeleted);
            _ = modelBuilder.Entity<CoverImage>().HasQueryFilter(entity => !entity.IsDeleted);
            _ = modelBuilder.Entity<SecureSetting>().HasQueryFilter(entity => !entity.IsDeleted);
        }
    }
}
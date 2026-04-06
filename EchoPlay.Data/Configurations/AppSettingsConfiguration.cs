using EchoPlay.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für die <see cref="AppSettings"/>-Entity.
    /// Stellt sicher, dass Einstellungen persistent und konsistent gespeichert werden.
    /// </summary>
    internal sealed class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettings>
    {
        /// <summary>
        /// Konfiguriert das Datenbankschema für <see cref="AppSettings"/>.
        /// </summary>
        /// <param name="builder">Der Entity-Type-Builder.</param>
        public void Configure(EntityTypeBuilder<AppSettings> builder)
        {
            builder.ToTable("AppSettings");

            // EpisodeFolderPattern ist Pflicht – ohne Muster kann kein Ordner korrekt zugeordnet werden
            builder.Property(settings => settings.EpisodeFolderPattern)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(settings => settings.LocalLibraryRootPath)
                .HasMaxLength(512);

            // Theme-Name: kurzer String, maximal 64 Zeichen für spätere Erweiterungen
            builder.Property(settings => settings.ActiveTheme)
                .IsRequired()
                .HasMaxLength(64);

            // Sprachcode: BCP-47-Format ("de", "en") – 16 Zeichen für mögliche Erweiterungen wie "zh-Hans"
            builder.Property(settings => settings.ActiveLanguage)
                .IsRequired()
                .HasMaxLength(16);

            builder.Property(settings => settings.LastOpenedPlayerFolder)
                .HasMaxLength(512);
        }
    }
}

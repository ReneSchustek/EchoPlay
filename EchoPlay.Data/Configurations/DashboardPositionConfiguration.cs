using EchoPlay.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für die <see cref="DashboardPosition"/>-Entity.
    /// Stellt einen eindeutigen Index auf SeriesId + Section sicher, damit
    /// pro Serie und Bereich maximal eine Position existiert.
    /// </summary>
    internal sealed class DashboardPositionConfiguration : IEntityTypeConfiguration<DashboardPosition>
    {
        /// <summary>
        /// Konfiguriert das Datenbankschema für <see cref="DashboardPosition"/>.
        /// </summary>
        /// <param name="builder">Der Entity-Type-Builder.</param>
        public void Configure(EntityTypeBuilder<DashboardPosition> builder)
        {
            builder.ToTable("DashboardPositions");

            builder.Property(dp => dp.Section)
                .IsRequired()
                .HasMaxLength(64);

            // Fachlicher Schlüssel: pro Serie und Bereich nur eine Position
            builder.HasIndex(dp => new { dp.SeriesId, dp.Section })
                .IsUnique();

            // Abfrage nach Bereich: GetBySectionAsync() filtert auf Section und sortiert nach Position.
            builder.HasIndex(dp => new { dp.Section, dp.Position })
                .HasFilter("IsDeleted = 0");

            // Fremdschlüssel-Beziehung zur Serie – bisher fehlte die explizite FK-Konfiguration.
            // Restrict: Eine Serie darf nicht physisch gelöscht werden, solange Positionen existieren.
            builder.HasOne(dp => dp.Series)
                .WithMany()
                .HasForeignKey(dp => dp.SeriesId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}

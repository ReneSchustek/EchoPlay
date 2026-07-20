using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// Gemeinsame Bausteine für EF-Core-Entity-Konfigurationen.
    /// </summary>
    internal static class EntityConfigurationExtensions
    {
        /// <summary>
        /// Legt den Purge-Index an: gefilterter Index auf <c>(IsDeleted, DeletedAt)</c> für
        /// nur logisch gelöschte Zeilen. Der <c>DatabaseMaintenanceService</c> nutzt ihn beim
        /// endgültigen Entfernen. Identisch für alle Soft-Delete-Entitäten.
        /// </summary>
        /// <typeparam name="T">Der Entity-Typ.</typeparam>
        /// <param name="builder">Der Entity-Type-Builder.</param>
        public static void HasPurgeIndex<T>(this EntityTypeBuilder<T> builder)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.HasIndex("IsDeleted", "DeletedAt")
                   .HasFilter("IsDeleted = 1");
        }
    }
}

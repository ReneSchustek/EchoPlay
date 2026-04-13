using EchoPlay.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Context
{
    /// <summary>
    /// Initialisierer für die Datenbank-Infrastruktur während des Anwendungsstarts.
    /// </summary>
    public static class DbContextInitializer
    {
        /// <summary>
        /// Führt ausstehende Migrationen aus und stellt sicher, dass die Datenbank bereit ist.
        /// </summary>
        /// <param name="context">Der zu initialisierende Datenbankkontext.</param>
        public static async Task InitializeAsync(EchoPlayDbContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // Wir nutzen MigrateAsync statt EnsureCreated, um zukünftige Schema-Änderungen
            // versioniert auf bestehende Installationen anwenden zu können.
            await context.Database.MigrateAsync().ConfigureAwait(false);
        }
    }
}
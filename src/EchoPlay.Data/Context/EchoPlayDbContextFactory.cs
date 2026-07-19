using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EchoPlay.Data.Context
{
    /// <summary>
    /// Designzeit-Fabrik für den DbContext. 
    /// Ermöglicht es den Entity Framework Core Tools (z.B. Migrations), 
    /// den Kontext ohne eine laufende Applikation zu instanziieren.
    /// </summary>
    public class EchoPlayDbContextFactory : IDesignTimeDbContextFactory<EchoPlayDbContext>
    {
        /// <inheritdoc/>
        public EchoPlayDbContext CreateDbContext(string[] args)
        {
            DbContextOptionsBuilder<EchoPlayDbContext> optionsBuilder = new();
            string dbPath = DatabasePathProvider.GetDatabasePath();

            // Explizite Konfiguration für SQLite während der Migrations-Erstellung.
            _ = optionsBuilder.UseSqlite($"Data Source={dbPath}");

            return new(optionsBuilder.Options);
        }
    }
}

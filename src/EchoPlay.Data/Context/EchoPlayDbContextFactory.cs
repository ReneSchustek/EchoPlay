using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EchoPlay.Data.Context
{
    /// <summary>
    /// Designzeit-Fabrik für den DbContext. Die EF-Core-Werkzeuge (<c>dotnet ef migrations</c>)
    /// erzeugen den Kontext darüber, ohne die Anwendung und ihren DI-Container zu starten.
    /// </summary>
    public sealed class EchoPlayDbContextFactory : IDesignTimeDbContextFactory<EchoPlayDbContext>
    {
        /// <inheritdoc/>
        public EchoPlayDbContext CreateDbContext(string[] args)
        {
            DbContextOptionsBuilder<EchoPlayDbContext> optionsBuilder = new();
            string dbPath = DatabasePathProvider.PrepareDatabasePath();

            // Ohne Interceptor und ohne Cache=Shared: Die Werkzeuge vergleichen nur das Modell mit
            // dem Schnappschuss; die Laufzeit-Einstellungen aus AddEchoPlayData wirken hier nicht.
            _ = optionsBuilder.UseSqlite($"Data Source={dbPath}");

            return new(optionsBuilder.Options);
        }
    }
}

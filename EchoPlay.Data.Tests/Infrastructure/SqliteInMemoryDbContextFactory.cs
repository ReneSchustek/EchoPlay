using EchoPlay.Data.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Infrastructure
{
    /// <summary>
    /// Erzeugt Datenbankkontexte auf Basis von SQLite im Arbeitsspeicher für relationale Test-Szenarien.
    /// </summary>
    public static class SqliteInMemoryDbContextFactory
    {
        /// <summary>
        /// Erstellt und öffnet eine SQLite-In-Memory-Datenbank.
        /// </summary>
        /// <returns>Ein konfigurierter <see cref="EchoPlayDbContext"/>.</returns>
        public static EchoPlayDbContext Create()
        {
            // SQLite benötigt eine offene Verbindung, damit die In-Memory-DB nicht sofort gelöscht wird.
            SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            DbContextOptionsBuilder<EchoPlayDbContext> builder = new();
            builder.UseSqlite(connection);

            EchoPlayDbContext context = new(builder.Options);
            context.Database.EnsureCreated();

            return context;
        }
    }
}
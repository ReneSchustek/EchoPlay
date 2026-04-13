using EchoPlay.Data.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Data.Tests.Infrastructure
{
    /// <summary>
    /// Erzeugt Datenbankkontexte auf Basis von SQLite im Arbeitsspeicher für relationale Test-Szenarien.
    /// </summary>
    [SuppressMessage("Design", "CA1515:Consider making public types internal",
        Justification = "Gemeinsam mit DbTestBase und TestDataBuilder als public Test-Infrastruktur gehalten.")]
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
            _ = builder.UseSqlite(connection)
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

            EchoPlayDbContext context = new(builder.Options);
            _ = context.Database.EnsureCreated();

            return context;
        }
    }
}
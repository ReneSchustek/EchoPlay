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
            return Create(interceptor: null);
        }

        /// <summary>
        /// Erstellt eine SQLite-In-Memory-Datenbank mit angehängtem Command-Interceptor.
        /// Wird für Akzeptanztests genutzt, die die Anzahl ausgeführter SELECTs prüfen.
        /// </summary>
        /// <param name="interceptor">
        /// Optionaler Interceptor; <c>null</c> entspricht <see cref="Create()"/>.
        /// </param>
        public static EchoPlayDbContext Create(CountingCommandInterceptor? interceptor)
        {
            // SQLite benötigt eine offene Verbindung, damit die In-Memory-DB nicht sofort gelöscht wird.
            SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            DbContextOptionsBuilder<EchoPlayDbContext> builder = new();
            _ = builder.UseSqlite(connection)
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

            if (interceptor is not null)
            {
                _ = builder.AddInterceptors(interceptor);
            }

            EchoPlayDbContext context = new(builder.Options);
            _ = context.Database.EnsureCreated();

            return context;
        }
    }
}

using EchoPlay.Data.Context;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Data.Tests.Infrastructure
{
    /// <summary>
    /// Erzeugt Datenbankkontexte für schnelle Unit-Tests unter Verwendung des EF Core In-Memory-Providers.
    /// </summary>
    [SuppressMessage("Design", "CA1515:Consider making public types internal",
        Justification = "Gemeinsam mit DbTestBase und TestDataBuilder als public Test-Infrastruktur gehalten.")]
    public static class InMemoryDbContextFactory
    {
        /// <summary>
        /// Erstellt eine frische Instanz des <see cref="EchoPlayDbContext"/> mit isoliertem Speicher.
        /// </summary>
        /// <returns>Ein einsatzbereiter Datenbankkontext.</returns>
        public static EchoPlayDbContext Create()
        {
            // Die GUID verhindert, dass parallele Tests in denselben Speicherbereich schreiben.
            DbContextOptionsBuilder<EchoPlayDbContext> builder = new();
            _ = builder.UseInMemoryDatabase(Guid.NewGuid().ToString());

            EchoPlayDbContext context = new(builder.Options);
            _ = context.Database.EnsureCreated();

            return context;
        }
    }
}
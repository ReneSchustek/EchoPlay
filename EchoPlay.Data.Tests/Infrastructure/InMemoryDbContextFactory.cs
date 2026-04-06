using EchoPlay.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Infrastructure
{
    /// <summary>
    /// Erzeugt Datenbankkontexte für schnelle Unit-Tests unter Verwendung des EF Core In-Memory-Providers.
    /// </summary>
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
            builder.UseInMemoryDatabase(Guid.NewGuid().ToString());

            EchoPlayDbContext context = new(builder.Options);
            context.Database.EnsureCreated();

            return context;
        }
    }
}
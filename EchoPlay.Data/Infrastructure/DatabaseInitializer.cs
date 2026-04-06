using EchoPlay.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Infrastructure
{
    /// <summary>
    /// Stellt sicher, dass alle ausstehenden EF-Core-Migrationen beim App-Start eingespielt werden.
    /// Muss einmalig aufgerufen werden, bevor andere Datenbankzugriffe stattfinden.
    /// </summary>
    public sealed class DatabaseInitializer
    {
        private readonly EchoPlayDbContext _context;

        /// <summary>
        /// Initialisiert den <see cref="DatabaseInitializer"/> mit dem Datenbankkontext.
        /// </summary>
        /// <param name="context">Der EF-Core-Kontext, dessen Datenbank migriert werden soll.</param>
        public DatabaseInitializer(EchoPlayDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Wendet alle ausstehenden Migrationen an und erstellt die Datenbank falls nötig.
        /// Ist kein Schema vorhanden, wird es vollständig aufgebaut.
        /// Fehler werden nicht unterdrückt – ohne lauffähiges Schema kann die App nicht starten.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task InitializeAsync()
        {
            await _context.Database.MigrateAsync().ConfigureAwait(false);
        }
    }
}

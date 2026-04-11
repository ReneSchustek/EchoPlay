using System.Globalization;
using EchoPlay.Data.Context;
using EchoPlay.Logger.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Infrastructure
{
    /// <summary>
    /// Stellt sicher, dass alle ausstehenden EF-Core-Migrationen beim App-Start eingespielt werden.
    /// Vor einer Migration mit offenen Änderungen wird die bestehende SQLite-Datei als
    /// Backup kopiert, damit der Nutzer seine Bibliothek bei einem Schema-Bruch nicht verliert.
    /// Muss einmalig aufgerufen werden, bevor andere Datenbankzugriffe stattfinden.
    /// </summary>
    public sealed class DatabaseInitializer
    {
        private const int MaxBackupsToKeep = 5;

        private readonly EchoPlayDbContext _context;
        private readonly ILogger? _logger;

        /// <summary>
        /// Initialisiert den <see cref="DatabaseInitializer"/> mit dem Datenbankkontext.
        /// </summary>
        /// <param name="context">Der EF-Core-Kontext, dessen Datenbank migriert werden soll.</param>
        /// <param name="loggerFactory">Optionale Logger-Fabrik für Backup-/Migration-Ausgaben.</param>
        public DatabaseInitializer(EchoPlayDbContext context, ILoggerFactory? loggerFactory = null)
        {
            _context = context;
            _logger = loggerFactory?.CreateLogger(nameof(DatabaseInitializer));
        }

        /// <summary>
        /// Wendet alle ausstehenden Migrationen an und erstellt die Datenbank falls nötig.
        /// Ist kein Schema vorhanden, wird es vollständig aufgebaut.
        /// Fehler werden nicht unterdrückt – ohne lauffähiges Schema kann die App nicht starten.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task InitializeAsync()
        {
            IEnumerable<string> pending = await _context.Database.GetPendingMigrationsAsync().ConfigureAwait(false);

            if (pending.Any())
            {
                TryCreateBackup();
            }

            await _context.Database.MigrateAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Legt eine Kopie der aktuellen SQLite-Datei neben dem Original an und räumt alte
        /// Backups auf, sodass maximal <see cref="MaxBackupsToKeep"/> Exemplare liegen bleiben.
        /// Fehler beim Backup werden geloggt, brechen die Migration aber nicht ab – ein
        /// fehlender Backup-Schritt ist besser als ein verweigerter App-Start.
        /// </summary>
        private void TryCreateBackup()
        {
            try
            {
                string dbPath = DatabasePathProvider.GetDatabasePath();
                if (!File.Exists(dbPath))
                {
                    return;
                }

                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string backupPath = $"{dbPath}.backup-{timestamp}";
                File.Copy(dbPath, backupPath, overwrite: false);
                _logger?.Info($"DB-Backup vor Migration erstellt: {Path.GetFileName(backupPath)}");

                CleanupOldBackups(dbPath);
            }
            catch (IOException ex)
            {
                _logger?.Warning($"Backup vor Migration fehlgeschlagen: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger?.Warning($"Backup vor Migration fehlgeschlagen (Zugriff verweigert): {ex.Message}");
            }
        }

        /// <summary>
        /// Behält maximal <see cref="MaxBackupsToKeep"/> Backup-Dateien und löscht die
        /// ältesten. Name-Sortierung genügt, weil der Zeitstempel ISO-sortierbar ist.
        /// </summary>
        private void CleanupOldBackups(string dbPath)
        {
            string directory = Path.GetDirectoryName(dbPath) ?? string.Empty;
            string fileName = Path.GetFileName(dbPath);
            string pattern = $"{fileName}.backup-*";

            string[] backups = Directory.GetFiles(directory, pattern);
            Array.Sort(backups, (a, b) => string.CompareOrdinal(b, a));

            for (int i = MaxBackupsToKeep; i < backups.Length; i++)
            {
                try
                {
                    File.Delete(backups[i]);
                }
                catch (IOException ex)
                {
                    _logger?.Warning($"Altes DB-Backup konnte nicht gelöscht werden ({Path.GetFileName(backups[i])}): {ex.Message}");
                }
            }
        }
    }
}

using System.Globalization;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Internal;
using EchoPlay.Logger.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Infrastructure
{
    /// <summary>
    /// Stellt sicher, dass alle ausstehenden EF-Core-Migrationen beim App-Start eingespielt werden.
    /// Vor einer Migration mit offenen Änderungen wird die bestehende SQLite-Datei per
    /// <c>VACUUM INTO</c> snapshot-artig dupliziert – SQLite konsolidiert dabei WAL-Journal und
    /// Hauptdatei in eine konsistente Kopie, sodass der Nutzer bei einem Schema-Bruch zurückrollen kann.
    /// Muss einmalig aufgerufen werden, bevor andere Datenbankzugriffe stattfinden.
    /// </summary>
    public sealed class DatabaseInitializer
    {
        private const int DefaultBackupRetentionCount = 5;
        private const int MaxBackupRetentionCount = 20;
        private const long DiskSpaceSafetyFactor = 2;

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
                (bool enabled, int retentionCount) = await TryReadBackupSettingsAsync().ConfigureAwait(false);

                if (enabled && retentionCount > 0)
                {
                    await TryCreateBackupAsync(retentionCount).ConfigureAwait(false);
                }
                else
                {
                    _logger?.Info("DB-Backup vor Migration ist per Einstellungen deaktiviert.");
                }
            }

            await _context.Database.MigrateAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Liest die Backup-Einstellungen aus der Datenbank. Wirft die Abfrage eine
        /// <see cref="SqliteException"/> (Tabelle/Spalte existiert noch nicht, z. B. bei
        /// Erstinstallation oder vor der Einführung der Backup-Spalten), werden die
        /// Default-Werte zurückgegeben – Backup an, fünf Kopien.
        /// Intern zum Testen – die öffentliche API bleibt <see cref="InitializeAsync"/>.
        /// </summary>
        internal async Task<(bool Enabled, int RetentionCount)> TryReadBackupSettingsAsync()
        {
            try
            {
                AppSettings? settings = await _context.AppSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);

                if (settings is null)
                {
                    return (true, DefaultBackupRetentionCount);
                }

                int retention = Math.Clamp(settings.DbBackupRetentionCount, 0, MaxBackupRetentionCount);
                return (settings.DbBackupEnabled, retention);
            }
            catch (SqliteException)
            {
                return (true, DefaultBackupRetentionCount);
            }
        }

        /// <summary>
        /// Erstellt per <c>VACUUM INTO</c> einen konsistenten Snapshot der Datenbankdatei und
        /// räumt alte Backups auf, sodass maximal <paramref name="retentionCount"/> Kopien bestehen bleiben.
        /// Fehler werden geloggt, brechen die Migration aber nicht ab – ein
        /// fehlender Backup-Schritt ist besser als ein verweigerter App-Start.
        /// </summary>
        /// <param name="retentionCount">Anzahl der zu behaltenden Backups.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
            Justification = "VACUUM INTO akzeptiert keine Parameter-Bindings; der Zielpfad wird explizit durch Verdoppeln der Single Quotes escaped und stammt aus der Connection-String-DataSource-Route (programmatisch ermittelt).")]
        private async Task TryCreateBackupAsync(int retentionCount)
        {
            string? connectionString = _context.Database.GetConnectionString();
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger?.Warning("DB-Backup übersprungen: kein Connection-String am DbContext.");
                return;
            }

            SqliteConnectionStringBuilder connectionBuilder = new(connectionString);
            string dbPath = connectionBuilder.DataSource;

            // In-Memory-DBs (`:memory:`) und leere Pfade haben keinen physischen Snapshot
            // und werden schlicht übersprungen – relevant für Tests und unkonfigurierte Umgebungen.
            if (string.IsNullOrEmpty(dbPath)
                || string.Equals(dbPath, ":memory:", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(dbPath))
            {
                return;
            }

            try
            {
                // Grund: dbPath stammt aus IConfiguration (ConnectionStrings:Default),
                // nicht aus User-Input — Path-Traversal-Risiko greift hier nicht.
#pragma warning disable SCS0018
                long dbSize = new FileInfo(dbPath).Length;
#pragma warning restore SCS0018
                if (!HasEnoughDiskSpace(dbPath, dbSize))
                {
                    long requiredMb = (dbSize * DiskSpaceSafetyFactor) / (1024 * 1024);
                    _logger?.Warning("DB-Backup übersprungen: zu wenig freier Speicher (benötigt ~{RequiredMb} MB).", requiredMb);
                    return;
                }

                string timestamp = EntityClock.Current.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string backupPath = $"{dbPath}.backup-{timestamp}";

                // VACUUM INTO akzeptiert keine Parameter-Bindings – Single Quotes verdoppeln,
                // damit auch Pfade mit Hochkomma sicher eingebettet werden.
                string escapedPath = backupPath.Replace("'", "''", StringComparison.Ordinal);

                SqliteConnection connection = new(connectionString);
                await using (connection.ConfigureAwait(false))
                {
                    await connection.OpenAsync().ConfigureAwait(false);

                    using SqliteCommand command = connection.CreateCommand();
                    // Grund: VACUUM INTO erlaubt keine Parameter-Bindings; escapedPath
                    // ist intern aus dbPath (Config) und UTC-Timestamp gebildet, das
                    // Single-Quote-Escaping schliesst die einzige verbleibende Injection-Quelle.
#pragma warning disable SCS0002
                    command.CommandText = $"VACUUM INTO '{escapedPath}'";
#pragma warning restore SCS0002
                    _ = await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                    _logger?.Info("DB-Backup vor Migration erstellt: {BackupFileName}", Path.GetFileName(backupPath));
                }

                CleanupOldBackups(dbPath, retentionCount);
            }
            catch (SqliteException ex)
            {
                _logger?.Warning("Backup vor Migration fehlgeschlagen (SQLite): {Reason}", ex.Message);
            }
            catch (IOException ex)
            {
                _logger?.Warning("Backup vor Migration fehlgeschlagen (IO): {Reason}", ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger?.Warning("Backup vor Migration fehlgeschlagen (Zugriff verweigert): {Reason}", ex.Message);
            }
        }

        /// <summary>
        /// Prüft, ob auf dem Ziellaufwerk noch mindestens die doppelte DB-Größe frei ist.
        /// <c>VACUUM INTO</c> braucht temporär sowohl das Backup als auch interne Scratch-Pages,
        /// der Faktor 2 ist die konservative Sicherheitsmarge. Schlägt der Check fehl
        /// (z. B. UNC-Pfad ohne DriveInfo-Unterstützung), wird das Backup optimistisch zugelassen.
        /// </summary>
        private static bool HasEnoughDiskSpace(string dbPath, long dbSize)
        {
            try
            {
                string? root = Path.GetPathRoot(Path.GetFullPath(dbPath));
                if (string.IsNullOrEmpty(root))
                {
                    return true;
                }

                DriveInfo drive = new(root);
                long required = dbSize * DiskSpaceSafetyFactor;
                return drive.AvailableFreeSpace >= required;
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        /// <summary>
        /// Behält maximal <paramref name="retentionCount"/> Backup-Dateien und löscht die
        /// ältesten. Name-Sortierung genügt, weil der Zeitstempel ISO-sortierbar ist.
        /// Intern zum Testen – das Retention-Verhalten ist unabhängig vom DbContext.
        /// </summary>
        internal void CleanupOldBackups(string dbPath, int retentionCount)
        {
            string directory = Path.GetDirectoryName(dbPath) ?? string.Empty;
            string fileName = Path.GetFileName(dbPath);
            string pattern = $"{fileName}.backup-*";

            // Grund: dbPath stammt aus der gebundenen IConfiguration
            // (DataServiceCollectionExtensions), nicht aus User-Input.
#pragma warning disable SCS0018
            string[] backups = Directory.GetFiles(directory, pattern);
#pragma warning restore SCS0018
            Array.Sort(backups, (a, b) => string.CompareOrdinal(b, a));

            for (int i = retentionCount; i < backups.Length; i++)
            {
                try
                {
                    // Grund: backups[i] kommt direkt aus Directory.GetFiles oben,
                    // ist daher per Konstruktion innerhalb von 'directory'.
#pragma warning disable SCS0018
                    File.Delete(backups[i]);
#pragma warning restore SCS0018
                }
                catch (IOException ex)
                {
                    _logger?.Warning("Altes DB-Backup konnte nicht gelöscht werden ({BackupFileName}): {Reason}", Path.GetFileName(backups[i]), ex.Message);
                }
            }
        }
    }
}

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
    /// Spielt beim App-Start alle ausstehenden EF-Core-Migrationen ein.
    /// Vor einer Migration mit offenen Änderungen legt er per <see cref="SqliteSnapshot"/> eine
    /// Sicherung der Datenbankdatei an, sodass der Nutzer bei einem Schema-Bruch zurückrollen kann.
    /// Muss einmalig aufgerufen werden, bevor andere Datenbankzugriffe stattfinden.
    /// </summary>
    public sealed class DatabaseInitializer
    {
        // Gilt, solange sich keine Einstellungen lesen lassen; derselbe Wert wie der Standard in
        // AppSettings.DbBackupRetentionCount.
        private const int DefaultBackupRetentionCount = 5;

        // Jede Sicherung ist so groß wie die Datenbank. Die Obergrenze fängt einen von Hand
        // gesetzten Wert ab, bevor die Sicherungen das Laufwerk füllen.
        private const int MaxBackupRetentionCount = 20;

        // Eine Sicherung braucht so viel Platz wie die Datenbank. Gefordert wird das Doppelte,
        // damit die Kopie das Laufwerk nicht bis zum letzten Byte füllt und die Migration danach
        // noch schreiben kann.
        private const long DiskSpaceSafetyFactor = 2;
        private const string BackupInfix = ".backup-";

        private readonly EchoPlayDbContext _context;
        private readonly DatabaseFileLocation _databaseFile;
        private readonly ILogger? _logger;

        /// <summary>
        /// Initialisiert den <see cref="DatabaseInitializer"/> mit dem Datenbankkontext.
        /// </summary>
        /// <param name="context">Der EF-Core-Kontext, dessen Datenbank migriert werden soll.</param>
        /// <param name="databaseFile">Die Datei, die der Kontext öffnet; Ziel der Sicherung.</param>
        /// <param name="loggerFactory">Optionale Logger-Fabrik für Backup-/Migration-Ausgaben.</param>
        public DatabaseInitializer(
            EchoPlayDbContext context,
            DatabaseFileLocation databaseFile,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(databaseFile);

            _context = context;
            _databaseFile = databaseFile;
            _logger = loggerFactory?.CreateLogger(nameof(DatabaseInitializer));
        }

        /// <summary>
        /// Wendet alle ausstehenden Migrationen an und erstellt die Datenbank falls nötig.
        /// Ist kein Schema vorhanden, wird es vollständig aufgebaut.
        /// Fehler werden nicht unterdrückt, denn ohne lauffähiges Schema kann die App nicht starten.
        /// </summary>
        /// <param name="cancellationToken">
        /// Abbruch-Token. Greift bei Vorprüfung und Backup; die Migration selbst läuft je
        /// Schritt in einer Transaktion, ein Abbruch lässt daher kein halbes Schema zurück.
        /// </param>
        /// <returns>Der Task ist abgeschlossen, wenn alle ausstehenden Migrationen angewendet sind.</returns>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            IEnumerable<string> pending = await _context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);

            if (pending.Any())
            {
                (bool enabled, int retentionCount) = await TryReadBackupSettingsAsync(cancellationToken).ConfigureAwait(false);

                if (enabled && retentionCount > 0)
                {
                    await TryCreateBackupAsync(retentionCount, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _logger?.Info("DB-Backup vor Migration ist per Einstellungen deaktiviert.");
                }
            }

            await _context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Liest die Backup-Einstellungen aus der Datenbank. Wirft die Abfrage eine
        /// <see cref="SqliteException"/> (Tabelle/Spalte existiert noch nicht, z. B. bei
        /// Erstinstallation oder vor der Einführung der Backup-Spalten), werden die
        /// Default-Werte zurückgegeben: Backup an, fünf Kopien.
        /// Intern zum Testen, die öffentliche API bleibt <see cref="InitializeAsync"/>.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        internal async Task<(bool Enabled, int RetentionCount)> TryReadBackupSettingsAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Nur die beiden Spalten, nicht die ganze Zeile: Gelesen wird vor der Migration, und
                // eine Spalte, die erst die ausstehende Migration anlegt, ließe die Abfrage scheitern.
                // Dann griffe die Vorgabe, und wer die Sicherung abgeschaltet hat, bekäme trotzdem eine.
                BackupSettingsRow? settings = await _context.AppSettings
                    .AsNoTracking()
                    .Select(s => new BackupSettingsRow(s.DbBackupEnabled, s.DbBackupRetentionCount))
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (settings is null)
                {
                    return (true, DefaultBackupRetentionCount);
                }

                int retention = Math.Clamp(settings.RetentionCount, 0, MaxBackupRetentionCount);
                return (settings.Enabled, retention);
            }
            catch (SqliteException ex)
            {
                // Erwartet vor der ersten Migration; die Vorgabe sichert lieber einmal zu viel.
                _logger?.Debug(() => $"Backup-Einstellungen noch nicht lesbar, nehme Vorgabe: {ex.Message}");
                return (true, DefaultBackupRetentionCount);
            }
        }

        /// <summary>
        /// Legt eine Sicherung der Datenbankdatei an und räumt alte Sicherungen auf, sodass
        /// höchstens <paramref name="retentionCount"/> Kopien bestehen bleiben.
        /// Fehler werden geloggt, brechen die Migration aber nicht ab; ein
        /// fehlender Backup-Schritt ist besser als ein verweigerter App-Start.
        /// </summary>
        /// <param name="retentionCount">Anzahl der zu behaltenden Backups.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        private async Task TryCreateBackupAsync(int retentionCount, CancellationToken cancellationToken)
        {
            string dbPath = _databaseFile.FilePath;

            // Eine Erstinstallation hat noch keine Datei; dann gibt es nichts zu sichern.
            if (!File.Exists(dbPath))
            {
                return;
            }

            try
            {
                await CreateBackupAsync(dbPath, retentionCount, cancellationToken).ConfigureAwait(false);
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
        /// Schreibt die Sicherung mit Zeitstempel im Namen, wenn der Platz reicht, und räumt danach auf.
        /// </summary>
        private async Task CreateBackupAsync(string dbPath, int retentionCount, CancellationToken cancellationToken)
        {
            long dbSize = new FileInfo(dbPath).Length;
            if (!HasEnoughDiskSpace(dbPath, dbSize))
            {
                long requiredMb = (dbSize * DiskSpaceSafetyFactor) / (1024 * 1024);
                _logger?.Warning("DB-Backup übersprungen: zu wenig freier Speicher (benötigt ~{RequiredMb} MB).", requiredMb);
                return;
            }

            string timestamp = EntityClock.Current.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string backupFileName = Path.GetFileName(dbPath) + BackupInfix + timestamp;

            _ = await SqliteSnapshot.CreateAsync(dbPath, backupFileName, cancellationToken).ConfigureAwait(false);
            _logger?.Info("DB-Backup vor Migration erstellt: {BackupFileName}", backupFileName);

            CleanupOldBackups(retentionCount);
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
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // Lässt sich der freie Platz nicht ermitteln, wird trotzdem gesichert; scheitert
                // die Sicherung dann am Platz, meldet sie das selbst mit Grund.
                return true;
            }
        }

        /// <summary>
        /// Behält höchstens <paramref name="retentionCount"/> Sicherungen neben der Datenbankdatei
        /// und löscht die ältesten. Die Sortierung nach Namen genügt, weil der Zeitstempel im
        /// Namen lexikografisch sortierbar ist.
        /// Gezählt werden nur die eigenen Sicherungen, deren Name auf genau einen Zeitstempel
        /// endet. Von Hand benannte Kopien wie „…backup-vor-umbau-…" samt ihren -wal- und
        /// -shm-Dateien sortierten sonst vor jeden Zeitstempel, belegten die Plätze, und die
        /// gerade angelegte Sicherung wurde sofort wieder gelöscht.
        /// Intern zum Testen; das Aufräumen hängt nicht an einer Migration.
        /// </summary>
        /// <param name="retentionCount">Anzahl der zu behaltenden Sicherungen.</param>
        internal void CleanupOldBackups(int retentionCount)
        {
            string dbPath = _databaseFile.FilePath;
            string directory = Path.GetDirectoryName(dbPath) ?? string.Empty;
            string pattern = Path.GetFileName(dbPath) + BackupInfix + "*";

            string prefix = Path.GetFileName(dbPath) + BackupInfix;
            string[] backups = [.. Directory.GetFiles(directory, pattern)
                .Where(file => IsTimestampedBackup(Path.GetFileName(file), prefix))];
            Array.Sort(backups, (a, b) => string.CompareOrdinal(b, a));

            for (int i = retentionCount; i < backups.Length; i++)
            {
                try
                {
                    File.Delete(backups[i]);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Eine schreibgeschützte oder geöffnete Sicherung bleibt liegen; die übrigen
                    // werden trotzdem aufgeräumt.
                    _logger?.Warning("Altes DB-Backup konnte nicht gelöscht werden ({BackupFileName}): {Reason}", Path.GetFileName(backups[i]), ex.Message);
                }
            }
        }

        /// <summary>
        /// Ob ein Dateiname eine eigene Sicherung ist: Präfix, dann genau der Zeitstempel im
        /// Format <c>yyyyMMdd-HHmmss</c> und nichts weiter.
        /// </summary>
        /// <param name="fileName">Der Dateiname ohne Verzeichnis.</param>
        /// <param name="prefix">Datenbankname samt <c>.backup-</c>.</param>
        internal static bool IsTimestampedBackup(string fileName, string prefix)
        {
            if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            return DateTime.TryParseExact(
                fileName[prefix.Length..], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }

        /// <summary>Die Sicherungseinstellungen, ohne den Rest der Einstellungszeile.</summary>
        /// <param name="Enabled">Ob vor einer Migration gesichert wird.</param>
        /// <param name="RetentionCount">Wie viele Sicherungen bleiben.</param>
        private sealed record BackupSettingsRow(bool Enabled, int RetentionCount);
    }
}

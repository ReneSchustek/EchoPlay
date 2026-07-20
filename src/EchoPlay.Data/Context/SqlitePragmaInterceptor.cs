using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EchoPlay.Data.Context
{
    /// <summary>
    /// EF-Core-Interceptor, der bei jeder neuen SQLite-Verbindung Performance- und
    /// Integritäts-PRAGMAs setzt.
    ///
    /// Warum ein Interceptor statt Connection-String-Optionen?
    /// SQLite unterstützt nicht alle PRAGMAs im Connection-String (z.B. <c>cache_size</c>,
    /// <c>mmap_size</c>, <c>temp_store</c>). Der Interceptor fängt das Öffnen der Verbindung ab
    /// und führt die PRAGMAs direkt danach aus – bevor EF Core die erste Abfrage sendet.
    ///
    /// Die gewählten Werte sind für eine Desktop-App mit lokaler SQLite-DB optimiert:
    /// Schnelle Reads, sichere Writes, kein Mehrbenutzerbetrieb.
    /// </summary>
    public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
    {
        /// <summary>
        /// Wird synchron aufgerufen, nachdem die SQLite-Verbindung geöffnet wurde.
        /// Setzt die PRAGMA-Konfiguration für Performance und Datenintegrität.
        /// </summary>
        /// <param name="connection">Die geöffnete Datenbankverbindung.</param>
        /// <param name="eventData">Diagnosedaten zum Verbindungsereignis.</param>
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            ArgumentNullException.ThrowIfNull(connection);

            base.ConnectionOpened(connection, eventData);
            ApplyPragmas(connection);
        }

        /// <summary>
        /// Wird asynchron aufgerufen, nachdem die SQLite-Verbindung geöffnet wurde.
        /// Delegiert an die synchrone PRAGMA-Methode – SQLite-PRAGMAs sind extrem schnell
        /// und blockieren nicht, daher kein Vorteil durch async.
        /// </summary>
        /// <param name="connection">Die geöffnete Datenbankverbindung.</param>
        /// <param name="eventData">Diagnosedaten zum Verbindungsereignis.</param>
        /// <param name="cancellationToken">Abbruch-Token.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public override Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);

            ApplyPragmas(connection);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Führt die PRAGMA-Befehle auf der geöffneten Verbindung aus.
        /// Jeder PRAGMA-Befehl wird einzeln gesendet – SQLite verarbeitet pro Statement nur ein PRAGMA.
        /// </summary>
        /// <param name="connection">Die geöffnete SQLite-Verbindung.</param>
        private static void ApplyPragmas(DbConnection connection)
        {
            using DbCommand command = connection.CreateCommand();

            // WAL (Write-Ahead-Log): Leser blockieren Schreiber nicht mehr.
            // Deutlich bessere Performance bei gleichzeitigem Lesen und Schreiben,
            // z.B. wenn das Dashboard lädt während ein Scan im Hintergrund importiert.
            // Einmal gesetzt bleibt WAL dauerhaft aktiv (persistiert in der DB-Datei).
            ExecutePragma(command, "PRAGMA journal_mode = WAL;");

            // NORMAL statt FULL: In Kombination mit WAL ausreichend sicher.
            // WAL garantiert Crash-Recovery auch ohne FULL-Sync nach jedem Commit.
            // Reduziert fsync-Aufrufe deutlich – spürbar bei vielen kleinen Writes.
            ExecutePragma(command, "PRAGMA synchronous = NORMAL;");

            // 64 MB Cache statt 2 MB Default: Hält häufig gelesene Seiten im RAM.
            // Negative Werte = Kilobytes (SQLite-Konvention).
            // Bei 1.000+ Serien und 50.000+ Episoden reduziert das Disk-I/O signifikant.
            ExecutePragma(command, "PRAGMA cache_size = -65536;");

            // 128 MB Memory-Mapped I/O: SQLite liest Datenbankseiten direkt aus dem
            // Betriebssystem-Cache statt über read()-Systemaufrufe. Schnellere sequenzielle Reads.
            ExecutePragma(command, "PRAGMA mmap_size = 134217728;");

            // Temporäre Tabellen und Indizes im RAM statt auf Disk.
            // Beschleunigt ORDER BY, GROUP BY und DISTINCT bei komplexen Abfragen.
            ExecutePragma(command, "PRAGMA temp_store = MEMORY;");

            // FK-Constraints erzwingen. SQLite-Default ist OFF – ohne dieses PRAGMA
            // werden Fremdschlüssel ignoriert und Dateninkonsistenzen nicht erkannt.
            ExecutePragma(command, "PRAGMA foreign_keys = ON;");
        }

        /// <summary>
        /// Führt ein einzelnes PRAGMA-Statement aus.
        /// Fehler werden bewusst nicht gefangen – ein fehlgeschlagenes PRAGMA deutet
        /// auf ein ernstes Problem hin (z.B. gesperrte DB), das nicht ignoriert werden darf.
        /// </summary>
        /// <param name="command">Das wiederverwendbare Command-Objekt.</param>
        /// <param name="pragma">Das PRAGMA-Statement.</param>
        [SuppressMessage("Security", "CA2100:SQL-Abfragen auf Sicherheitsrisiken überprüfen",
            Justification = "PRAGMA-Strings sind ausschließlich hartcodierte Literale aus dieser Klasse.")]
        private static void ExecutePragma(DbCommand command, string pragma)
        {
            command.CommandText = pragma;
            _ = command.ExecuteNonQuery();
        }
    }
}

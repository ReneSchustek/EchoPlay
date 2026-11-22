using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EchoPlay.Data.Context
{
    /// <summary>
    /// EF-Core-Interceptor, der bei jeder neuen SQLite-Verbindung Performance- und
    /// Integritäts-PRAGMAs setzt.
    /// <para>
    /// Warum ein Interceptor statt Connection-String-Optionen?
    /// SQLite unterstützt nicht alle PRAGMAs im Connection-String (z.B. <c>cache_size</c>,
    /// <c>mmap_size</c>, <c>temp_store</c>). Der Interceptor fängt das Öffnen der Verbindung ab
    /// und führt die PRAGMAs direkt danach aus, bevor EF Core die erste Abfrage sendet.
    /// </para>
    /// <para>
    /// Die gewählten Werte sind für eine Desktop-App mit lokaler SQLite-DB optimiert:
    /// Schnelle Reads, sichere Writes, kein Mehrbenutzerbetrieb.
    /// </para>
    /// </summary>
    public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
    {
        // Eine Konstante aus Literalen: So steht fest, dass kein Wert von außen in das SQL gelangt.
        private const string ConnectionPragmas =
            // WAL (Write-Ahead-Log): Leser blockieren Schreiber nicht, etwa wenn die Startseite
            // lädt, während ein Scan im Hintergrund importiert. Einmal gesetzt bleibt
            // WAL dauerhaft aktiv, der Modus steht in der Datenbankdatei.
            "PRAGMA journal_mode = WAL;"

            // NORMAL statt FULL: Zusammen mit WAL übersteht die Datenbank einen Absturz auch ohne
            // Synchronisieren nach jedem Commit; das spart viele fsync-Aufrufe bei kleinen Writes.
            + "PRAGMA synchronous = NORMAL;"

            // 64 MB Cache statt 2 MB Vorgabe: Häufig gelesene Seiten bleiben im Speicher.
            // Negative Werte sind Kilobyte (SQLite-Konvention).
            + "PRAGMA cache_size = -65536;"

            // 128 MB Memory-Mapped I/O: SQLite liest Seiten direkt aus dem Cache des
            // Betriebssystems statt über read()-Aufrufe.
            + "PRAGMA mmap_size = 134217728;"

            // Temporäre Tabellen und Indizes im Speicher: beschleunigt ORDER BY, GROUP BY und
            // DISTINCT.
            + "PRAGMA temp_store = MEMORY;"

            // Fremdschlüssel erzwingen. SQLite ignoriert sie ohne dieses PRAGMA.
            + "PRAGMA foreign_keys = ON;";

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
        /// Delegiert an die synchrone PRAGMA-Methode, denn SQLite-PRAGMAs sind extrem schnell
        /// und blockieren nicht, daher kein Vorteil durch async.
        /// </summary>
        /// <param name="connection">Die geöffnete Datenbankverbindung.</param>
        /// <param name="eventData">Diagnosedaten zum Verbindungsereignis.</param>
        /// <param name="cancellationToken">Abbruch-Token.</param>
        /// <returns>Der Task ist abgeschlossen, wenn die PRAGMA-Anweisungen auf der Verbindung gesetzt sind.</returns>
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
        /// Führt die PRAGMA-Befehle auf der geöffneten Verbindung aus. Microsoft.Data.Sqlite
        /// arbeitet mehrere Anweisungen eines Befehls nacheinander ab; ein Fehler bricht die
        /// Kette ab und wird nicht gefangen; ein gescheitertes PRAGMA deutet auf ein ernstes
        /// Problem hin (etwa eine gesperrte Datei), das nicht übergangen werden darf.
        /// </summary>
        /// <param name="connection">Die geöffnete SQLite-Verbindung.</param>
        private static void ApplyPragmas(DbConnection connection)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = ConnectionPragmas;
            _ = command.ExecuteNonQuery();
        }
    }
}

using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace EchoPlay.Data.Infrastructure
{
    /// <summary>
    /// Erzeugt per <c>VACUUM INTO</c> eine in sich stimmige Kopie einer SQLite-Datenbank.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Eine Dateikopie reicht nicht: Im WAL-Modus liegen jüngste Änderungen noch im Journal neben
    /// der Hauptdatei, eine Kopie der Hauptdatei allein ist dann unvollständig. <c>VACUUM INTO</c>
    /// liest über die Datenbank und schreibt beides zusammengeführt in eine neue Datei.
    /// </para>
    /// <para>
    /// Die Kopie entsteht immer im Verzeichnis der Quelle; der Aufrufer nennt nur den Dateinamen.
    /// Damit kann kein Aufrufer ein Ziel außerhalb des Anwendungsordners in das SQL bringen, und
    /// die Begründung zur SQL-Prüfung unten gilt unabhängig davon, woher der Name stammt.
    /// </para>
    /// </remarks>
    public static class SqliteSnapshot
    {
        /// <summary>
        /// Schreibt eine Kopie der Datenbank unter <paramref name="snapshotFileName"/> neben die Quelle.
        /// </summary>
        /// <param name="sourceDatabasePath">Absoluter Pfad der Quelldatenbank.</param>
        /// <param name="snapshotFileName">Reiner Dateiname der Kopie, ohne Verzeichnisanteil.</param>
        /// <param name="cancellationToken">Abbruch-Token.</param>
        /// <returns>Der absolute Pfad der erzeugten Kopie.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="snapshotFileName"/> ist leer oder enthält einen Verzeichnisanteil.
        /// </exception>
        /// <exception cref="SqliteException">Die Datenbank ließ sich nicht lesen oder die Kopie nicht schreiben.</exception>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
            Justification = "VACUUM INTO nimmt das Ziel nur als Literal an, SQLite bietet dafür keine Parameterbindung. Der Pfad besteht aus dem Verzeichnis der Quelle und einem geprüften reinen Dateinamen; Hochkommas werden verdoppelt.")]
        public static async Task<string> CreateAsync(
            string sourceDatabasePath,
            string snapshotFileName,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceDatabasePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(snapshotFileName);

            // Path.GetFileName schneidet jeden Verzeichnisanteil ab, auch „..\". Weicht das Ergebnis
            // vom Eingang ab, wollte der Aufrufer die Kopie woanders hinlegen. „." und „.." sind
            // für sich genommen reine Namen, zeigen aber auf ein Verzeichnis.
            if (!string.Equals(Path.GetFileName(snapshotFileName), snapshotFileName, StringComparison.Ordinal)
                || snapshotFileName is "." or ".."
                || snapshotFileName.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("Der Name der Kopie darf keinen Verzeichnisanteil enthalten.", nameof(snapshotFileName));
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(sourceDatabasePath)) ?? string.Empty;
            string snapshotPath = Path.Combine(directory, snapshotFileName);
            string escapedPath = snapshotPath.Replace("'", "''", StringComparison.Ordinal);

            SqliteConnectionStringBuilder connectionBuilder = new() { DataSource = sourceDatabasePath };
            SqliteConnection connection = new(connectionBuilder.ToString());
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"VACUUM INTO '{escapedPath}'";
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            return snapshotPath;
        }
    }
}

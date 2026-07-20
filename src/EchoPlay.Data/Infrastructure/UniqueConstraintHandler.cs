using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Infrastructure
{
    /// <summary>
    /// Hilfsklasse, um <see cref="DbUpdateException"/>-Exceptions zu erkennen, die durch
    /// SQLite-Unique-Constraint-Verletzungen entstehen. DataServices können den Helper
    /// in ihren Schreib-Pfaden nutzen, um Race-Conditions zwischen parallelen Schreibern
    /// sauber abzufangen, statt die Exception zur Shell durchschlagen zu lassen.
    /// </summary>
    internal static class UniqueConstraintHandler
    {
        /// <summary>
        /// <c>SQLITE_CONSTRAINT_UNIQUE</c> — der Extended-Error-Code für eine verletzte
        /// UNIQUE-Constraint. Nur dieser Code rechtfertigt das Retry-als-Update-Verhalten.
        /// Der Oberkategorie-Code 19 (<c>SQLITE_CONSTRAINT</c>) deckt dagegen auch
        /// FOREIGN KEY-, NOT NULL-, CHECK- und TRIGGER-Verletzungen ab — die müssen
        /// durchschlagen, damit echte Datenintegritätsfehler sichtbar bleiben.
        /// </summary>
        public const int SqliteConstraintUniqueExtendedCode = 2067;

        /// <summary>
        /// Prüft, ob eine <see cref="DbUpdateException"/> durch eine SQLite-Unique-
        /// Constraint-Verletzung ausgelöst wurde. Prüfung erfolgt über den
        /// Extended-Error-Code (2067), nicht über den Oberkategorie-Code (19).
        /// </summary>
        public static bool IsUniqueViolation(DbUpdateException ex)
            => ex.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteConstraintUniqueExtendedCode };
    }
}

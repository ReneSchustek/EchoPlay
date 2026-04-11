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
        /// SQLite-Error-Code 19 entspricht <c>SQLITE_CONSTRAINT</c>. Der Sub-Code 2067
        /// wäre spezifisch für <c>UNIQUE</c>, reicht EchoPlay aktuell aber nicht,
        /// weil wir keinen anderen Constraint gegen Race-Conditions absichern müssen.
        /// </summary>
        public const int SqliteConstraintCode = 19;

        /// <summary>
        /// Prüft, ob eine <see cref="DbUpdateException"/> durch eine SQLite-Unique-
        /// Constraint-Verletzung ausgelöst wurde.
        /// </summary>
        public static bool IsUniqueViolation(DbUpdateException ex)
            => ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintCode };
    }
}

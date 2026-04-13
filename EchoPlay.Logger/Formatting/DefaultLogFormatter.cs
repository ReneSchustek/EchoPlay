using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Formatting
{
    /// <summary>
    /// Standard-Formatter für lesbare Log-Ausgaben im Klartext-Format.
    /// </summary>
    public class DefaultLogFormatter : ILogFormatter
    {
        /// <summary>
        /// Formatiert einen Log-Eintrag als lesbaren String.
        /// </summary>
        /// <param name="entry">Der zu formatierende Eintrag.</param>
        /// <returns>Der formatierte String.</returns>
        public string Format(LogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            // Pfeil-Notation zeigt die Scope-Hierarchie visuell an
            string scopesPart = entry.Scopes.Count > 0 ? $" [{string.Join(" → ", entry.Scopes)}]" : string.Empty;

            // Exception auf neuer Zeile mit Einrückung für bessere Lesbarkeit im Log
            string exceptionPart = entry.Exception != null ? $"\n   Exception: {entry.Exception}" : string.Empty;

            return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] [{entry.Category}]{scopesPart}: {entry.Message}{exceptionPart}";
        }
    }
}
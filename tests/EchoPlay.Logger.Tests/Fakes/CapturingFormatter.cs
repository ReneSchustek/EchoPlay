using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Tests.Fakes
{
    /// <summary>
    /// Test-Formatter, der alle zur Formatierung übergebenen Einträge aufzeichnet.
    /// Ermöglicht die Prüfung, ob und mit welchen Einträgen ein Formatter aufgerufen wurde.
    /// </summary>
    internal sealed class CapturingFormatter : ILogFormatter
    {
        private readonly List<LogEntry> _formattedEntries = [];

        /// <summary>
        /// Alle Einträge, die bisher zur Formatierung übergeben wurden.
        /// </summary>
        public IReadOnlyList<LogEntry> FormattedEntries => _formattedEntries;

        /// <summary>
        /// Zeichnet den Eintrag auf und gibt einen deterministischen Platzhaltertext zurück.
        /// </summary>
        /// <param name="entry">Der zu formatierende Eintrag.</param>
        /// <returns>Ein fester Formatierungs-Platzhaltertext.</returns>
        public string Format(LogEntry entry)
        {
            _formattedEntries.Add(entry);
            return $"[CAPTURED] {entry.Category}: {entry.Message}";
        }
    }
}

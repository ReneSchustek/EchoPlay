using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Tests.Fakes
{
    /// <summary>
    /// Test-Senke, die alle empfangenen Log-Einträge in einer Liste speichert.
    /// Ermöglicht die Überprüfung von Logging-Aufrufen in Unit-Tests.
    /// </summary>
    internal sealed class CapturingSink : ILogSink
    {
        private readonly List<LogEntry> _entries = [];

        /// <summary>
        /// Alle bisher erfassten Log-Einträge in der Reihenfolge ihres Eingangs.
        /// </summary>
        public IReadOnlyList<LogEntry> Entries => _entries;

        /// <summary>
        /// Speichert den Log-Eintrag in der internen Liste.
        /// </summary>
        /// <param name="entry">Der zu speichernde Eintrag.</param>
        /// <returns>Ein abgeschlossener Task.</returns>
        public Task WriteAsync(LogEntry entry)
        {
            _entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}

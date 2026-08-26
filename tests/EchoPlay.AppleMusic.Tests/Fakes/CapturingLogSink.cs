using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Models;

namespace EchoPlay.AppleMusic.Tests.Fakes
{
    /// <summary>
    /// Test-Senke, die alle Protokolleinträge sammelt.
    /// </summary>
    /// <remarks>
    /// Gebraucht für die Frage, ob ein Vorgang etwas ins Protokoll schreibt, das dort nicht
    /// hingehört: Ein Abbruch ist kein Ausfall und darf keine Warnung erzeugen — sonst
    /// behauptet das Protokoll Fehler, die keine sind, und verdeckt die echten.
    /// </remarks>
    internal sealed class CapturingLogSink : ILogSink
    {
        private readonly List<LogEntry> _entries = [];

        /// <summary>Alle erfassten Einträge in der Reihenfolge ihres Eingangs.</summary>
        public IReadOnlyList<LogEntry> Entries => _entries;

        /// <summary>Alle Einträge der Stufe <see cref="LogLevel.Warning"/>.</summary>
        public IEnumerable<LogEntry> Warnings => _entries.Where(entry => entry.Level == LogLevel.Warning);

        /// <inheritdoc/>
        public Task WriteAsync(LogEntry entry)
        {
            _entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}

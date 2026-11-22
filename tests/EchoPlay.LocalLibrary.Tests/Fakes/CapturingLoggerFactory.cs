using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;

namespace EchoPlay.LocalLibrary.Tests.Fakes
{
    /// <summary>
    /// Logger-Fabrik, die für jede Kategorie denselben mitschreibenden Logger liefert. Für Tests,
    /// die belegen, dass ein Fehler nicht stumm bleibt.
    /// </summary>
    internal sealed class CapturingLoggerFactory : ILoggerFactory
    {
        /// <summary>Alle geschriebenen Einträge mit Stufe und Text.</summary>
        public List<(string Level, string Message)> Entries { get; } = [];

        /// <inheritdoc/>
        public ILogger CreateLogger(string category) => new CapturingLogger(Entries);

        /// <summary>Schreibt jeden Aufruf in die gemeinsame Liste der Fabrik.</summary>
        private sealed class CapturingLogger(List<(string Level, string Message)> entries) : ILogger
        {
            public bool IsDebugEnabled => true;

            public void Trace(string message) => entries.Add(("Trace", message));

            public void Debug(string message) => entries.Add(("Debug", message));

            public void Info(string message) => entries.Add(("Info", message));

            public void Warning(string message) => entries.Add(("Warning", message));

            public void Error(string message, Exception? exception = null) => entries.Add(("Error", message));

            public void Fatal(string message, Exception? exception = null) => entries.Add(("Fatal", message));

            public LogScope BeginScope(string name) => new(name);
        }
    }
}

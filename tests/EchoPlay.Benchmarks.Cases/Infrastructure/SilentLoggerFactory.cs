using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;

namespace EchoPlay.Benchmarks.Cases.Infrastructure
{
    /// <summary>
    /// Logger-Fabrik, deren Logger alles verwerfen. Gemessen wird die Arbeit der Dienste, nicht das
    /// Schreiben von Protokollzeilen.
    /// </summary>
    internal sealed class SilentLoggerFactory : ILoggerFactory
    {
        /// <inheritdoc/>
        public ILogger CreateLogger(string category) => SilentLogger.Instance;

        /// <summary>Der Logger, den die Fabrik für jede Kategorie liefert; er schreibt nichts.</summary>
        private sealed class SilentLogger : ILogger
        {
            public static readonly SilentLogger Instance = new();

            public bool IsDebugEnabled => false;

            public void Trace(string message) { }

            public void Debug(string message) { }

            public void Info(string message) { }

            public void Warning(string message) { }

            public void Error(string message, Exception? exception = null) { }

            public void Fatal(string message, Exception? exception = null) { }

            public LogScope BeginScope(string name) => new(name);
        }
    }
}

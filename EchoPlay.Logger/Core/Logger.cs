using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Scoping;

namespace EchoPlay.Logger.Core
{
    /// <summary>
    /// Standard-Implementierung des Loggers.
    /// Verteilt Log-Einträge an alle registrierten Sinks.
    /// Das Minimum-Level wird zur Laufzeit aus den <see cref="LoggerOptions"/> gelesen –
    /// so wirkt eine Einstellungsänderung sofort, ohne dass die App neu gestartet werden muss.
    /// </summary>
    /// <remarks>
    /// Erstellt einen neuen Logger für die angegebene Kategorie.
    /// </remarks>
    /// <param name="category">Die Quelle der Logs (z.B. "ApiService", "Database").</param>
    /// <param name="sinks">Liste der Ausgabeziele für Log-Einträge.</param>
    /// <param name="options">Logger-Konfiguration – <c>MinimumLevel</c> wird bei jedem Aufruf neu gelesen.</param>
    public class Logger(string category, IReadOnlyList<ILogSink> sinks, LoggerOptions options) : ILogger
    {
        private readonly string _category = category;
        private readonly IReadOnlyList<ILogSink> _sinks = sinks;
        private readonly LoggerOptions _options = options;

        /// <summary>
        /// Startet einen neuen Logging-Scope.
        /// </summary>
        /// <param name="name">Name des Scopes.</param>
        /// <returns>Ein Scope-Objekt für die Verwendung mit using.</returns>
        public LogScope BeginScope(string name)
        {
            return new LogScope(name);
        }

        /// <summary>
        /// Schreibt eine Trace-Nachricht.
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        public void Trace(string message)
        {
            // Fire-and-forget: Logging darf den aufrufenden Thread nie blockieren.
            // Exceptions werden in LogAsync pro Sink abgefangen und gehen nicht verloren.
            _ = LogAsync(LogLevel.Trace, message);
        }

        /// <summary>
        /// Schreibt eine Debug-Nachricht.
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        public void Debug(string message)
        {
            _ = LogAsync(LogLevel.Debug, message);
        }

        /// <summary>
        /// Schreibt eine Info-Nachricht.
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        public void Info(string message)
        {
            _ = LogAsync(LogLevel.Information, message);
        }

        /// <summary>
        /// Schreibt eine Warnung.
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        public void Warning(string message)
        {
            _ = LogAsync(LogLevel.Warning, message);
        }

        /// <summary>
        /// Schreibt einen Fehler.
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        /// <param name="exception">Optionale Exception.</param>
        public void Error(string message, Exception? exception = null)
        {
            _ = LogAsync(LogLevel.Error, message, exception);
        }

        /// <summary>
        /// Schreibt einen kritischen Fehler.
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        /// <param name="exception">Optionale Exception.</param>
        public void Fatal(string message, Exception? exception = null)
        {
            _ = LogAsync(LogLevel.Fatal, message, exception);
        }

        /// <summary>
        /// Erstellt einen Log-Eintrag und sendet ihn an alle Sinks.
        /// </summary>
        /// <param name="level">Die Wichtigkeitsstufe.</param>
        /// <param name="message">Die Nachricht.</param>
        /// <param name="exception">Optionaler Fehler.</param>
        private async Task LogAsync(LogLevel level, string message, Exception? exception = null)
        {
            // Frühzeitig abbrechen wenn Level unter Minimum liegt.
            // Direkter Zugriff auf _options statt gecachtem Wert – ermöglicht Live-Änderung des Levels.
            if (level < _options.MinimumLevel)
            {
                return;
            }

            LogEntry entry = new(
                Timestamp: DateTime.Now,
                Level: level,
                Message: message,
                Category: _category,
                Scopes: LogScopeManager.CurrentScopes,
                Exception: exception
            );

            foreach (ILogSink sink in _sinks)
            {
                try
                {
                    await sink.WriteAsync(entry).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Sink-Fehler dürfen nie die Anwendung crashen
                    System.Diagnostics.Trace.WriteLine($"Logger: Sink-Fehler: {ex.Message}");
                }
            }
        }
    }
}
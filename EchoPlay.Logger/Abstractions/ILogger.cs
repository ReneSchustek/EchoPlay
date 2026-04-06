using EchoPlay.Logger.Scoping;

namespace EchoPlay.Logger.Abstractions
{
    /// <summary>
    /// Definiert die Schnittstelle für alle Logger-Implementierungen.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Schreibt eine Trace-Nachricht (sehr detailliert).
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        void Trace(string message);

        /// <summary>
        /// Schreibt eine Debug-Nachricht.
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        void Debug(string message);

        /// <summary>
        /// Schreibt eine Info-Nachricht.
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        void Info(string message);

        /// <summary>
        /// Schreibt eine Warnung.
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        void Warning(string message);

        /// <summary>
        /// Schreibt einen Fehler.
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        /// <param name="exception">Optionale Exception.</param>
        void Error(string message, Exception? exception = null);

        /// <summary>
        /// Schreibt einen kritischen Fehler.
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        /// <param name="exception">Optionale Exception.</param>
        void Fatal(string message, Exception? exception = null);

        /// <summary>
        /// Startet einen neuen Logging-Scope.
        /// </summary>
        /// <param name="name">Name des Scopes.</param>
        /// <returns>Ein Scope-Objekt für die Verwendung mit using.</returns>
        LogScope BeginScope(string name);
    }
}
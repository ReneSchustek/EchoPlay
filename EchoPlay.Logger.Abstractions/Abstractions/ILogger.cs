using EchoPlay.Logger.Scoping;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Logger.Abstractions
{
    /// <summary>
    /// Definiert die Schnittstelle für alle Logger-Implementierungen.
    /// </summary>
    [SuppressMessage("Naming", "CA1716:Bezeichner dürfen nicht mit Schlüsselwörtern übereinstimmen",
        Justification = "Error/Warning/Info sind etablierte Namen in allen gängigen Logging-APIs (Serilog, NLog, Microsoft.Extensions.Logging). Umbenennung würde die Erwartung jedes .NET-Entwicklers verletzen.")]
    public interface ILogger
    {
        /// <summary>
        /// Schreibt eine Trace-Nachricht (sehr detailliert).
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        void Trace(string message);

        /// <summary>
        /// Gibt an, ob mindestens ein Sink Debug-Nachrichten verarbeitet.
        /// Aufrufer mit teurer Message-Komposition prüfen das Flag, um Allokationen
        /// zu sparen — alternativ die <see cref="Debug(Func{string})"/>-Overload nutzen.
        /// </summary>
        bool IsDebugEnabled { get; }

        /// <summary>
        /// Schreibt eine Debug-Nachricht.
        /// </summary>
        /// <param name="message">Die Log-Nachricht.</param>
        void Debug(string message);

        /// <summary>
        /// Lazy-Overload: ruft <paramref name="messageFactory"/> nur, wenn
        /// <see cref="IsDebugEnabled"/> ist. Spart String-Interpolation und Boxing
        /// in Hot-Pfaden, wenn das Debug-Level nicht aktiv ist.
        /// </summary>
        /// <param name="messageFactory">Factory zur lazy Message-Konstruktion.</param>
        void Debug(Func<string> messageFactory)
        {
            ArgumentNullException.ThrowIfNull(messageFactory);
            if (IsDebugEnabled)
            {
                Debug(messageFactory());
            }
        }

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

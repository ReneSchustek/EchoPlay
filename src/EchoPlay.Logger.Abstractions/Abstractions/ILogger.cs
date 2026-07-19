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

        // ── Message-Template-Overloads (strukturiertes Logging) ────────────────
        // Default-Implementierung rendert per string.Format und delegiert an die
        // bestehende Plain-Message-Version. Strukturierte Sinks können die
        // Methoden überschreiben und Template + Args als Properties exponieren,
        // damit Filter wie {UserId} oder {SeriesId} möglich werden.

        /// <summary>
        /// Schreibt eine Info-Nachricht mit Message-Template.
        /// Beispiel: <c>logger.Info("User {UserId} hat Serie {SeriesId} importiert", userId, seriesId);</c>
        /// </summary>
        /// <param name="template">Message-Template mit benannten Platzhaltern.</param>
        /// <param name="args">Werte, die in der Reihenfolge der Platzhalter eingesetzt werden.</param>
        void Info(string template, params object?[] args)
        {
            ArgumentNullException.ThrowIfNull(template);
            Info(FormatTemplate(template, args));
        }

        /// <summary>
        /// Schreibt eine Warnung mit Message-Template.
        /// </summary>
        /// <param name="template">Message-Template mit benannten Platzhaltern.</param>
        /// <param name="args">Werte für die Platzhalter.</param>
        void Warning(string template, params object?[] args)
        {
            ArgumentNullException.ThrowIfNull(template);
            Warning(FormatTemplate(template, args));
        }

        /// <summary>
        /// Schreibt einen Fehler mit Message-Template.
        /// </summary>
        /// <param name="template">Message-Template mit benannten Platzhaltern.</param>
        /// <param name="exception">Optionale Exception.</param>
        /// <param name="args">Werte für die Platzhalter.</param>
        void Error(string template, Exception? exception, params object?[] args)
        {
            ArgumentNullException.ThrowIfNull(template);
            Error(FormatTemplate(template, args), exception);
        }

        /// <summary>
        /// Schreibt eine Debug-Nachricht mit Message-Template (lazy: rendert nur bei aktivem Debug-Level).
        /// </summary>
        /// <param name="template">Message-Template mit benannten Platzhaltern.</param>
        /// <param name="args">Werte für die Platzhalter.</param>
        void Debug(string template, params object?[] args)
        {
            ArgumentNullException.ThrowIfNull(template);
            if (IsDebugEnabled)
            {
                Debug(FormatTemplate(template, args));
            }
        }

        /// <summary>
        /// Rendert ein Message-Template wie Microsoft.Extensions.Logging:
        /// Platzhalter <c>{Name}</c> werden in Reihenfolge mit <paramref name="args"/> ersetzt.
        /// Bei zu wenigen Args bleibt der Platzhalter als Literal stehen.
        /// </summary>
        private static string FormatTemplate(string template, object?[] args)
        {
            if (args is null || args.Length == 0)
            {
                return template;
            }

            string result = template;
            int argIndex = 0;
            int searchStart = 0;
            while (argIndex < args.Length && searchStart < result.Length)
            {
                int open = result.IndexOf('{', searchStart);
                if (open < 0) break;

                int close = result.IndexOf('}', open + 1);
                if (close < 0) break;

                string replacement = args[argIndex]?.ToString() ?? "(null)";
                result = string.Concat(result.AsSpan(0, open), replacement, result.AsSpan(close + 1));
                searchStart = open + replacement.Length;
                argIndex++;
            }
            return result;
        }
    }
}

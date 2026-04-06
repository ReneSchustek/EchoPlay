using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Sinks
{
    /// <summary>
    /// Schreibt Log-Einträge in die Debug-Konsole (Visual Studio Output-Fenster).
    /// </summary>
    public class DebugConsoleSink : ILogSink
    {
        private readonly ILogFormatter _formatter;

        /// <summary>
        /// Erstellt einen neuen DebugConsoleSink.
        /// </summary>
        /// <param name="formatter">Der Formatter für die Log-Ausgabe.</param>
        public DebugConsoleSink(ILogFormatter formatter)
        {
            _formatter = formatter;
        }

        /// <summary>
        /// Schreibt einen Log-Eintrag asynchron in die Debug-Ausgabe.
        /// </summary>
        /// <param name="entry">Der zu schreibende Log-Eintrag.</param>
        /// <returns>Ein abgeschlossener Task.</returns>
        public Task WriteAsync(LogEntry entry)
        {
            try
            {
                string formattedMessage = _formatter.Format(entry);
                System.Diagnostics.Trace.WriteLine(formattedMessage);
            }
            catch (Exception ex)
            {
                // Letzter Ausweg: Direkt ohne Formatter ausgeben
                System.Diagnostics.Trace.WriteLine($"DebugConsoleSink Fehler: {ex.Message}");
            }

            return Task.CompletedTask;
        }
    }
}
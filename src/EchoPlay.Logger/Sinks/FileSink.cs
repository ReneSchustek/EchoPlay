using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Sinks
{
    /// <summary>
    /// Schreibt Log-Einträge über einen <see cref="ILogFormatter"/> in Dateien mit
    /// täglicher Rotation und Größenlimit.
    /// </summary>
    public sealed class FileSink : RotatingFileSinkBase
    {
        private readonly ILogFormatter _formatter;

        /// <summary>
        /// Erstellt einen neuen FileSink.
        /// </summary>
        /// <param name="logDirectory">Verzeichnis für Log-Dateien.</param>
        /// <param name="formatter">Der Formatter für die Log-Ausgabe.</param>
        /// <param name="maxFileSizeMb">Maximale Dateigröße in MB (Standard: 10).</param>
        public FileSink(string logDirectory, ILogFormatter formatter, int maxFileSizeMb = 10)
            : base(logDirectory, "Logs", ".log", maxFileSizeMb)
        {
            _formatter = formatter;
        }

        /// <inheritdoc/>
        protected override string FormatLine(LogEntry entry) => _formatter.Format(entry);
    }
}

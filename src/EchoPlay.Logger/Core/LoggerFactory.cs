using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Configuration;

namespace EchoPlay.Logger.Core
{
    /// <summary>
    /// Standard-Implementierung der LoggerFactory.
    /// Verwaltet Sinks und erstellt Logger-Instanzen.
    /// Alle erzeugten Logger teilen dieselbe <see cref="LoggerOptions"/>-Instanz –
    /// eine Änderung von <c>MinimumLevel</c> wirkt damit sofort auf alle Logger.
    /// </summary>
    /// <remarks>
    /// Erstellt eine neue LoggerFactory.
    /// </remarks>
    /// <param name="sinks">Liste der Ausgabeziele für alle Logger.</param>
    /// <param name="options">Geteilte Konfiguration – insbesondere <c>MinimumLevel</c> ist zur Laufzeit änderbar.</param>
    public sealed class LoggerFactory(IEnumerable<ILogSink> sinks, LoggerOptions options) : ILoggerFactory
    {
        private readonly List<ILogSink> _sinks = [.. sinks];
        private readonly LoggerOptions _options = options;

        /// <summary>
        /// Erstellt einen Logger für die angegebene Kategorie.
        /// Der Logger liest <c>MinimumLevel</c> direkt aus den geteilten Options.
        /// </summary>
        /// <param name="category">Die Kategorie/Quelle der Logs.</param>
        /// <returns>Eine neue Logger-Instanz.</returns>
        public ILogger CreateLogger(string category)
        {
            return new Logger(category, _sinks, _options);
        }
    }
}

using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Sinks
{
    /// <summary>
    /// Trägt den neu geschriebenen Eintrag zu den Abonnenten von
    /// <see cref="ILiveLogSink.LogEntryAdded"/>.
    /// </summary>
    /// <param name="entry">Der gerade gepufferte Eintrag.</param>
    public sealed class LogEntryEventArgs(LogEntry entry) : EventArgs
    {
        /// <summary>
        /// Der gerade gepufferte Eintrag.
        /// </summary>
        public LogEntry Entry { get; } = entry;
    }
}

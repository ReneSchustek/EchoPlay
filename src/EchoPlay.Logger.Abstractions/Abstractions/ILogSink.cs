using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Abstractions
{
    /// <summary>
    /// Definiert die Fähigkeit, Log-Einträge zu schreiben.
    /// </summary>
    public interface ILogSink
    {
        /// <summary>
        /// Schreibt einen Log-Eintrag an das Ziel.
        /// </summary>
        /// <param name="entry">Der zu schreibende Log-Eintrag.</param>
        /// <returns>Ein Task der die asynchrone Operation repräsentiert.</returns>
        Task WriteAsync(LogEntry entry);
    }
}

using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Tests.Fakes
{
    /// <summary>
    /// Test-Senke, die bei jedem Schreibvorgang eine Exception auslöst.
    /// Dient zum Testen der Fehlertoleranz des Loggers gegenüber defekten Senken.
    /// </summary>
    internal sealed class ThrowingSink : ILogSink
    {
        /// <summary>
        /// Wirft immer eine <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <param name="entry">Wird nicht verarbeitet.</param>
        /// <returns>Wird nie zurückgegeben.</returns>
        public Task WriteAsync(LogEntry entry)
        {
            throw new InvalidOperationException("Simulierter Senken-Fehler für Tests.");
        }
    }
}

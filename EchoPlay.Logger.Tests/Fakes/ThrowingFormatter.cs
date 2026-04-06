using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Tests.Fakes
{
    /// <summary>
    /// Test-Formatter, der bei jedem Formatierungsaufruf eine Exception auslöst.
    /// Dient zum Testen der Fehlertoleranz von Senken gegenüber defekten Formatierern.
    /// </summary>
    internal sealed class ThrowingFormatter : ILogFormatter
    {
        /// <summary>
        /// Wirft immer eine <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <param name="entry">Wird nicht verarbeitet.</param>
        /// <returns>Wird nie zurückgegeben.</returns>
        public string Format(LogEntry entry)
        {
            throw new InvalidOperationException("Simulierter Formatierer-Fehler für Tests.");
        }
    }
}

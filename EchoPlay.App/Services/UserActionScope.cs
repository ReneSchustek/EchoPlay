using EchoPlay.Logger.Scoping;
using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Erzeugt den äußersten Logging-Scope für eine User-Aktion und klammert
    /// damit alle darunter liegenden Log-Zeilen (HTTP-Calls, DB-Writes, Cover-Downloads)
    /// mit einer gemeinsamen Korrelations-ID. Support filtert über <c>grep UA:&lt;id&gt;</c>
    /// alle zu einem einzigen Klick gehörenden Log-Zeilen aus dem Log-Viewer oder der Log-Datei.
    /// </summary>
    /// <remarks>
    /// Format des emittierten Scopes: <c>UA:&lt;id&gt; &lt;name&gt;</c> (z. B. <c>UA:a3f21c12 ImportSeries</c>).
    /// Die ID ist die ersten 8 Hex-Zeichen einer neuen Guid – kollisionsarm genug, um Support-Grep
    /// über mehrere Tages-Logs eindeutig zu halten, ohne die Log-Zeilen zu überlasten.
    /// </remarks>
    public static class UserActionScope
    {
        /// <summary>
        /// Liefert die ID für den nächsten User-Action-Scope. In Tests ersetzbar, damit
        /// Assertions auf den Scope-Namen deterministisch bleiben.
        /// </summary>
        internal static Func<string> IdGenerator { get; set; } = () => Guid.NewGuid().ToString("N")[..8];

        /// <summary>
        /// Öffnet einen User-Action-Scope. Rückgabewert ist per <c>using</c> zu entsorgen,
        /// damit der Scope nach Abschluss der Aktion wieder vom Stack entfernt wird.
        /// </summary>
        /// <param name="name">Fachlicher Aktions-Name (z. B. <c>"ImportSeries"</c>, <c>"Play"</c>, <c>"ClearCover"</c>).</param>
        /// <returns>Ein <see cref="IDisposable"/>, das den Scope bei Dispose wieder entfernt.</returns>
        /// <exception cref="ArgumentException">Wird geworfen, wenn <paramref name="name"/> leer ist.</exception>
        public static IDisposable BeginUserAction(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Aktions-Name darf nicht leer sein.", nameof(name));
            }

            string id = IdGenerator();
            return new LogScope($"UA:{id} {name}");
        }
    }
}

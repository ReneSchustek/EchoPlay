using System;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Zentrale Drosselung für externe API-Aufrufe. Jeder Host hat sein eigenes
    /// Minimum-Intervall — der Aufrufer wartet per <see cref="WaitAsync"/> bis der
    /// nächste Aufruf erlaubt ist. Ersetzt verteilte <c>Task.Delay</c>-Aufrufe durch
    /// ein konfigurierbares, pro-Host-basiertes Rate-Limiting.
    /// Implementierungen halten interne <c>SemaphoreSlim</c>-Handles, daher ist
    /// <see cref="IDisposable"/> Pflicht — der DI-Container gibt die Singleton-Instanz
    /// beim Host-Shutdown frei.
    /// </summary>
    public interface IHostRateLimiter : IDisposable
    {
        /// <summary>
        /// Wartet, bis der nächste Aufruf an den angegebenen Host erlaubt ist.
        /// Kehrt sofort zurück, wenn das Intervall seit dem letzten Aufruf bereits verstrichen ist.
        /// </summary>
        /// <param name="host">Hostname, z. B. <c>musicbrainz.org</c>.</param>
        /// <param name="ct">Abbruch-Token.</param>
        Task WaitAsync(string host, CancellationToken ct = default);
    }
}

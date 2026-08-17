using EchoPlay.App.Services;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IHostRateLimiter"/>, der nicht bremst, sondern mitschreibt.
    /// </summary>
    /// <remarks>
    /// Das Wartelimit selbst ist anderswo geprüft. Hier zählt nur, ob die Aufrufer es
    /// überhaupt fragen und mit welcher Dringlichkeit — ein Aufruf, der sich als
    /// Vordergrund ausgibt, überholt die Hintergrundarbeit und darf nicht versehentlich
    /// als Hintergrund gestellt werden.
    /// </remarks>
    internal sealed class RecordingHostRateLimiter : IHostRateLimiter
    {
        private readonly List<(string Host, CoverFetchPriority Priority)> _waits = [];

        /// <summary>Jede Anfrage mit Gegenstelle und Dringlichkeit, in Aufrufreihenfolge.</summary>
        public IReadOnlyList<(string Host, CoverFetchPriority Priority)> Waits => _waits;

        /// <inheritdoc/>
        public Task WaitAsync(string host, CancellationToken cancellationToken = default)
        {
            return WaitAsync(host, CoverFetchPriority.Background, cancellationToken);
        }

        /// <inheritdoc/>
        public Task WaitAsync(string host, CoverFetchPriority priority, CancellationToken cancellationToken = default)
        {
            _waits.Add((host, priority));
            return Task.CompletedTask;
        }

        /// <summary>Der Fake hält nichts, was freigegeben werden müsste.</summary>
        public void Dispose()
        {
        }
    }
}

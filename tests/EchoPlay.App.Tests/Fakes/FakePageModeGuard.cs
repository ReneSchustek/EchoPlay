using EchoPlay.App.Services;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IPageModeGuard"/>. Antwortet mit dem vorgegebenen Ergebnis und
    /// zählt, wie oft gefragt wurde.
    /// </summary>
    internal sealed class FakePageModeGuard : IPageModeGuard
    {
        private readonly bool _allow;

        /// <param name="allow">Ob die Seite Online-Funktionen nutzen darf.</param>
        public FakePageModeGuard(bool allow = true) => _allow = allow;

        /// <summary>Wie oft der Zugang erfragt wurde.</summary>
        public int CallCount { get; private set; }

        /// <inheritdoc/>
        public Task<bool> EnsureOnlineAccessAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_allow);
        }

        /// <inheritdoc/>
        public Task<bool> EnsureLocalAccessAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_allow);
        }
    }
}

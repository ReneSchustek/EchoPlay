using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standard-Implementierung von <see cref="INewReleaseEventService"/>.
    /// Hält keinen Zustand – reine Weiterleitung an die Abonnenten.
    /// </summary>
    public sealed class NewReleaseEventService : INewReleaseEventService
    {
        /// <inheritdoc/>
        public event Action? CacheChanged;

        /// <inheritdoc/>
        public void RaiseCacheChanged()
        {
            CacheChanged?.Invoke();
        }
    }
}

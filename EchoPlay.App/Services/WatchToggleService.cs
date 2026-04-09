using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standard-Implementierung von <see cref="IWatchToggleService"/>.
    /// Singleton-Service: nutzt einen eigenen DI-Scope pro Aufruf, weil
    /// <see cref="ISeriesDataService"/> und <see cref="ICachedNewReleaseDataService"/> Scoped sind.
    /// </summary>
    public sealed class WatchToggleService : IWatchToggleService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// Initialisiert den Service mit der Scope-Fabrik der Anwendung.
        /// </summary>
        /// <param name="scopeFactory">DI-Scope-Fabrik – wird pro Toggle einmal verwendet.</param>
        public WatchToggleService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <inheritdoc/>
        public async Task ToggleAsync(Guid seriesId, bool watch)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService =
                scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            await seriesService.SetWatchedAsync(seriesId, watch);

            if (watch)
            {
                // Beim Aktivieren: sofort einen iTunes-Check auslösen, damit
                // die Neuerscheinungen beim nächsten Dashboard-Besuch verfügbar sind.
                Series? series = await seriesService.GetByIdAsync(seriesId);
                if (series is not null)
                {
                    await NewReleaseCheckHelper.CheckAndCacheSingleSeriesAsync(series, scope.ServiceProvider);
                }
                return;
            }

            // Beim Deaktivieren: gemerkte Neuerscheinungen dieser Serie entfernen.
            ICachedNewReleaseDataService cacheService =
                scope.ServiceProvider.GetRequiredService<ICachedNewReleaseDataService>();
            await cacheService.RemoveBySeriesIdsAsync([seriesId]);
        }
    }
}

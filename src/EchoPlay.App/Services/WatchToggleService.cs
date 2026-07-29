using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
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
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Service mit der Scope-Fabrik der Anwendung.
        /// </summary>
        /// <param name="scopeFactory">DI-Scope-Fabrik – wird pro Toggle einmal verwendet.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        public WatchToggleService(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("WatchToggleService");
        }

        // Startseite anstoßen, damit sie den geänderten Cache übernimmt statt bis zum
        // nächsten Besuch auf dem alten Stand zu bleiben. Ohne registrierten Service
        // (z. B. Unit-Tests) passiert nichts.
        private static void NotifyCacheChanged(IServiceScope scope) =>
            scope.ServiceProvider.GetService<INewReleaseEventService>()?.RaiseCacheChanged();

        /// <inheritdoc/>
        /// <param name="seriesId">Datenbank-ID der Serie.</param>
        /// <param name="watch">Neuer Status: <see langword="true"/> aktiviert die Überwachung.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task ToggleAsync(Guid seriesId, bool watch, CancellationToken cancellationToken = default)
        {
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.WatchToggle);
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService =
                scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            await seriesService.SetWatchedAsync(seriesId, watch, cancellationToken);

            if (watch)
            {
                // Beim Aktivieren: sofort einen iTunes-Check auslösen, damit
                // die Neuerscheinungen beim nächsten Dashboard-Besuch verfügbar sind.
                Series? series = await seriesService.GetByIdAsync(seriesId, cancellationToken);
                if (series is not null)
                {
                    await NewReleaseCheckHelper.CheckAndCacheSingleSeriesAsync(series, scope.ServiceProvider, cancellationToken);
                }

                NotifyCacheChanged(scope);
                return;
            }

            // Beim Deaktivieren: gemerkte Neuerscheinungen dieser Serie entfernen.
            ICachedNewReleaseDataService cacheService =
                scope.ServiceProvider.GetRequiredService<ICachedNewReleaseDataService>();
            _ = await cacheService.RemoveBySeriesIdsAsync([seriesId], cancellationToken);

            NotifyCacheChanged(scope);
        }
    }
}

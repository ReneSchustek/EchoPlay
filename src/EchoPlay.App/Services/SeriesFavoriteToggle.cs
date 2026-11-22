using System;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.App.Infrastructure;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Persistiert den Favoriten-Status einer Serie in einem eigenen DI-Scope. Bündelt die sonst
    /// in mehreren Kachel-/Detail-ViewModels wiederholte „Scope öffnen → SetFavoriteAsync"-Sequenz.
    /// Der Aufrufer setzt danach weiterhin selbst seinen eigenen UI-Zustand (z.B. <c>IsFavorite</c>).
    /// </summary>
    internal static class SeriesFavoriteToggle
    {
        /// <summary>
        /// Setzt den Favoriten-Status einer Serie über einen kurzlebigen Scope.
        /// Favorisieren aktiviert in <see cref="ISeriesDataService.SetFavoriteAsync"/> zugleich die
        /// Überwachung. War die Serie vorher unbeobachtet, wird ihr Neuerscheinungen-Cache im
        /// Hintergrund nachgezogen, damit die Startseite nicht bis zum nächsten App-Start leer bleibt.
        /// </summary>
        /// <param name="scopeFactory">Die Scope-Factory des aufrufenden ViewModels.</param>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="isFavorite">Der neue Favoriten-Status.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Der Task ist abgeschlossen, wenn der Favoritenstatus gespeichert ist; das Nachziehen der Neuerscheinungen läuft danach im Hintergrund weiter.</returns>
        public static async Task SetFavoriteAsync(
            IServiceScopeFactory scopeFactory,
            Guid seriesId,
            bool isFavorite,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);

            bool startedWatching;
            ILogger? logger;

            using (IServiceScope scope = scopeFactory.CreateScope())
            {
                logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(nameof(SeriesFavoriteToggle));

                ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

                // Vorzustand vor dem Update lesen: nur ein echter Übergang „unbeobachtet → überwacht"
                // rechtfertigt den anschließenden Provider-Aufruf.
                Series? before = isFavorite
                    ? await seriesService.GetByIdAsync(seriesId, cancellationToken)
                    : null;

                await seriesService.SetFavoriteAsync(seriesId, isFavorite, cancellationToken);

                startedWatching = before is not null && !before.IsWatched;
            }

            if (!startedWatching)
            {
                return;
            }

            // Bewusst ohne await: der Check hängt am Provider-Rate-Limiter und würde die
            // Favoriten-Schaltfläche sekundenlang blockieren. Spätestens der nächste
            // App-Start prüft erneut.
            Task refresh = Task.Run(
                () => RefreshNewReleasesAsync(scopeFactory, seriesId, CancellationToken.None),
                CancellationToken.None);

            // Losgelöst, aber beobachtet: Ein Fehler (etwa ein beim Beenden schon entsorgter
            // Container) landet im Protokoll statt als unbeobachtete Aufgabe zu verpuffen. Die
            // Logger-Fabrik fehlt nur in Tests, die den Container von Hand zusammenstellen.
            if (logger is not null)
            {
                DetachedTask.Observe(refresh, logger, nameof(RefreshNewReleasesAsync));
            }
        }

        /// <summary>
        /// Zieht die Neuerscheinungen einer frisch überwachten Serie in einem eigenen Scope nach.
        /// Eigener Scope, weil der Aufrufer-Scope beim Hintergrundlauf bereits entsorgt ist.
        /// </summary>
        /// <param name="scopeFactory">Die Scope-Factory des aufrufenden ViewModels.</param>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Der Task ist abgeschlossen, wenn der Neuerscheinungen-Cache dieser Serie steht.</returns>
        internal static async Task RefreshNewReleasesAsync(
            IServiceScopeFactory scopeFactory,
            Guid seriesId,
            CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

            Series? series = await seriesService.GetByIdAsync(seriesId, cancellationToken);
            if (series is null)
            {
                return;
            }

            await NewReleaseCheckHelper.CheckAndCacheSingleSeriesAsync(series, scope.ServiceProvider, cancellationToken);

            // Startseite anstoßen: sie ist zu diesem Zeitpunkt längst gerendert und würde
            // die frisch gefundenen Folgen sonst erst beim nächsten Besuch zeigen.
            scope.ServiceProvider.GetService<INewReleaseEventService>()?.NotifyCacheChanged();
        }
    }
}

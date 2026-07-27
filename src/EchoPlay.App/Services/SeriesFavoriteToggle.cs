using System;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
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
        /// <returns>Asynchrone Ausführung.</returns>
        public static async Task SetFavoriteAsync(
            IServiceScopeFactory scopeFactory,
            Guid seriesId,
            bool isFavorite,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);

            bool startedWatching;

            using (IServiceScope scope = scopeFactory.CreateScope())
            {
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
            _ = Task.Run(
                () => RefreshNewReleasesSafeAsync(scopeFactory, seriesId),
                CancellationToken.None);
        }

        /// <summary>
        /// Hüllt <see cref="RefreshNewReleasesAsync"/> für den Hintergrundlauf ab.
        /// Ohne diese Klammer würde eine Exception als unbeobachtete Task-Exception erst
        /// im Finalizer auftauchen – etwa wenn der Scope beim App-Ende schon entsorgt ist.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Nachlauf ohne Aufrufer: DB-, Provider- oder Lifecycle-Fehler (entsorgter Scope beim App-Ende) dürfen weder die UI stören noch als unbeobachtete Task-Exception hochkommen. Der Cache wird beim nächsten App-Start ohnehin neu geprüft.")]
        private static async Task RefreshNewReleasesSafeAsync(IServiceScopeFactory scopeFactory, Guid seriesId)
        {
            try
            {
                await RefreshNewReleasesAsync(scopeFactory, seriesId, CancellationToken.None);
            }
            catch (Exception)
            {
                // Bewusst geschluckt – siehe Methoden-Kommentar.
            }
        }

        /// <summary>
        /// Zieht die Neuerscheinungen einer frisch überwachten Serie in einem eigenen Scope nach.
        /// Eigener Scope, weil der Aufrufer-Scope beim Hintergrundlauf bereits entsorgt ist.
        /// </summary>
        /// <param name="scopeFactory">Die Scope-Factory des aufrufenden ViewModels.</param>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Asynchrone Ausführung.</returns>
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
            scope.ServiceProvider.GetService<INewReleaseEventService>()?.RaiseCacheChanged();
        }
    }
}

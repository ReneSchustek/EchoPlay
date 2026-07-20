using System;
using System.Threading;
using System.Threading.Tasks;
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

            using IServiceScope scope = scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

            await seriesService.SetFavoriteAsync(seriesId, isFavorite, cancellationToken);
        }
    }
}

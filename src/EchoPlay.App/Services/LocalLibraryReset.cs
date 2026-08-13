using EchoPlay.Core;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Löst die Verbindung zwischen Datenbank und Festplatte: Ordnerangaben, Spuren und
    /// gespeicherte Cover verschwinden, damit ein Neu-Einlesen von vorn beginnt.
    /// </summary>
    /// <remarks>
    /// Rein lokale Serien werden ganz entfernt — ohne Dateien bliebe von ihnen nichts übrig.
    /// Serien, die vom Anbieter stammen, behalten ihre Angaben und verlieren nur die
    /// Zuordnung zur Festplatte.
    /// </remarks>
    public sealed class LocalLibraryReset
    {
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// Richtet das Zurücksetzen ein.
        /// </summary>
        /// <param name="scopeFactory">
        /// Für einen eigenen Datenbankbereich — der Vorgang läuft auf einem Hintergrundfaden,
        /// und der Datenbankzugriff verträgt keine geteilte Nutzung.
        /// </param>
        public LocalLibraryReset(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Entfernt alle lokalen Zuordnungen. Läuft auf einem Hintergrundfaden, weil die vielen
        /// aufeinanderfolgenden Datenbankschritte die Oberfläche sonst anhalten würden.
        /// </summary>
        /// <returns>Der Task ist abgeschlossen, wenn die Datenbank bereinigt ist.</returns>
        public Task ClearLocalAssignmentsAsync() => Task.Run(ClearInternalAsync);

        private async Task ClearInternalAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
            ICoverImageDataService coverImageService = scope.ServiceProvider.GetRequiredService<ICoverImageDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();

            List<Guid> seriesIdsToResetCover = [];
            List<Guid> episodeIdsToResetCover = [];

            foreach (Series series in allSeries)
            {
                bool isLocalOnly = series.SpotifyArtistId is null && series.AppleMusicArtistId is null;

                if (isLocalOnly)
                {
                    // Rein lokale Serie ganz entfernen — das nimmt Folgen, Spuren und Hörstände mit.
                    await seriesService.DeleteAsync(series.Id);
                    continue;
                }

                series.LocalFolderPath = null;
                await seriesService.UpdateAsync(series);
                seriesIdsToResetCover.Add(series.Id);

                await ClearEpisodesAsync(
                    episodeService, trackService, series.Id, episodeIdsToResetCover);
            }

            // Gespeicherte Cover löschen — sie werden beim nächsten Einlesen neu geholt. Ohne
            // dieses Zurücksetzen bliebe das alte Bild in der Datenbank stehen, selbst wenn
            // inzwischen ein anderes auf der Festplatte liegt.
            _ = await coverImageService.DeleteByEntitiesAsync(CoverEntityTypes.Series, seriesIdsToResetCover);
            _ = await coverImageService.DeleteByEntitiesAsync(CoverEntityTypes.Episode, episodeIdsToResetCover);
        }

        /// <summary>
        /// Nimmt den Folgen einer Serie ihre lokale Zuordnung und löscht deren Spuren.
        /// </summary>
        private static async Task ClearEpisodesAsync(
            IEpisodeDataService episodeService,
            ILocalTrackDataService trackService,
            Guid seriesId,
            List<Guid> episodeIdsToResetCover)
        {
            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(seriesId);

            foreach (Episode episode in episodes)
            {
                episode.LocalFolderPath = null;
                episode.LocalTrackCount = null;

                // Zurück auf den Ausgangszustand — es gibt keinen lokalen Abgleich mehr.
                episode.TrackMatchKind = TrackMatchKind.NotMatched;
                await episodeService.UpdateAsync(episode);

                episodeIdsToResetCover.Add(episode.Id);

                // Eine leere Liste löscht alle Spuren dieser Folge.
                await trackService.SaveTracksForEpisodeAsync(episode.Id, []);
            }
        }
    }
}

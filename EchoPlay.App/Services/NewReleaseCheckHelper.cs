using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Hilfsmethode zum Prüfen einer einzelnen Serie auf Neuerscheinungen.
    /// Wird aufgerufen, wenn der Nutzer die Überwachung für eine Serie aktiviert,
    /// damit die Ergebnisse sofort im Cache liegen und beim nächsten Dashboard-Besuch angezeigt werden.
    /// </summary>
    internal static class NewReleaseCheckHelper
    {
        /// <summary>
        /// Prüft eine einzelne Serie gegen die iTunes-API und speichert die Ergebnisse im Cache.
        /// </summary>
        /// <param name="series">Die zu prüfende Serie.</param>
        /// <param name="serviceProvider">DI-Provider für den Zugriff auf OnlineEpisodeChecker und Cache.</param>
        public static async Task CheckAndCacheSingleSeriesAsync(
            Series series,
            IServiceProvider serviceProvider)
        {
            try
            {
                IAppSettingsDataService settingsService =
                    serviceProvider.GetRequiredService<IAppSettingsDataService>();
                AppSettings settings = await settingsService.GetAsync();

                // Im Offline-Modus keine API-Calls
                if (settings.OfflineMode)
                {
                    return;
                }

                DateTime cutoffDate = (settings.LastAppStart ?? DateTime.UtcNow)
                    .AddDays(-settings.NewReleaseDays);

                CheckableSeriesInfo checkable = new()
                {
                    SeriesId = series.Id,
                    Title = series.Title,
                    AppleMusicArtistId = series.AppleMusicArtistId,
                    LocalFolderPath = series.LocalFolderPath,
                    CoverImageUrl = series.CoverImageUrl
                };

                IOnlineEpisodeChecker checker =
                    serviceProvider.GetRequiredService<IOnlineEpisodeChecker>();

                IReadOnlyList<OnlineEpisodeCheckResult> results =
                    await checker.CheckNewReleasesAsync([checkable], cutoffDate);

                // Ergebnisse in den Cache schreiben
                DateTime checkedAt = DateTime.UtcNow;
                List<CachedNewRelease> newEntries = [];

                foreach (OnlineEpisodeCheckResult result in results)
                {
                    foreach (NewReleaseEpisode release in result.NewReleaseEpisodes)
                    {
                        newEntries.Add(new CachedNewRelease
                        {
                            SeriesId = result.SeriesId,
                            Title = release.Title,
                            EpisodeNumber = release.EpisodeNumber,
                            ReleaseDate = release.ReleaseDate,
                            CoverUrl = release.CoverUrl,
                            CollectionId = release.CollectionId,
                            CheckedAtUtc = checkedAt
                        });
                    }
                }

                if (newEntries.Count > 0)
                {
                    ICachedNewReleaseDataService cacheService =
                        serviceProvider.GetRequiredService<ICachedNewReleaseDataService>();
                    await cacheService.UpsertRangeAsync(newEntries);
                }
            }
            catch (Exception)
            {
                // Fehler beim Check dürfen die Toggle-Aktion nicht blockieren.
                // Der Check wird beim nächsten App-Start erneut versucht.
            }
        }
    }
}

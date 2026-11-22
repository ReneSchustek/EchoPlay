using EchoPlay.Core.Http;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Hilfsmethode zum Prüfen einer einzelnen Serie auf Neuerscheinungen.
    /// Wird aufgerufen, wenn der Nutzer die Überwachung für eine Serie einschaltet, auch über das
    /// Favorisieren, damit die Ergebnisse sofort im Cache liegen und beim nächsten
    /// Dashboard-Besuch angezeigt werden.
    /// </summary>
    internal static class NewReleaseCheckHelper
    {
        /// <summary>
        /// Prüft eine einzelne Serie gegen die iTunes-API und speichert die Ergebnisse im Cache.
        /// </summary>
        /// <param name="series">Die zu prüfende Serie.</param>
        /// <param name="serviceProvider">DI-Provider für den Zugriff auf OnlineEpisodeChecker und Cache.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Neuerscheinungen-Check nach dem Umschalten: HTTP-Fehler bei iTunes, DB-Fehler beim Cache-Upsert oder unerwartete Parser-Probleme dürfen die Aktion nicht blockieren – die tägliche Prüfung beim Start holt den Check nach.")]
        public static async Task CheckAndCacheSingleSeriesAsync(
            Series series,
            // Helper-Methode: Provider kommt aus dem aufrufenden Scope (kein Service-Locator im Konstruktor).
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            try
            {
                IClock clock = serviceProvider.GetRequiredService<IClock>();
                AppSettings settings = await serviceProvider.GetRequiredService<IAppSettingsDataService>().GetAsync(cancellationToken);

                // Im Offline-Modus keine API-Calls
                if (settings.OfflineMode)
                {
                    return;
                }

                DateTime cutoffDate = (settings.LastAppStart ?? clock.UtcNow).AddDays(-settings.NewReleaseDays);
                IReadOnlyList<OnlineEpisodeCheckResult> results = await serviceProvider.GetRequiredService<IOnlineEpisodeChecker>()
                    .CheckNewReleasesAsync([ToCheckable(series)], cutoffDate, cancellationToken);

                List<CachedNewRelease> newEntries = ToCacheEntries(results, clock.UtcNow);
                if (newEntries.Count > 0)
                {
                    await serviceProvider.GetRequiredService<ICachedNewReleaseDataService>().UpsertRangeAsync(newEntries, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ein Abbruch des Aufrufers ist kein Fehlschlag und gehört nach oben.
                throw;
            }
            catch (Exception ex)
            {
                // Fehler beim Check dürfen die Aktion nicht blockieren; die Prüfung beim Start,
                // die höchstens einen Tag alten Stand hält, holt ihn nach. Der Helfer arbeitet mit
                // dem Provider des Aufrufers und holt den Logger deshalb von dort.
                serviceProvider.GetService<ILoggerFactory>()?
                    .CreateLogger(nameof(NewReleaseCheckHelper))
                    .Warning("Neuerscheinungs-Check für '{SeriesTitle}' fehlgeschlagen: {Reason}", series.Title, ex.Message);
            }
        }

        /// <summary>Die Angaben der Serie, die der Prüfer braucht.</summary>
        private static CheckableSeriesInfo ToCheckable(Series series) => new()
        {
            SeriesId = series.Id,
            Title = series.Title,
            AppleMusicArtistId = series.AppleMusicArtistId,
            LocalFolderPath = series.LocalFolderPath,
            CoverImageUrl = WebAddress.TryParse(series.CoverImageUrl)
        };

        /// <summary>Die Neuerscheinungen als Einträge für den Zwischenspeicher.</summary>
        private static List<CachedNewRelease> ToCacheEntries(IReadOnlyList<OnlineEpisodeCheckResult> results, DateTime checkedAt)
            => [.. results.SelectMany(result => result.NewReleaseEpisodes.Select(release => new CachedNewRelease
            {
                SeriesId = result.SeriesId,
                Title = release.Title,
                EpisodeNumber = release.EpisodeNumber,
                ReleaseDate = release.ReleaseDate,
                CoverUrl = release.CoverUrl?.OriginalString,
                CollectionId = release.CollectionId,
                CheckedAtUtc = checkedAt
            }))];
    }
}

using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Http;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Pflegt den Zwischenspeicher der Neuerscheinungen im Startlauf: aufräumen, was nicht mehr
    /// dazugehört, und höchstens einmal am Tag bei den Anbietern nachfragen. Die Startseite findet
    /// beim Öffnen damit einen fertigen Stand vor.
    /// </summary>
    /// <param name="scopeFactory">Für den eigenen Bereich des Auffrischens.</param>
    /// <param name="clock">Zeitquelle für Prüfzeitpunkte.</param>
    /// <param name="logger">Protokoll des Startlaufs.</param>
    internal sealed class NewReleaseCacheMaintenance(IServiceScopeFactory scopeFactory, IClock clock, ILogger logger)
    {
        // Häufiger als einmal am Tag lohnt die Nachfrage nicht: Neuerscheinungen kommen in
        // Wochen, nicht in Stunden, und jede Serie kostet eine gedrosselte Anfrage.
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

        /// <summary>
        /// Die Merkliste der überwachten Titel nachziehen, dann den Zwischenspeicher von nicht
        /// überwachten Serien und abgelaufenen Einträgen befreien. Scheitert das Aufräumen, wird
        /// es protokolliert und der Start geht weiter: Ein Zwischenspeicher, der einen Start länger
        /// hält, ist kein Grund, die Anwendung nicht zu öffnen. Der nächste Start räumt ihn
        /// ohnehin wieder auf.
        /// </summary>
        /// <param name="watchedTitleService">Die Merkliste der überwachten Titel.</param>
        /// <param name="cacheService">Der Zwischenspeicher.</param>
        /// <param name="context">Serien und Altersgrenze dieses Laufs.</param>
        /// <param name="cancellationToken">Abbruch des Startlaufs.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Aufräumen im Startbild: Datenbankfehler (gesperrte Datei, Zeilen schon von einem anderen Kontext entfernt) werden protokolliert, der Start fährt fort; der nächste Lauf holt das Aufräumen nach.")]
        public async Task CleanAsync(
            IWatchedTitleDataService watchedTitleService,
            ICachedNewReleaseDataService cacheService,
            StartupContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                await CleanCoreAsync(watchedTitleService, cacheService, context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ein Abbruch des Aufrufers ist kein Fehlschlag und gehört nach oben.
                throw;
            }
            catch (Exception ex)
            {
                logger.Warning("Aufräumen des Zwischenspeichers fehlgeschlagen: {Reason}", ex.Message);
            }
        }

        /// <summary>
        /// Frischt die Neuerscheinungen auf. Eigener Bereich, weil der Hauptbereich nach den
        /// Stapel-Löschungen des Aufräumens einen unstimmigen Änderungsverfolger haben kann.
        /// </summary>
        /// <param name="context">Serien und Altersgrenze dieses Laufs.</param>
        /// <param name="cancellationToken">Abbruch des Startlaufs.</param>
        public async Task RefreshAsync(StartupContext context, CancellationToken cancellationToken)
        {
            using IServiceScope refreshScope = scopeFactory.CreateScope();
            ICachedNewReleaseDataService cacheService =
                refreshScope.ServiceProvider.GetRequiredService<ICachedNewReleaseDataService>();

            await RefreshCoreAsync(context, cacheService, refreshScope.ServiceProvider, cancellationToken);
        }

        private async Task CleanCoreAsync(
            IWatchedTitleDataService watchedTitleService,
            ICachedNewReleaseDataService cacheService,
            StartupContext context,
            CancellationToken cancellationToken)
        {
            // Die Merkliste gibt neu eingelesenen Serien ihre Überwachung zurück, nachdem
            // die Mediathek geleert wurde.
            _ = await watchedTitleService.SyncFromWatchedSeriesAsync(cancellationToken);

            List<Guid> unwatchedSeriesIds = [.. context.SubscribedSeries.Where(s => !s.IsWatched).Select(s => s.Id)];

            if (unwatchedSeriesIds.Count > 0)
            {
                int cleaned = await cacheService.RemoveBySeriesIdsAsync(unwatchedSeriesIds, cancellationToken);
                if (cleaned > 0)
                {
                    logger.Info("{Cleaned} Cache-Einträge für nicht-überwachte Serien entfernt.", cleaned);
                }
            }

            int expired = await cacheService.RemoveOlderThanAsync(context.CutoffDate, cancellationToken);
            if (expired > 0)
            {
                logger.Debug(() => $"{expired} abgelaufene Cache-Einträge entfernt.");
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Neuerscheinungen-Cache-Refresh: HTTP-Fehler bei iTunes oder DB-Fehler beim Schreiben dürfen den Start nicht abbrechen; der Cache behält dann seinen bereinigten Stand.")]
        private async Task RefreshCoreAsync(
            StartupContext context,
            ICachedNewReleaseDataService cacheService,
            // Der Provider kommt aus dem eigenen Bereich (kein Service-Locator im Konstruktor).
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken)
        {
            if (context.SubscribedSeries.Count == 0 || !await IsRefreshDueAsync(cacheService, cancellationToken))
            {
                return;
            }

            List<CheckableSeriesInfo> checkable = SelectWatchedSeries(context);
            if (checkable.Count == 0)
            {
                logger.Debug("Keine überwachten Serien – iTunes-Prüfung übersprungen.");
                return;
            }

            try
            {
                IOnlineEpisodeChecker checker = serviceProvider.GetRequiredService<IOnlineEpisodeChecker>();
                await CheckAndStoreAsync(checker, checkable, context.CutoffDate, cacheService, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ein Abbruch des Aufrufers ist kein Fehlschlag und gehört nach oben.
                throw;
            }
            catch (Exception ex)
            {
                // Fehler beim Refresh dürfen den Startup nicht blockieren,
                // denn der Cache enthält dann weiterhin die alten (bereinigten) Daten.
                string innerMsg = ex.InnerException?.Message ?? "keine InnerException";
                logger.Warning("Neuerscheinungen-Refresh fehlgeschlagen: {Reason} | Inner: {InnerReason}", ex.Message, innerMsg);
            }
        }

        /// <summary>
        /// Fragt die Anbieter nach Neuerscheinungen und legt die Treffer im Zwischenspeicher ab.
        /// </summary>
        private async Task CheckAndStoreAsync(
            IOnlineEpisodeChecker checker,
            List<CheckableSeriesInfo> checkable,
            DateTime cutoffDate,
            ICachedNewReleaseDataService cacheService,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<OnlineEpisodeCheckResult> results =
                await checker.CheckNewReleasesAsync(checkable, cutoffDate, cancellationToken);

            List<CachedNewRelease> newEntries = ToCacheEntries(results, clock.UtcNow);
            if (newEntries.Count > 0)
            {
                await cacheService.UpsertRangeAsync(newEntries, cancellationToken);
            }

            logger.Info("Neuerscheinungen-Cache aktualisiert: {NewEntries} Einträge aus {SeriesCount} Serien.", newEntries.Count, results.Count);
        }

        /// <summary>
        /// Wählt die überwachten Serien aus; nur sie gehen an die Anbieter.
        /// </summary>
        private static List<CheckableSeriesInfo> SelectWatchedSeries(StartupContext context) =>
            [.. context.SubscribedSeries
                .Where(series => series.IsWatched)
                .Select(series => new CheckableSeriesInfo
                {
                    SeriesId = series.Id,
                    Title = series.Title,
                    AppleMusicArtistId = series.AppleMusicArtistId,
                    LocalFolderPath = series.LocalFolderPath,
                    CoverImageUrl = WebAddress.TryParse(series.CoverImageUrl)
                })];

        /// <summary>Ob die letzte Prüfung länger als <see cref="RefreshInterval"/> zurückliegt.</summary>
        private async Task<bool> IsRefreshDueAsync(ICachedNewReleaseDataService cacheService, CancellationToken cancellationToken)
        {
            DateTime? lastCheck = await cacheService.GetLatestCheckTimeAsync(cancellationToken);
            if (lastCheck is null || clock.UtcNow - lastCheck.Value > RefreshInterval)
            {
                return true;
            }

            logger.Debug("Neuerscheinungen-Cache ist aktuell (< 24h) – kein iTunes-Update nötig.");
            return false;
        }

        /// <summary>Macht aus den Prüfergebnissen Einträge des Zwischenspeichers.</summary>
        private static List<CachedNewRelease> ToCacheEntries(IReadOnlyList<OnlineEpisodeCheckResult> results, DateTime checkedAt) =>
        [
            .. results.SelectMany(result => result.NewReleaseEpisodes.Select(release => new CachedNewRelease
            {
                SeriesId = result.SeriesId,
                Title = release.Title,
                EpisodeNumber = release.EpisodeNumber,
                ReleaseDate = release.ReleaseDate,
                CoverUrl = release.CoverUrl?.OriginalString,
                CollectionId = release.CollectionId,
                CheckedAtUtc = checkedAt
            }))
        ];
    }
}

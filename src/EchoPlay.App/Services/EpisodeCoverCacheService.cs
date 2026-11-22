using EchoPlay.Core.Http;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Lädt fehlende Episoden-Cover und speichert sie in der DB.
    /// Dreistufig: Kopie aus dem eigenen Bestand → Provider-URL → Online-Suchkette.
    /// Episoden ohne Treffer erhalten einen Cooldown-Zeitstempel (7 Tage),
    /// damit nicht bei jedem Öffnen der Folgenübersicht erneut gesucht wird.
    /// </summary>
    public sealed class EpisodeCoverCacheService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private readonly ICoverService _coverService;
        private readonly IClock _clock;
        private readonly ICoverDownloader _coverDownloader;
        private readonly EpisodeCoverOnlineSearch _onlineSearch;

        /// <summary>
        /// Cooldown in Tagen: Erfolglose Cover-Suchen werden erst nach Ablauf dieser Frist wiederholt.
        /// </summary>
        internal const int CooldownDays = 7;

        /// <summary>
        /// Pause zwischen zwei Online-Suchen. Verteilt die Last auf die Anbieter, statt alle
        /// coverlosen Folgen einer Serie in einem Burst abzufeuern. Die Serien-Suche in
        /// <see cref="OnlineCoverPhases"/> nutzt denselben Wert.
        /// </summary>
        internal static readonly TimeSpan OnlineSearchPause = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// Initialisiert den Cover-Cache-Service.
        /// </summary>
        /// <param name="scopeFactory">Fabrik für DI-Scopes.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        /// <param name="coverService">Singleton-Dienst für Cover-Operationen über die CoverImages-Tabelle.</param>
        /// <param name="clock">Zeitquelle für Cooldown-Berechnung.</param>
        /// <param name="coverDownloader">Lädt die Bilddaten hinter einer Cover-URL.</param>
        public EpisodeCoverCacheService(
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory,
            ICoverService coverService,
            IClock clock,
            ICoverDownloader coverDownloader)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("EpisodeCoverCacheService");
            _coverService = coverService;
            _clock = clock;
            _coverDownloader = coverDownloader;
            _onlineSearch = new EpisodeCoverOnlineSearch(scopeFactory, coverDownloader, _logger);
        }

        /// <summary>
        /// Lädt fehlende Cover für Episoden einer Serie.
        /// Nur Episoden ohne Cover UND ohne aktiven Cooldown werden geprüft.
        /// Phase 1: Cover gleichnamiger Folgen aus dem eigenen Bestand kopieren (ICoverCopyService, Raw SQL).
        /// Phase 2: Provider-URL aus Import-Daten oder der gespeicherten Adresse laden.
        /// Phase 3: Online-Suchkette (CompositeCoverSearchService).
        /// Nach der Suche wird <c>CoverLastChecked</c> gesetzt, egal ob Treffer oder nicht.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Public Entry-Point für Cover-Caching: HTTP-, IO- oder DB-Fehler aus den drei Such-Phasen (Provider-URL, lokale DB, Online-Kette) dürfen den Import/Scan nicht abbrechen; Abbrüche werden separat über OperationCanceledException behandelt.")]
        public async Task CacheCoversAsync(
            Guid seriesId,
            IReadOnlyList<ImportEpisode>? importEpisodes = null,
            CoverFetchPriority priority = CoverFetchPriority.Background,
            CancellationToken ct = default)
        {
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.EpisodeCoverCache);
            try
            {
                await CacheCoversInternalAsync(seriesId, importEpisodes, priority, ct);
            }
            catch (OperationCanceledException)
            {
                // Erwarteter Abbruch (z.B. Serienwechsel), kein Log nötig
            }
            catch (Exception ex)
            {
                _logger.Warning("Cover-Caching für Serie {SeriesId} fehlgeschlagen: {Reason}", seriesId, ex.Message);
            }
        }

        /// <summary>
        /// Interne Implementierung des Cover-Cachings ohne Fehlerbehandlung.
        /// </summary>
        private async Task CacheCoversInternalAsync(
            Guid seriesId,
            IReadOnlyList<ImportEpisode>? importEpisodes,
            CoverFetchPriority priority,
            CancellationToken ct)
        {
            LogForegroundRequest(seriesId, priority);

            // Phase 1: Lokale Cover kopieren (Raw SQL via Data-Schicht)
            int localFound = await CopyLocalCoversAsync(seriesId, ct);

            // Episoden ohne Cover ermitteln (mit Cooldown-Filter)

            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

            Series? series = await seriesService.GetByIdAsync(seriesId, ct);
            string seriesName = series?.Title ?? string.Empty;

            DateTime cooldownThreshold = _clock.UtcNow.AddDays(-CooldownDays);

            IReadOnlyList<Episode> needsCheck =
                await CollectMissingCoversAsync(episodeService, seriesId, cooldownThreshold, ct);

            if (needsCheck.Count == 0)
            {
                LogLocalOnly(seriesName, localFound);
                return;
            }

            Dictionary<string, Uri> titleToCoverUrl = CollectProviderUrls(importEpisodes, needsCheck);

            _logger.Info("Cover-Suche für {EpisodeCount} Episoden von \"{SeriesName}\"", needsCheck.Count, seriesName);

            // Phase 2: Provider-URLs herunterladen (sequentiell)
            int downloaded = await DownloadProviderCoversAsync(needsCheck, titleToCoverUrl, ct);

            // Phase 3: Online-Suchkette für den Rest (mit Rate-Limiting)
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<Episode> stillMissing =
                await CollectMissingCoversAsync(episodeService, seriesId, cooldownThreshold, ct);

            int onlineFound = await SearchCoversOnlineAsync(stillMissing, seriesName, ct);

            _logger.Info(
                "Cover abgeschlossen: {LocalFound} lokal, {Downloaded} Provider, {OnlineFound} online, {NotFound} nicht gefunden",
                localFound, downloaded, onlineFound, stillMissing.Count - onlineFound);
        }

        private void LogForegroundRequest(Guid seriesId, CoverFetchPriority priority)
        {
            // Die Priorität wirkt hier nur im Protokoll: Der HTTP-Weg über die Ratenbremse
            // kennt sie nicht. Die Zeile markiert, wann Vordergrund-Anfragen kommen, damit
            // eine Auswertung solche Spitzen erkennen kann.
            if (priority == CoverFetchPriority.Foreground)
            {
                _logger.Debug(() => $"Cover-Caching Serie {seriesId} mit Foreground-Priorität angefordert.");
            }
        }

        private void LogLocalOnly(string seriesName, int localFound)
        {
            if (localFound > 0)
            {
                _logger.Info("Alle Cover für \"{SeriesName}\" lokal kopiert ({LocalFound} Stück).", seriesName, localFound);
            }
        }

        /// <summary>
        /// Phase 1: Cover gleichnamiger Folgen aus dem eigenen Bestand übernehmen.
        /// </summary>
        /// <returns>Wie viele Cover kopiert wurden.</returns>
        private async Task<int> CopyLocalCoversAsync(Guid seriesId, CancellationToken ct)
        {
            using IServiceScope copyScope = _scopeFactory.CreateScope();
            ICoverCopyService coverCopy = copyScope.ServiceProvider.GetRequiredService<ICoverCopyService>();
            return await coverCopy.CopyFromMatchingEpisodesAsync(seriesId, ct);
        }

        /// <summary>
        /// Die Folgen einer Serie, die weder ein Cover noch einen laufenden Cooldown haben.
        /// </summary>
        /// <remarks>
        /// Dieselbe Frage stellt sich zweimal: vor dem Herunterladen und noch einmal vor der
        /// Online-Suche. Sie stand deshalb zweimal im Quelltext: zwei Fassungen derselben
        /// Regel, die auseinanderlaufen können, ohne dass es auffällt.
        /// </remarks>
        private async Task<IReadOnlyList<Episode>> CollectMissingCoversAsync(
            IEpisodeDataService episodeService,
            Guid seriesId,
            DateTime cooldownThreshold,
            CancellationToken ct)
        {
            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(seriesId, ct);

            List<Guid> episodeIds = new(episodes.Count);
            foreach (Episode ep in episodes) episodeIds.Add(ep.Id);

            IReadOnlyDictionary<Guid, byte[]> existingCovers =
                await _coverService.GetEpisodeCoverBytesAsync(episodeIds, ct);

            List<Episode> missing = [];

            foreach (Episode episode in episodes)
            {
                // Cover in CoverImages vorhanden → nichts zu tun
                if (existingCovers.ContainsKey(episode.Id)) continue;

                // Cooldown noch aktiv → überspringen
                if (episode.CoverLastChecked.HasValue
                    && episode.CoverLastChecked.Value > cooldownThreshold)
                {
                    continue;
                }

                missing.Add(episode);
            }

            return missing;
        }

        /// <summary>
        /// Sammelt die Cover-Adressen des Anbieters, bevorzugt aus den Import-Daten. Was dort
        /// fehlt, kommt aus der gespeicherten Adresse der Folge.
        /// </summary>
        private static Dictionary<string, Uri> CollectProviderUrls(
            IReadOnlyList<ImportEpisode>? importEpisodes,
            IReadOnlyList<Episode> needsCheck)
        {
            Dictionary<string, Uri> titleToCoverUrl = new(StringComparer.OrdinalIgnoreCase);

            if (importEpisodes is not null)
            {
                foreach (ImportEpisode importEp in importEpisodes)
                {
                    if (importEp.CoverImageUrl is not null)
                    {
                        titleToCoverUrl[importEp.Title] = importEp.CoverImageUrl;
                    }
                }
            }

            foreach (Episode episode in needsCheck)
            {
                Uri? stored = WebAddress.TryParse(episode.CoverImageUrl);
                if (stored is not null && !titleToCoverUrl.ContainsKey(episode.Title))
                {
                    titleToCoverUrl[episode.Title] = stored;
                }
            }

            return titleToCoverUrl;
        }

        /// <summary>
        /// Phase 2: Die feststehenden Adressen des Anbieters herunterladen.
        /// </summary>
        /// <remarks>
        /// Sequentiell und ohne Wartelimit: Die Adressen stehen schon fest, es ist ein Abruf
        /// je Folge, und die Bilder liegen auf den Auslieferungsnetzen der Anbieter, nicht
        /// auf deren Schnittstellen, für die das Wartelimit gilt.
        /// </remarks>
        /// <returns>Wie viele Cover geladen wurden.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Download je Folge: HTTP- oder DB-Fehler einer einzelnen Folge dürfen die Schleife nicht verlassen; jede Folge wird unabhängig behandelt.")]
        private async Task<int> DownloadProviderCoversAsync(
            IReadOnlyList<Episode> needsCheck,
            Dictionary<string, Uri> titleToCoverUrl,
            CancellationToken ct)
        {
            int downloaded = 0;

            foreach (Episode episode in needsCheck)
            {
                ct.ThrowIfCancellationRequested();
                if (!titleToCoverUrl.TryGetValue(episode.Title, out Uri? coverAddress)) continue;

                try
                {
                    byte[]? coverBytes = await _coverDownloader.DownloadAsync(coverAddress, ct);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes, cancellationToken: ct);
                        downloaded++;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Wie in Phase 3: Ein Abbruch des Aufrufers ist kein Fehlschlag dieser Folge
                    // und darf nicht als Warnung im Protokoll landen.
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warning("Provider-Cover für \"{EpisodeTitle}\" fehlgeschlagen: {Reason}", episode.Title, ex.Message);
                }
            }

            return downloaded;
        }

        /// <summary>
        /// Phase 3: Für den Rest die Online-Suchkette bemühen, mit Pause zwischen den Anfragen.
        /// Der Zeitstempel wird auch ohne Treffer gesetzt, denn er trägt den Cooldown.
        /// </summary>
        /// <returns>Wie viele Cover gefunden wurden.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Online-Cover-Suche je Folge: Fehler der fremden Gegenstellen dürfen die Schleife nicht verlassen; jede Folge wird unabhängig behandelt.")]
        private async Task<int> SearchCoversOnlineAsync(
            IReadOnlyList<Episode> stillMissing,
            string seriesName,
            CancellationToken ct)
        {
            int onlineFound = 0;

            foreach (Episode episode in stillMissing)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    byte[]? coverBytes = await _onlineSearch.FindCoverAsync(seriesName, episode.Title, episode.EpisodeNumber, ct);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes, cancellationToken: ct);
                        onlineFound++;
                    }

                    // Zeitstempel immer setzen, auch bei Nicht-Treffer (Cooldown)
                    using IServiceScope writeScope = _scopeFactory.CreateScope();
                    IEpisodeDataService writeService = writeScope.ServiceProvider
                        .GetRequiredService<IEpisodeDataService>();
                    await writeService.SetCoverLastCheckedAsync(episode.Id, _clock.UtcNow, ct);

                    // Rate-Limiting nur bei Online-Suche (HTTP-Requests gegen externe APIs)
                    await Task.Delay(OnlineSearchPause, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Ein Abbruch des Aufrufers ist kein Fehlschlag dieser Folge. Ohne diesen
                    // Zweig hätte der catch darunter ihn als Warnung protokolliert, und weil er
                    // die Ausnahme schluckt, wäre die Schleife noch eine Runde weitergelaufen,
                    // bis das ThrowIfCancellationRequested am Anfang greift.
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warning("Online-Cover für \"{EpisodeTitle}\" fehlgeschlagen: {Reason}", episode.Title, ex.Message);
                }
            }

            return onlineFound;
        }

    }
}

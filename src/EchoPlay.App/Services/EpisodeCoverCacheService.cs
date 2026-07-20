using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Scoring;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Lädt fehlende Episoden-Cover und speichert sie in der DB.
    /// Dreistufig: Provider-URL → lokale DB-Kopie → Online-Suchkette.
    /// Episoden ohne Treffer erhalten einen Cooldown-Zeitstempel (7 Tage),
    /// damit nicht bei jedem Öffnen der Folgenübersicht erneut gesucht wird.
    /// </summary>

    public sealed class EpisodeCoverCacheService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private readonly ICoverService _coverService;
        private readonly IClock _clock;
        private readonly IHttpClientFactory _httpClientFactory;

        /// <summary>
        /// Cooldown in Tagen: Erfolglose Cover-Suchen werden erst nach Ablauf dieser Frist wiederholt.
        /// </summary>

        private const int CooldownDays = 7;

        /// <summary>
        /// Initialisiert den Cover-Cache-Service.
        /// </summary>
        /// <param name="scopeFactory">Fabrik für DI-Scopes.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        /// <param name="coverService">Singleton-Dienst für Cover-Operationen über die CoverImages-Tabelle.</param>
        /// <param name="clock">Zeitquelle für Cooldown-Berechnung.</param>
        /// <param name="httpClientFactory">Fabrik für benannte HTTP-Clients.</param>

        public EpisodeCoverCacheService(
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory,
            ICoverService coverService,
            IClock clock,
            IHttpClientFactory httpClientFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("EpisodeCoverCacheService");
            _coverService = coverService;
            _clock = clock;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Lädt fehlende Cover für Episoden einer Serie.
        /// Nur Episoden ohne Cover UND ohne aktiven Cooldown werden geprüft.
        /// Phase 1: Provider-URL aus Import-Daten (falls vorhanden).
        /// Phase 2: Lokale DB durchsuchen (ICoverCopyService – Raw SQL).
        /// Phase 3: Online-Suchkette (CompositeCoverSearchService).
        /// Nach der Suche wird <c>CoverLastChecked</c> gesetzt – egal ob Treffer oder nicht.
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
                // Erwarteter Abbruch (z.B. Serienwechsel) – kein Log nötig
            }
            catch (Exception ex)
            {
                _logger.Warning("Cover-Caching für Serie {SeriesId} fehlgeschlagen: {Reason}", seriesId, ex.Message);
            }
        }

        /// <summary>
        /// Interne Implementierung des Cover-Cachings ohne Fehlerbehandlung.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Interne Cover-Caching-Phase: HTTP-Fehler (Provider-URL-Download), DB-Fehler oder Parsing-Probleme einzelner Folgen dürfen die Schleife nicht verlassen; jede Episode wird unabhängig behandelt.")]
        private async Task CacheCoversInternalAsync(
            Guid seriesId,
            IReadOnlyList<ImportEpisode>? importEpisodes,
            CoverFetchPriority priority,
            CancellationToken ct)
        {
            // Priority wird vom Rate-Limiter via HTTP-Pipeline automatisch genutzt, sobald
            // der HttpClient in einem Foreground-Scope läuft. Aktuell wirkt der Parameter
            // nur informativ: die Protokoll-Zeile markiert die Phase, damit operative
            // Auswertungen Foreground-Spikes erkennen können.
            if (priority == CoverFetchPriority.Foreground)
            {
                _logger.Debug(() => $"Cover-Caching Serie {seriesId} mit Foreground-Priorität angefordert.");
            }

            // ── Phase 1: Lokale Cover kopieren (Raw SQL via Data-Schicht) ────────
            int localFound;

            using (IServiceScope copyScope = _scopeFactory.CreateScope())
            {
                ICoverCopyService coverCopy = copyScope.ServiceProvider
                    .GetRequiredService<ICoverCopyService>();
                localFound = await coverCopy.CopyFromMatchingEpisodesAsync(seriesId, ct);
            }

            // ── Episoden ohne Cover ermitteln (mit Cooldown-Filter) ─────────────

            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

            Series? series = await seriesService.GetByIdAsync(seriesId, ct);
            string seriesName = series?.Title ?? string.Empty;

            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(seriesId, ct);
            DateTime cooldownThreshold = _clock.UtcNow.AddDays(-CooldownDays);

            // Batch-Prüfung: welche Episoden haben bereits ein Cover in der CoverImages-Tabelle?
            List<Guid> episodeIds = new(episodes.Count);
            foreach (Episode ep in episodes) episodeIds.Add(ep.Id);

            IReadOnlyDictionary<Guid, byte[]> existingCovers =
                await _coverService.GetEpisodeCoverBytesAsync(episodeIds, ct);

            List<Episode> needsCheck = [];

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

                needsCheck.Add(episode);
            }

            if (needsCheck.Count == 0)
            {
                if (localFound > 0)
                {
                    _logger.Info("Alle Cover für \"{SeriesName}\" lokal kopiert ({LocalFound} Stück).", seriesName, localFound);
                }
                return;
            }

            // Provider-URLs sammeln: aus Import-Daten oder aus der DB
            Dictionary<string, string> titleToCoverUrl = new(StringComparer.OrdinalIgnoreCase);

            if (importEpisodes is not null)
            {
                foreach (ImportEpisode importEp in importEpisodes)
                {
                    if (!string.IsNullOrEmpty(importEp.CoverImageUrl))
                    {
                        titleToCoverUrl[importEp.Title] = importEp.CoverImageUrl;
                    }
                }
            }

            // Fallback: gespeicherte Provider-URLs aus der Episode-Entity
            foreach (Episode episode in needsCheck)
            {
                if (!string.IsNullOrEmpty(episode.CoverImageUrl)
                    && !titleToCoverUrl.ContainsKey(episode.Title))
                {
                    titleToCoverUrl[episode.Title] = episode.CoverImageUrl;
                }
            }

            _logger.Info("Cover-Suche für {EpisodeCount} Episoden von \"{SeriesName}\"", needsCheck.Count, seriesName);

            // ── Phase 2: Provider-URLs herunterladen (parallel, schnell) ────────

            int downloaded = 0;

            // Erst alle Provider-Downloads sammeln (kein Rate-Limiting nötig)
            foreach (Episode episode in needsCheck)
            {
                ct.ThrowIfCancellationRequested();
                if (!titleToCoverUrl.TryGetValue(episode.Title, out string? providerUrl)) continue;

                try
                {
                    byte[]? coverBytes = await DownloadSafeAsync(providerUrl, ct);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes, cancellationToken: ct);
                        downloaded++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning("Provider-Cover für \"{EpisodeTitle}\" fehlgeschlagen: {Reason}", episode.Title, ex.Message);
                }
            }

            // ── Phase 3: Online-Suchkette für den Rest (mit Rate-Limiting) ──────

            ct.ThrowIfCancellationRequested();

            // Erneut prüfen welche Episoden noch kein Cover haben (via CoverImages-Tabelle)
            IReadOnlyList<Episode> afterDownload = await episodeService.GetBySeriesIdAsync(seriesId, ct);

            List<Guid> afterDownloadIds = new(afterDownload.Count);
            foreach (Episode ep in afterDownload) afterDownloadIds.Add(ep.Id);

            IReadOnlyDictionary<Guid, byte[]> coversAfterDownload =
                await _coverService.GetEpisodeCoverBytesAsync(afterDownloadIds, ct);

            List<Episode> stillMissing = [];

            foreach (Episode episode in afterDownload)
            {
                if (!coversAfterDownload.ContainsKey(episode.Id)
                    && (!episode.CoverLastChecked.HasValue || episode.CoverLastChecked.Value <= cooldownThreshold))
                {
                    stillMissing.Add(episode);
                }
            }

            int onlineFound = 0;

            foreach (Episode episode in stillMissing)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    byte[]? coverBytes = await SearchCoverOnlineAsync(seriesName, episode.Title, episode.EpisodeNumber, ct);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes, cancellationToken: ct);
                        onlineFound++;
                    }

                    // Zeitstempel immer setzen – auch bei Nicht-Treffer (Cooldown)
                    using IServiceScope writeScope = _scopeFactory.CreateScope();
                    IEpisodeDataService writeService = writeScope.ServiceProvider
                        .GetRequiredService<IEpisodeDataService>();
                    await writeService.SetCoverLastCheckedAsync(episode.Id, _clock.UtcNow, ct);

                    // Rate-Limiting nur bei Online-Suche (HTTP-Requests gegen externe APIs)
                    await Task.Delay(200, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Online-Cover für \"{EpisodeTitle}\" fehlgeschlagen: {Reason}", episode.Title, ex.Message);
                }
            }

            _logger.Info(
                "Cover abgeschlossen: {LocalFound} lokal, {Downloaded} Provider, {OnlineFound} online, {NotFound} nicht gefunden",
                localFound, downloaded, onlineFound, stillMissing.Count - onlineFound);
        }

        /// <summary>
        /// Durchsucht die Cover-Dienste mit abnehmender Spezifität.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Online-Cover-Suche (CompositeCoverSearchService): HTTP-, Rate-Limit- oder Parser-Fehler der externen Quellen (iTunes/Cover Art Archive/MusicBrainz) werden zu 'null' normalisiert, sodass die aufrufende Folge 'kein Cover' bekommt.")]
        private async Task<byte[]?> SearchCoverOnlineAsync(
            string seriesName, string title, int? episodeNumber, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(seriesName)) return null;

            using IServiceScope scope = _scopeFactory.CreateScope();
            ICoverSearchService? coverSearch = scope.ServiceProvider.GetService<ICoverSearchService>();

            if (coverSearch is null) return null;

            string shortTitle = ExtractShortTitle(title, seriesName);

            // Suchbegriffe mit abnehmender Spezifität
            List<string> queries = [];

            if (episodeNumber.HasValue && !string.IsNullOrWhiteSpace(shortTitle))
            {
                queries.Add($"{seriesName} Episode {episodeNumber.Value} {shortTitle}");
                queries.Add($"{seriesName} {episodeNumber.Value} {shortTitle}");
            }

            if (!string.IsNullOrWhiteSpace(shortTitle))
            {
                queries.Add($"{seriesName} {shortTitle}");
            }

            if (episodeNumber.HasValue)
            {
                queries.Add($"{seriesName} {episodeNumber.Value}");
            }

            foreach (string query in queries)
            {
                try
                {
                    IReadOnlyList<CoverSearchResult> results = await coverSearch.SearchAsync(query, cancellationToken);

                    // Ergebnisse nach Relevanz filtern – verhindert irrelevante Cover
                    // (z.B. „Mimi Rutherford" bei Suche nach „Die drei ??? Kids")
                    CoverSearchResult? best = FindBestMatch(
                        results, seriesName, episodeNumber, shortTitle);

                    if (best is not null)
                    {
                        byte[]? bytes = await DownloadSafeAsync(best.FullUrl, cancellationToken);
                        if (bytes is not null) return bytes;
                    }
                }
                catch (Exception)
                {
                    // Einzelne Suchfehler nicht abbrechen
                }
            }

            return null;
        }

        /// <summary>
        /// Extrahiert den kurzen Folgentitel ohne Seriennamen-Präfix und Folgennummer.
        /// </summary>


        /// <param name="fullTitle">Parameter <c>fullTitle</c>.</param>
        /// <param name="seriesName">Parameter <c>seriesName</c>.</param>
        private static string ExtractShortTitle(string fullTitle, string seriesName)
        {
            string title = fullTitle;

            if (title.StartsWith(seriesName, StringComparison.OrdinalIgnoreCase))
            {
                title = title[seriesName.Length..].TrimStart(' ', '-', '–', ':');
            }

            int dashIndex = title.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIndex >= 0 && dashIndex < 10)
            {
                string beforeDash = title[..dashIndex].Trim();
                if (int.TryParse(beforeDash, out _))
                {
                    title = title[(dashIndex + 3)..].Trim();
                }
            }

            return title;
        }

        /// <summary>
        /// Wählt das relevanteste Suchergebnis anhand des <see cref="CoverRelevanceScorer"/>.
        /// Ergebnisse unter der Mindest-Schwelle werden verworfen.
        /// </summary>

        private static CoverSearchResult? FindBestMatch(
            IReadOnlyList<CoverSearchResult> results,
            string seriesName,
            int? episodeNumber,
            string? episodeTitle)
        {
            CoverSearchResult? best = null;
            int bestScore = 0;

            foreach (CoverSearchResult result in results)
            {
                int score = CoverRelevanceScorer.CalculateScore(
                    result.ReleaseTitle, seriesName, episodeNumber, episodeTitle);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = result;
                }
            }

            // Nur Treffer über der Mindest-Schwelle zurückgeben
            return bestScore >= CoverRelevanceScorer.MinimumThreshold ? best : null;
        }

        /// <summary>
        /// Lädt ein Bild von einer URL. Null bei Fehler.
        /// </summary>
        /// <param name="url">Absolute Cover-URL.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Download-Wrapper: HTTP-, TLS-, Redirect- oder Timeout-Fehler beim Laden einzelner Cover-URLs werden zu 'null' normalisiert; der Aufrufer überspringt diese Episode und fährt mit der nächsten fort.")]
        private async Task<byte[]?> DownloadSafeAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                HttpClient client = _httpClientFactory.CreateClient("CoverDownload");
                return await client.GetByteArrayAsync(new Uri(url, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}

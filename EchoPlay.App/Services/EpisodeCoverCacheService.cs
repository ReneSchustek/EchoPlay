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
        private readonly CoverService _coverService;

        /// <summary>
        /// Cooldown in Tagen: Erfolglose Cover-Suchen werden erst nach Ablauf dieser Frist wiederholt.
        /// </summary>
        private const int CooldownDays = 7;

        // Statischer HttpClient – wiederverwendet, verhindert Socket-Erschöpfung bei häufigen Cover-Downloads.
        // HttpClient ist thread-safe, eine einzige Instanz pro Prozess reicht.
        private static readonly HttpClient Client = new();

        /// <summary>
        /// Initialisiert den Cover-Cache-Service.
        /// </summary>
        /// <param name="scopeFactory">Fabrik für DI-Scopes.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        /// <param name="coverService">Singleton-Dienst für Cover-Operationen über die CoverImages-Tabelle.</param>
        public EpisodeCoverCacheService(
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory,
            CoverService coverService)
        {
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("EpisodeCoverCacheService");
            _coverService = coverService;
        }

        /// <summary>
        /// Lädt fehlende Cover für Episoden einer Serie.
        /// Nur Episoden ohne Cover UND ohne aktiven Cooldown werden geprüft.
        /// Phase 1: Provider-URL aus Import-Daten (falls vorhanden).
        /// Phase 2: Lokale DB durchsuchen (ICoverCopyService – Raw SQL).
        /// Phase 3: Online-Suchkette (CompositeCoverSearchService).
        /// Nach der Suche wird <c>CoverLastChecked</c> gesetzt – egal ob Treffer oder nicht.
        /// </summary>
        public async Task CacheCoversAsync(
            Guid seriesId,
            IReadOnlyList<ImportEpisode>? importEpisodes = null,
            CancellationToken ct = default)
        {
            try
            {
                await CacheCoversInternalAsync(seriesId, importEpisodes, ct);
            }
            catch (OperationCanceledException)
            {
                // Erwarteter Abbruch (z.B. Serienwechsel) – kein Log nötig
            }
            catch (Exception ex)
            {
                _logger.Warning($"Cover-Caching für Serie {seriesId} fehlgeschlagen: {ex.Message}");
            }
        }

        /// <summary>
        /// Interne Implementierung des Cover-Cachings ohne Fehlerbehandlung.
        /// </summary>
        private async Task CacheCoversInternalAsync(
            Guid seriesId,
            IReadOnlyList<ImportEpisode>? importEpisodes,
            CancellationToken ct)
        {
            // ── Phase 1: Lokale Cover kopieren (Raw SQL via Data-Schicht) ────────
            int localFound;

            using (IServiceScope copyScope = _scopeFactory.CreateScope())
            {
                ICoverCopyService coverCopy = copyScope.ServiceProvider
                    .GetRequiredService<ICoverCopyService>();
                localFound = await coverCopy.CopyFromMatchingEpisodesAsync(seriesId);
            }

            // ── Episoden ohne Cover ermitteln (mit Cooldown-Filter) ─────────────

            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

            Series? series = await seriesService.GetByIdAsync(seriesId);
            string seriesName = series?.Title ?? string.Empty;

            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(seriesId);
            DateTime cooldownThreshold = DateTime.UtcNow.AddDays(-CooldownDays);

            // Batch-Prüfung: welche Episoden haben bereits ein Cover in der CoverImages-Tabelle?
            List<Guid> episodeIds = new(episodes.Count);
            foreach (Episode ep in episodes) episodeIds.Add(ep.Id);

            IReadOnlyDictionary<Guid, byte[]> existingCovers =
                await _coverService.GetEpisodeCoverBytesAsync(episodeIds);

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
                    _logger.Info($"Alle Cover für \"{seriesName}\" lokal kopiert ({localFound} Stück).");
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

            _logger.Info($"Cover-Suche für {needsCheck.Count} Episoden von \"{seriesName}\"");

            // ── Phase 2: Provider-URLs herunterladen (parallel, schnell) ────────

            int downloaded = 0;

            // Erst alle Provider-Downloads sammeln (kein Rate-Limiting nötig)
            foreach (Episode episode in needsCheck)
            {
                ct.ThrowIfCancellationRequested();
                if (!titleToCoverUrl.TryGetValue(episode.Title, out string? providerUrl)) continue;

                try
                {
                    byte[]? coverBytes = await DownloadSafeAsync(providerUrl);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes);
                        downloaded++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Provider-Cover für \"{episode.Title}\" fehlgeschlagen: {ex.Message}");
                }
            }

            // ── Phase 3: Online-Suchkette für den Rest (mit Rate-Limiting) ──────

            ct.ThrowIfCancellationRequested();

            // Erneut prüfen welche Episoden noch kein Cover haben (via CoverImages-Tabelle)
            IReadOnlyList<Episode> afterDownload = await episodeService.GetBySeriesIdAsync(seriesId);

            List<Guid> afterDownloadIds = new(afterDownload.Count);
            foreach (Episode ep in afterDownload) afterDownloadIds.Add(ep.Id);

            IReadOnlyDictionary<Guid, byte[]> coversAfterDownload =
                await _coverService.GetEpisodeCoverBytesAsync(afterDownloadIds);

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
                    byte[]? coverBytes = await SearchCoverOnlineAsync(
                        seriesName, episode.Title, episode.EpisodeNumber);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes);
                        onlineFound++;
                    }

                    // Zeitstempel immer setzen – auch bei Nicht-Treffer (Cooldown)
                    using IServiceScope writeScope = _scopeFactory.CreateScope();
                    IEpisodeDataService writeService = writeScope.ServiceProvider
                        .GetRequiredService<IEpisodeDataService>();
                    await writeService.SetCoverLastCheckedAsync(episode.Id, DateTime.UtcNow);

                    // Rate-Limiting nur bei Online-Suche (HTTP-Requests gegen externe APIs)
                    await Task.Delay(200, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Online-Cover für \"{episode.Title}\" fehlgeschlagen: {ex.Message}");
                }
            }

            _logger.Info($"Cover abgeschlossen: {localFound} lokal, {downloaded} Provider, " +
                $"{onlineFound} online, {stillMissing.Count - onlineFound} nicht gefunden");
        }

        /// <summary>
        /// Durchsucht die Cover-Dienste mit abnehmender Spezifität.
        /// </summary>
        private async Task<byte[]?> SearchCoverOnlineAsync(
            string seriesName, string title, int? episodeNumber)
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
                    IReadOnlyList<CoverSearchResult> results = await coverSearch.SearchAsync(query);

                    // Ergebnisse nach Relevanz filtern – verhindert irrelevante Cover
                    // (z.B. „Mimi Rutherford" bei Suche nach „Die drei ??? Kids")
                    CoverSearchResult? best = FindBestMatch(
                        results, seriesName, episodeNumber, shortTitle);

                    if (best is not null)
                    {
                        byte[]? bytes = await DownloadSafeAsync(best.FullUrl);
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
        private static async Task<byte[]?> DownloadSafeAsync(string url)
        {
            try
            {
                return await Client.GetByteArrayAsync(url).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}

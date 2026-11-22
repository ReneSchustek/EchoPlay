using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Scoring;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Die teuerste Nachlade-Stufe: die Online-Suchkette für Folgen und Serien, deren Cover
    /// weder lokal noch über eine Anbieter-Adresse zu bekommen war.
    /// </summary>
    /// <remarks>
    /// Der Cooldown der Serien-Suche liegt hier, weil es für Serien keinen Dienst gibt, der ihn
    /// wie beim <see cref="EpisodeCoverCacheService"/> mitbringt. Einzelfehler bleiben bei der
    /// betroffenen Serie.
    /// </remarks>
    internal sealed class OnlineCoverSearchPhases
    {
        // Cooldown der Serien-Cover-Suche, derselbe Wert wie für Folgen: Eine Serie, für die
        // heute kein Cover zu finden war, hat eine Woche später realistischerweise auch keins,
        // und die Anbieter sehen nicht bei jedem Durchlauf dieselbe erfolglose Anfrage.
        private const int SeriesCoverSearchCooldownDays = EpisodeCoverCacheService.CooldownDays;

        // Pause zwischen zwei Serien-Suchen, dieselbe wie zwischen zwei Folgen-Suchen. Sie
        // verteilt die Last, statt alle coverlosen Serien in einem Burst abzufeuern.
        private static readonly TimeSpan SeriesSearchPause = EpisodeCoverCacheService.OnlineSearchPause;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ICoverService _coverService;
        private readonly ICoverDownloader _coverDownloader;
        private readonly IClock _clock;
        private readonly IHostRateLimiter? _rateLimiter;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert die Suchphasen. Der Logger kommt vom <see cref="BackgroundCoverService"/>,
        /// damit alle Meldungen eines Durchlaufs unter derselben Quelle im Protokoll stehen.
        /// </summary>
        internal OnlineCoverSearchPhases(
            IServiceScopeFactory scopeFactory,
            ICoverService coverService,
            ICoverDownloader coverDownloader,
            IClock clock,
            IHostRateLimiter? rateLimiter,
            ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _coverService = coverService;
            _coverDownloader = coverDownloader;
            _clock = clock;
            _rateLimiter = rateLimiter;
            _logger = logger;
        }

        /// <summary>
        /// Stößt für jede Serie, die noch Episoden ohne Cover hat, die Online-Suchkette an.
        /// </summary>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Anzahl der Serien, für die eine Suche angestoßen wurde.</returns>
        /// <remarks>
        /// Die Kette selbst liegt im <see cref="EpisodeCoverCacheService"/> und läuft sonst nur
        /// beim Import. Scheitert die Suche dort, oder stammt die Serie aus einem Bestand ohne
        /// Folgen-Cover, käme nie ein Cover nach. Genau das schließt dieser Schritt.
        /// <para>
        /// Der Dienst filtert selbst auf Episoden ohne Cover und respektiert dabei seinen
        /// Cooldown, damit erfolglose Suchen nicht bei jedem Durchlauf wiederholt werden. Hier
        /// wird deshalb nur vorselektiert, welche Serien überhaupt in Frage kommen; das spart
        /// die Scope-Erzeugung für Serien, die vollständig versorgt sind.
        /// </para>
        /// </remarks>
        internal async Task<int> SearchMissingEpisodeCoversOnlineAsync(CancellationToken ct)
        {
            List<Guid> seriesWithGaps = await FindSeriesWithEpisodeGapsAsync(ct);

            int triggered = 0;
            foreach (Guid seriesId in seriesWithGaps)
            {
                if (ct.IsCancellationRequested) break;

                using IServiceScope searchScope = _scopeFactory.CreateScope();
                EpisodeCoverCacheService cacheService = searchScope.ServiceProvider
                    .GetRequiredService<EpisodeCoverCacheService>();

                await cacheService.CacheCoversAsync(seriesId, ct: ct).ConfigureAwait(false);
                triggered++;
            }

            return triggered;
        }

        /// <summary>
        /// Sucht Cover für Serien, die keines haben, über dieselbe Online-Kette wie die Episoden.
        /// </summary>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Anzahl der neu gefundenen Serien-Cover.</returns>
        /// <remarks>
        /// Für Episoden übernimmt der <see cref="EpisodeCoverCacheService"/> den Cooldown; für
        /// Serien gibt es keinen solchen Dienst, deshalb prüft dieser Schritt
        /// <see cref="Series.CoverLastChecked"/> selbst und setzt den Zeitstempel nach jedem
        /// Versuch, auch ohne Treffer. Ohne das würde jeder Durchlauf dieselben coverlosen
        /// Serien erneut bei den Anbietern anfragen.
        /// </remarks>
        internal async Task<int> SearchMissingSeriesCoversOnlineAsync(CancellationToken ct)
        {
            List<Series> candidates = await FindSeriesWithoutCoverAsync(ct);
            if (candidates.Count == 0)
            {
                return 0;
            }

            _logger.Info("Serien-Cover-Suche: {CandidateCount} Serien ohne Cover und ohne aktiven Cooldown.", candidates.Count);

            int found = 0;

            foreach (Series series in candidates)
            {
                if (ct.IsCancellationRequested) break;

                byte[]? coverBytes = await SearchSeriesCoverOnlineAsync(series.Title, ct).ConfigureAwait(false);

                if (coverBytes is not null)
                {
                    await _coverService.SetSeriesCoverAsync(series.Id, coverBytes, cancellationToken: ct);
                    found++;
                    _logger.Debug(() => $"Serien-Cover über Online-Suche gefunden: \"{series.Title}\" ({coverBytes.Length} Bytes)");
                }

                // Zeitstempel immer setzen, auch ohne Treffer, sonst greift der Cooldown nicht.
                using IServiceScope writeScope = _scopeFactory.CreateScope();
                ISeriesDataService writeService = writeScope.ServiceProvider
                    .GetRequiredService<ISeriesDataService>();
                await writeService.SetCoverLastCheckedAsync(series.Id, _clock.UtcNow, ct).ConfigureAwait(false);

                await Task.Delay(SeriesSearchPause, ct).ConfigureAwait(false);
            }

            return found;
        }

        /// <summary>Die Serien, von denen mindestens eine Folge noch kein Cover hat.</summary>
        private async Task<List<Guid>> FindSeriesWithEpisodeGapsAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            ICoverImageDataService coverImageService = scope.ServiceProvider.GetRequiredService<ICoverImageDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync(ct);
            IReadOnlyList<Episode> allEpisodes = await episodeService.GetBySeriesIdsAsync(allSeries.Select(s => s.Id).ToArray(), ct);
            if (allEpisodes.Count == 0)
            {
                return [];
            }

            IReadOnlyDictionary<Guid, byte[]> existing = await coverImageService.GetImageDataByEntitiesAsync(
                CoverEntityTypes.Episode, allEpisodes.Select(e => e.Id).ToArray(), ct);

            return [.. allEpisodes.Where(e => !existing.ContainsKey(e.Id)).Select(e => e.SeriesId).Distinct()];
        }

        /// <summary>Die Serien ohne Cover, mit Titel und ohne laufenden Cooldown.</summary>
        private async Task<List<Series>> FindSeriesWithoutCoverAsync(CancellationToken ct)
        {
            DateTime cooldownThreshold = _clock.UtcNow.AddDays(-SeriesCoverSearchCooldownDays);

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            ICoverImageDataService coverImageService = scope.ServiceProvider.GetRequiredService<ICoverImageDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync(ct);
            if (allSeries.Count == 0)
            {
                return [];
            }

            IReadOnlyDictionary<Guid, byte[]> existing = await coverImageService.GetImageDataByEntitiesAsync(
                CoverEntityTypes.Series, allSeries.Select(s => s.Id).ToArray(), ct);

            return [.. allSeries.Where(series =>
                !existing.ContainsKey(series.Id)
                && !string.IsNullOrWhiteSpace(series.Title)
                && !(series.CoverLastChecked.HasValue && series.CoverLastChecked.Value > cooldownThreshold))];
        }

        /// <summary>
        /// Fragt die Suchkette nach einem Cover für einen Seriennamen und lädt den besten
        /// Treffer herunter. <see langword="null"/>, wenn nichts über der Relevanzschwelle liegt.
        /// </summary>
        /// <param name="seriesTitle">Titel der Serie, zugleich Suchbegriff.</param>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Online-Suche für ein einzelnes Serien-Cover: HTTP-, Rate-Limit- oder Parser-Fehler der externen Quellen werden zu 'null' normalisiert, damit die Schleife über die übrigen Serien weiterläuft.")]
        private async Task<byte[]?> SearchSeriesCoverOnlineAsync(string seriesTitle, CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ICoverSearchService? coverSearch = scope.ServiceProvider.GetService<ICoverSearchService>();

            if (coverSearch is null) return null;

            try
            {
                IReadOnlyList<CoverSearchResult> results = await coverSearch.SearchAsync(seriesTitle, ct).ConfigureAwait(false);
                CoverSearchResult? best = PickBestSeriesMatch(results, seriesTitle);

                return best is null
                    ? null
                    : await DownloadThrottledAsync(best.FullUrl, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Nur der Abbruch des Aufrufers geht nach oben. Eine Zeitüberschreitung ist ein
                // Fehlschlag dieser Serie; sonst fehlte ihr der Zeitstempel für den Cooldown.
                throw;
            }
            catch (Exception ex)
            {
                _logger.Debug(() => $"Serien-Cover-Suche fehlgeschlagen: {ex.Message} Serie=\"{seriesTitle}\"");
                return null;
            }
        }

        /// <summary>
        /// Der Treffer, der am besten zur Serie passt; <see langword="null"/> unter der Schwelle.
        /// </summary>
        private static CoverSearchResult? PickBestSeriesMatch(IReadOnlyList<CoverSearchResult> results, string seriesTitle)
        {
            CoverSearchResult? best = null;
            int bestScore = 0;

            foreach (CoverSearchResult result in results)
            {
                // Weder Folgennummer noch Folgentitel: Für ein Serien-Cover zählt allein,
                // ob der Treffer zur Serie gehört. Genau dafür vergibt der Scorer 50 Punkte
                // und trifft damit die Mindestschwelle. Jede Folge der Serie taugt als
                // Serienbild, eine bestimmte muss es nicht sein.
                int score = CoverRelevanceScorer.CalculateScore(result.ReleaseTitle, seriesTitle, null, null);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = result;
                }
            }

            return bestScore < CoverRelevanceScorer.MinimumThreshold ? null : best;
        }

        /// <summary>
        /// Lädt ein Cover über den <see cref="ICoverDownloader"/>, wartet davor aber auf den
        /// <see cref="IHostRateLimiter"/>, mit <see cref="CoverFetchPriority.Background"/>,
        /// damit sichtbare UI-Anfragen Vorrang behalten.
        /// </summary>
        /// <param name="url">Geprüfte Cover-Adresse aus dem Suchtreffer.</param>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        private async Task<byte[]?> DownloadThrottledAsync(Uri url, CancellationToken ct)
        {
            if (_rateLimiter is not null)
            {
                await _rateLimiter.WaitAsync(url.Host, CoverFetchPriority.Background, ct).ConfigureAwait(false);
            }

            return await _coverDownloader.DownloadAsync(url, ct).ConfigureAwait(false);
        }
    }
}

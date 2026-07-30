using EchoPlay.Core.Abstractions.Import;
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Die Nachlade-Phasen, die das Netz brauchen: fehlende Cover-Adressen bei den Anbietern
    /// erfragen, Cover über eine bekannte Adresse herunterladen und - als teuerste Stufe -
    /// die Online-Suchkette für Folgen und Serien anstoßen.
    /// </summary>
    /// <remarks>
    /// Alles hier steht unter Fremdeinfluss: Wartelimits der Anbieter, fehlende Zugangsdaten,
    /// abgelaufene Adressen. Deshalb liefert jede Phase eine Zahl statt zu werfen, und
    /// Einzelfehler bleiben bei der betroffenen Serie. Der Cooldown der Serien-Suche liegt
    /// hier, weil es für Serien keinen Dienst gibt, der ihn wie beim
    /// <see cref="EpisodeCoverCacheService"/> mitbringt.
    /// </remarks>
    internal sealed class OnlineCoverPhases
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ICoverService _coverService;
        private readonly ICoverDownloader _coverDownloader;
        private readonly ISpotifyCredentialStore _credentialStore;
        private readonly IClock _clock;
        private readonly IHostRateLimiter? _rateLimiter;
        private readonly ILogger _logger;

        // Cooldown der Serien-Cover-Suche. Gleicher Wert wie im EpisodeCoverCacheService -
        // eine Serie, für die heute kein Cover zu finden war, hat eine Woche später
        // realistischerweise auch keins, und die Anbieter sehen nicht bei jedem Durchlauf
        // dieselbe erfolglose Anfrage.
        private const int SeriesCoverSearchCooldownDays = 7;

        // Pause zwischen zwei Serien-Suchen. Entspricht dem Episoden-Pfad und verteilt die
        // Last, statt alle coverlosen Serien in einem Burst abzufeuern.
        private static readonly TimeSpan SeriesSearchPause = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// Initialisiert die Online-Phasen. Der Logger kommt vom
        /// <see cref="BackgroundCoverService"/>, damit alle Meldungen eines Durchlaufs unter
        /// derselben Quelle im Protokoll stehen.
        /// </summary>
        internal OnlineCoverPhases(
            IServiceScopeFactory scopeFactory,
            ICoverService coverService,
            ICoverDownloader coverDownloader,
            ISpotifyCredentialStore credentialStore,
            IClock clock,
            IHostRateLimiter? rateLimiter,
            ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _coverService = coverService;
            _coverDownloader = coverDownloader;
            _credentialStore = credentialStore;
            _clock = clock;
            _rateLimiter = rateLimiter;
            _logger = logger;
        }
        /// <summary>
        /// Fragt die Provider-API (Spotify/Apple Music) für Online-Serien ab und trägt
        /// fehlende <see cref="Episode.CoverImageUrl"/> auf bestehenden Episoden nach.
        /// Überspringt Serien bei denen alle Episoden bereits eine URL haben.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "URL-Nachtrag pro Serie: HTTP- oder API-Fehler (Spotify/AppleMusic) einer Serie dürfen den Batch für die restlichen Serien nicht abbrechen; Einzelfehler werden als Warning geloggt.")]
        internal async Task<int> UpdateMissingCoverUrlsAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider
                .GetRequiredService<IEpisodeDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync(ct);
            int totalUpdated = 0;

            foreach (Series series in allSeries)
            {
                if (ct.IsCancellationRequested) break;
                if (!series.IsOnlineImported) continue;

                // Provider-Key und Quell-ID ermitteln.
                // Spotify nur nutzen wenn Credentials vorhanden sind – ohne gültige
                // Client-ID/Secret schlägt der Token-Request fehl.
                string? providerKey = series.SpotifyArtistId is not null && _credentialStore.HasCredentials
                    ? ProviderKeys.Spotify
                    : series.AppleMusicArtistId is not null ? ProviderKeys.AppleMusic
                    : null;

                if (providerKey is null) continue;

                string sourceSeriesId = providerKey == ProviderKeys.Spotify
                    ? series.SpotifyArtistId!
                    : series.AppleMusicArtistId!;

                // Prüfen ob Episoden ohne CoverImageUrl existieren
                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id, ct);

                List<Episode> missingUrl = [];
                foreach (Episode episode in episodes)
                {
                    if (string.IsNullOrEmpty(episode.CoverImageUrl))
                    {
                        missingUrl.Add(episode);
                    }
                }

                if (missingUrl.Count == 0) continue;

                // Provider-API abfragen
                try
                {
                    IEpisodeImportSource episodeSource = scope.ServiceProvider
                        .GetRequiredKeyedService<IEpisodeImportSource>(providerKey);

                    IReadOnlyList<ImportEpisode> providerEpisodes =
                        await episodeSource.GetEpisodesAsync(sourceSeriesId, cancellationToken: ct);

                    // Titel → URL Mapping aufbauen
                    Dictionary<string, string> titleToUrl = new(StringComparer.OrdinalIgnoreCase);
                    foreach (ImportEpisode importEp in providerEpisodes)
                    {
                        if (!string.IsNullOrEmpty(importEp.CoverImageUrl))
                        {
                            titleToUrl[importEp.Title] = importEp.CoverImageUrl;
                        }
                    }

                    // Bestehende Episoden updaten
                    int updatedForSeries = 0;
                    foreach (Episode episode in missingUrl)
                    {
                        if (titleToUrl.TryGetValue(episode.Title, out string? coverUrl))
                        {
                            episode.CoverImageUrl = coverUrl;
                            await episodeService.UpdateAsync(episode, ct);
                            updatedForSeries++;
                        }
                    }

                    totalUpdated += updatedForSeries;

                    // Serien-lokal zählen: am akkumulierten Zähler hätte ab der ersten
                    // erfolgreichen Serie jede weitere "URLs gesetzt" geloggt, auch die
                    // ohne einen einzigen Treffer.
                    if (updatedForSeries > 0)
                    {
                        _logger.Debug(() => $"URL-Nachtrag \"{series.Title}\": {missingUrl.Count} geprüft, {updatedForSeries} URLs gesetzt.");
                    }
                }
                catch (OperationCanceledException)
                {
                    // Abbruch ist kein Serien-Fehler. Ohne diesen Zweig hätte der catch darunter
                    // ihn als Warnung protokolliert und die Schleife wäre bis zur
                    // IsCancellationRequested-Prüfung der nächsten Runde weitergelaufen.
                    throw;
                }
                catch (Exception ex)
                {
                    // Einzelne Serien-Fehler nicht abbrechen
                    _logger.Warning("URL-Nachtrag für \"{SeriesTitle}\" fehlgeschlagen: {Reason}", series.Title, ex.Message);
                }
            }

            return totalUpdated;
        }
        /// <summary>
        /// Lädt fehlende Serien-Cover über Provider-URLs (<see cref="Series.CoverImageUrl"/>)
        /// herunter. Kein Online-Suchkette – nur direkte URL-Downloads.
        /// </summary>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        internal async Task<int> DownloadMissingSeriesProviderCoversAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            ICoverImageDataService coverImageService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync(ct);
            List<Series> seriesNeedingCover = [];

            foreach (Series series in allSeries)
            {
                if (!string.IsNullOrEmpty(series.CoverImageUrl))
                {
                    seriesNeedingCover.Add(series);
                }
            }

            if (seriesNeedingCover.Count == 0)
            {
                _logger.Info("Provider-Check Serien: {SeriesCount} Serien, keine mit CoverImageUrl.", allSeries.Count);
                return 0;
            }

            List<Guid> seriesIds = seriesNeedingCover.Select(s => s.Id).ToList();
            IReadOnlyDictionary<Guid, byte[]> existingSeries =
                await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Series, seriesIds, ct);

            int missingSeriesCount = seriesNeedingCover.Count - existingSeries.Count;
            _logger.Info(
                "Provider-Check Serien: {NeedingCoverCount} mit CoverImageUrl, {ExistingCount} bereits in DB, {MissingCount} fehlen.",
                seriesNeedingCover.Count, existingSeries.Count, missingSeriesCount);

            int loaded = 0;

            foreach (Series series in seriesNeedingCover)
            {
                if (ct.IsCancellationRequested) break;
                if (existingSeries.ContainsKey(series.Id)) continue;

                byte[]? coverBytes = await _coverDownloader.DownloadAsync(series.CoverImageUrl!, cancellationToken: ct);

                if (coverBytes is not null)
                {
                    await _coverService.SetSeriesCoverAsync(series.Id, coverBytes, series.CoverImageUrl, ct);
                    loaded++;
                    _logger.Debug(() => $"Serien-Cover geladen: \"{series.Title}\" ({coverBytes.Length} Bytes)");
                }
                else
                {
                    _logger.Warning("Serien-Cover Download fehlgeschlagen: \"{SeriesTitle}\" URL={CoverImageUrl}", series.Title, series.CoverImageUrl);
                }
            }

            return loaded;
        }
        /// <summary>
        /// Lädt fehlende Episoden-Cover über Provider-URLs (<see cref="Episode.CoverImageUrl"/>)
        /// herunter. Kein Online-Suchkette – nur direkte URL-Downloads.
        /// </summary>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        internal async Task<int> DownloadMissingEpisodeProviderCoversAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider
                .GetRequiredService<IEpisodeDataService>();
            ICoverImageDataService coverImageService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync(ct);
            int loaded = 0;
            int totalEpisodeCandidates = 0;
            int totalEpisodeExisting = 0;

            foreach (Series series in allSeries)
            {
                if (ct.IsCancellationRequested) break;

                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id, ct);

                List<Episode> candidates = [];

                foreach (Episode episode in episodes)
                {
                    if (!string.IsNullOrEmpty(episode.CoverImageUrl))
                    {
                        candidates.Add(episode);
                    }
                }

                if (candidates.Count == 0) continue;

                totalEpisodeCandidates += candidates.Count;

                List<Guid> candidateIds = candidates.Select(e => e.Id).ToList();
                IReadOnlyDictionary<Guid, byte[]> existing =
                    await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, candidateIds, ct);

                totalEpisodeExisting += existing.Count;

                foreach (Episode episode in candidates)
                {
                    if (ct.IsCancellationRequested) break;
                    if (existing.ContainsKey(episode.Id)) continue;

                    byte[]? coverBytes = await _coverDownloader.DownloadAsync(episode.CoverImageUrl!, cancellationToken: ct);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes, episode.CoverImageUrl, ct);
                        loaded++;
                    }
                }
            }

            _logger.Info(
                "Provider-Check Episoden: {CandidateCount} mit CoverImageUrl, {ExistingCount} bereits in DB, {Loaded} neu geladen.",
                totalEpisodeCandidates, totalEpisodeExisting, loaded);

            return loaded;
        }
        /// <summary>
        /// Stößt für jede Serie, die noch Episoden ohne Cover hat, die Online-Suchkette an.
        /// </summary>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Anzahl der Serien, für die eine Suche angestoßen wurde.</returns>
        /// <remarks>
        /// Die Kette selbst liegt im <see cref="EpisodeCoverCacheService"/> und lief bisher nur
        /// beim Import. Wer eine Serie vor dessen Einführung importiert hat oder wessen Suche
        /// damals scheiterte, bekam nie ein Cover nachgereicht — genau das schließt dieser Schritt.
        /// <para>
        /// Der Dienst filtert selbst auf Episoden ohne Cover und respektiert dabei seinen
        /// Cooldown, damit erfolglose Suchen nicht bei jedem Durchlauf wiederholt werden. Hier
        /// wird deshalb nur vorselektiert, welche Serien überhaupt in Frage kommen — das spart
        /// die Scope-Erzeugung für Serien, die vollständig versorgt sind.
        /// </para>
        /// </remarks>
        internal async Task<int> SearchMissingEpisodeCoversOnlineAsync(CancellationToken ct)
        {
            List<Guid> seriesWithGaps = [];

            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                ISeriesDataService seriesService = scope.ServiceProvider
                    .GetRequiredService<ISeriesDataService>();
                IEpisodeDataService episodeService = scope.ServiceProvider
                    .GetRequiredService<IEpisodeDataService>();
                ICoverImageDataService coverImageService = scope.ServiceProvider
                    .GetRequiredService<ICoverImageDataService>();

                IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync(ct);
                List<Guid> allSeriesIds = [.. allSeries.Select(s => s.Id)];

                IReadOnlyList<Episode> allEpisodes = await episodeService.GetBySeriesIdsAsync(allSeriesIds, ct);
                if (allEpisodes.Count == 0)
                {
                    return 0;
                }

                List<Guid> episodeIds = [.. allEpisodes.Select(e => e.Id)];
                IReadOnlyDictionary<Guid, byte[]> existing =
                    await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, episodeIds, ct);

                HashSet<Guid> gaps = [];
                foreach (Episode episode in allEpisodes)
                {
                    if (!existing.ContainsKey(episode.Id))
                    {
                        _ = gaps.Add(episode.SeriesId);
                    }
                }

                seriesWithGaps.AddRange(gaps);
            }

            if (seriesWithGaps.Count == 0)
            {
                return 0;
            }

            int angestossen = 0;
            foreach (Guid seriesId in seriesWithGaps)
            {
                if (ct.IsCancellationRequested) break;

                using IServiceScope searchScope = _scopeFactory.CreateScope();
                EpisodeCoverCacheService cacheService = searchScope.ServiceProvider
                    .GetRequiredService<EpisodeCoverCacheService>();

                await cacheService.CacheCoversAsync(seriesId, ct: ct).ConfigureAwait(false);
                angestossen++;
            }

            return angestossen;
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
        /// Versuch — auch ohne Treffer. Ohne das würde jeder Durchlauf dieselben coverlosen
        /// Serien erneut bei den Anbietern anfragen.
        /// </remarks>
        internal async Task<int> SearchMissingSeriesCoversOnlineAsync(CancellationToken ct)
        {
            DateTime cooldownThreshold = _clock.UtcNow.AddDays(-SeriesCoverSearchCooldownDays);
            List<Series> candidates = [];

            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                ISeriesDataService seriesService = scope.ServiceProvider
                    .GetRequiredService<ISeriesDataService>();
                ICoverImageDataService coverImageService = scope.ServiceProvider
                    .GetRequiredService<ICoverImageDataService>();

                IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync(ct);
                if (allSeries.Count == 0)
                {
                    return 0;
                }

                List<Guid> seriesIds = [.. allSeries.Select(s => s.Id)];
                IReadOnlyDictionary<Guid, byte[]> existing =
                    await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Series, seriesIds, ct);

                foreach (Series series in allSeries)
                {
                    if (existing.ContainsKey(series.Id)) continue;
                    if (string.IsNullOrWhiteSpace(series.Title)) continue;

                    if (series.CoverLastChecked.HasValue
                        && series.CoverLastChecked.Value > cooldownThreshold)
                    {
                        continue;
                    }

                    candidates.Add(series);
                }
            }

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

                // Zeitstempel immer setzen – auch ohne Treffer, sonst greift der Cooldown nicht.
                using IServiceScope writeScope = _scopeFactory.CreateScope();
                ISeriesDataService writeService = writeScope.ServiceProvider
                    .GetRequiredService<ISeriesDataService>();
                await writeService.SetCoverLastCheckedAsync(series.Id, _clock.UtcNow, ct).ConfigureAwait(false);

                await Task.Delay(SeriesSearchPause, ct).ConfigureAwait(false);
            }

            return found;
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

                CoverSearchResult? best = null;
                int bestScore = 0;

                foreach (CoverSearchResult result in results)
                {
                    // Weder Folgennummer noch Folgentitel: Für ein Serien-Cover zählt allein,
                    // ob der Treffer zur Serie gehört. Genau dafür vergibt der Scorer 50 Punkte
                    // und trifft damit die Mindestschwelle — jede Folge der Serie taugt als
                    // Serienbild, eine bestimmte muss es nicht sein.
                    int score = CoverRelevanceScorer.CalculateScore(result.ReleaseTitle, seriesTitle, null, null);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = result;
                    }
                }

                if (best is null || bestScore < CoverRelevanceScorer.MinimumThreshold)
                {
                    return null;
                }

                return await DownloadThrottledAsync(best.FullUrl, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Debug(() => $"Serien-Cover-Suche fehlgeschlagen: {ex.Message} Serie=\"{seriesTitle}\"");
                return null;
            }
        }
        /// <summary>
        /// Lädt ein Cover über den <see cref="ICoverDownloader"/>, wartet davor aber auf den
        /// <see cref="IHostRateLimiter"/> — mit <see cref="CoverFetchPriority.Background"/>,
        /// damit sichtbare UI-Anfragen Vorrang behalten.
        /// </summary>
        /// <param name="url">Absolute Cover-URL aus dem Suchtreffer.</param>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "Cover-URL stammt aus dem Suchergebnis der externen Provider-API und wird in der gesamten Cover-Pipeline als string verwaltet (gleiches Muster wie ICoverDownloader).")]
        private async Task<byte[]?> DownloadThrottledAsync(string url, CancellationToken ct)
        {
            if (_rateLimiter is not null && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                await _rateLimiter.WaitAsync(uri.Host, CoverFetchPriority.Background, ct).ConfigureAwait(false);
            }

            return await _coverDownloader.DownloadAsync(url, ct).ConfigureAwait(false);
        }
    }
}

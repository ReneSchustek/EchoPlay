using EchoPlay.Core.Http;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
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
    /// Die Nachlade-Phasen über die Anbieter: fehlende Cover-Adressen bei den Anbietern
    /// erfragen und Cover über eine bekannte Adresse herunterladen. Die teuerste Stufe, die
    /// Online-Suchkette, liegt in <see cref="OnlineCoverSearchPhases"/>.
    /// </summary>
    /// <remarks>
    /// Alles hier steht unter Fremdeinfluss: Wartelimits der Anbieter, fehlende Zugangsdaten,
    /// abgelaufene Adressen. Deshalb liefert jede Phase eine Zahl statt zu werfen, und
    /// Einzelfehler bleiben bei der betroffenen Serie.
    /// </remarks>
    internal sealed class OnlineCoverPhases
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ICoverService _coverService;
        private readonly ICoverDownloader _coverDownloader;
        private readonly ISpotifyCredentialStore _credentialStore;
        private readonly ILogger _logger;

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
            ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _coverService = coverService;
            _coverDownloader = coverDownloader;
            _credentialStore = credentialStore;
            _logger = logger;
        }

        /// <summary>
        /// Fragt die Provider-API (Spotify/Apple Music) für Online-Serien ab und trägt
        /// fehlende <see cref="Episode.CoverImageUrl"/> auf bestehenden Episoden nach.
        /// Überspringt Serien bei denen alle Episoden bereits eine URL haben.
        /// </summary>
        internal async Task<int> UpdateMissingCoverUrlsAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IReadOnlyList<Series> allSeries = await scope.ServiceProvider.GetRequiredService<ISeriesDataService>().GetAllAsync(ct);
            int totalUpdated = 0;

            foreach (Series series in allSeries)
            {
                if (ct.IsCancellationRequested) break;
                if (!series.IsOnlineImported || ResolveSource(series) is not { } source) continue;

                // Prüfen ob Episoden ohne CoverImageUrl existieren
                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id, ct);
                List<Episode> missingUrl = [.. episodes.Where(e => string.IsNullOrEmpty(e.CoverImageUrl))];
                if (missingUrl.Count == 0) continue;

                totalUpdated += await UpdateSeriesUrlsAsync(scope.ServiceProvider, series, source, missingUrl, ct);
            }

            return totalUpdated;
        }

        /// <summary>
        /// Provider-Key und Quell-ID einer Serie. Spotify nur nutzen, wenn Credentials vorhanden
        /// sind; ohne gültige Client-ID/Secret schlägt der Token-Request fehl.
        /// </summary>
        private (string ProviderKey, string SourceSeriesId)? ResolveSource(Series series)
        {
            if (series.SpotifyArtistId is not null && _credentialStore.HasCredentials)
            {
                return (ProviderKeys.Spotify, series.SpotifyArtistId);
            }

            return series.AppleMusicArtistId is not null ? (ProviderKeys.AppleMusic, series.AppleMusicArtistId) : null;
        }

        /// <summary>
        /// Fragt den Anbieter für eine Serie ab und trägt die fehlenden Adressen nach. Ein Fehler
        /// trifft nur diese Serie.
        /// </summary>
        /// <returns>Wie viele Folgen eine Adresse bekamen.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "URL-Nachtrag pro Serie: HTTP- oder API-Fehler (Spotify/AppleMusic) einer Serie dürfen den Batch für die restlichen Serien nicht abbrechen; Einzelfehler werden als Warning geloggt.")]
        private async Task<int> UpdateSeriesUrlsAsync(
            IServiceProvider services, Series series, (string ProviderKey, string SourceSeriesId) source, List<Episode> missingUrl, CancellationToken ct)
        {
            try
            {
                IEpisodeImportSource episodeSource = services.GetRequiredKeyedService<IEpisodeImportSource>(source.ProviderKey);
                IReadOnlyList<ImportEpisode> providerEpisodes =
                    await episodeSource.GetEpisodesAsync(source.SourceSeriesId, cancellationToken: ct);

                int updatedForSeries = await ApplyUrlsAsync(services.GetRequiredService<IEpisodeDataService>(), missingUrl, providerEpisodes, ct);

                // Serien-lokal zählen: am akkumulierten Zähler hätte ab der ersten
                // erfolgreichen Serie jede weitere "URLs gesetzt" geloggt, auch die
                // ohne einen einzigen Treffer.
                if (updatedForSeries > 0)
                {
                    _logger.Debug(() => $"URL-Nachtrag \"{series.Title}\": {missingUrl.Count} geprüft, {updatedForSeries} URLs gesetzt.");
                }

                return updatedForSeries;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Ein Abbruch des Aufrufers ist kein Serien-Fehler und gehört nach oben. Eine
                // Zeitüberschreitung beim Anbieter dagegen trifft nur diese Serie und landet
                // im Zweig darunter, sonst bräche sie den Nachtrag für alle übrigen ab.
                throw;
            }
            catch (Exception ex)
            {
                // Einzelne Serien-Fehler nicht abbrechen
                _logger.Warning("URL-Nachtrag für \"{SeriesTitle}\" fehlgeschlagen: {Reason}", series.Title, ex.Message);
                return 0;
            }
        }

        /// <summary>Ordnet die Adressen des Anbieters über den Titel zu und speichert sie.</summary>
        private static async Task<int> ApplyUrlsAsync(
            IEpisodeDataService episodeService, List<Episode> missingUrl, IReadOnlyList<ImportEpisode> providerEpisodes, CancellationToken ct)
        {
            Dictionary<string, Uri> titleToUrl = new(StringComparer.OrdinalIgnoreCase);
            foreach (ImportEpisode importEp in providerEpisodes.Where(e => e.CoverImageUrl is not null))
            {
                titleToUrl[importEp.Title] = importEp.CoverImageUrl!;
            }

            int updated = 0;
            foreach (Episode episode in missingUrl)
            {
                if (titleToUrl.TryGetValue(episode.Title, out Uri? coverUrl))
                {
                    episode.CoverImageUrl = coverUrl.OriginalString;
                    await episodeService.UpdateAsync(episode, ct);
                    updated++;
                }
            }

            return updated;
        }

        /// <summary>
        /// Lädt fehlende Serien-Cover über Provider-URLs (<see cref="Series.CoverImageUrl"/>)
        /// herunter. Keine Online-Suchkette, nur direkte URL-Downloads.
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
            List<Series> seriesNeedingCover = [.. allSeries.Where(series => !string.IsNullOrEmpty(series.CoverImageUrl))];
            if (seriesNeedingCover.Count == 0)
            {
                _logger.Info("Provider-Check Serien: {SeriesCount} Serien, keine mit CoverImageUrl.", allSeries.Count);
                return 0;
            }

            IReadOnlyDictionary<Guid, byte[]> existingSeries = await coverImageService.GetImageDataByEntitiesAsync(
                CoverEntityTypes.Series, seriesNeedingCover.Select(s => s.Id).ToArray(), ct);
            _logger.Info(
                "Provider-Check Serien: {NeedingCoverCount} mit CoverImageUrl, {ExistingCount} bereits in DB, {MissingCount} fehlen.",
                seriesNeedingCover.Count, existingSeries.Count, seriesNeedingCover.Count - existingSeries.Count);

            int loaded = 0;
            foreach (Series series in seriesNeedingCover)
            {
                if (ct.IsCancellationRequested) break;
                if (!existingSeries.ContainsKey(series.Id) && await TryLoadSeriesCoverAsync(series, ct))
                {
                    loaded++;
                }
            }

            return loaded;
        }

        /// <summary>Lädt das Cover einer Serie von der Adresse des Anbieters und speichert es.</summary>
        /// <returns><see langword="true"/>, wenn ein Cover geladen wurde.</returns>
        private async Task<bool> TryLoadSeriesCoverAsync(Series series, CancellationToken ct)
        {
            Uri? coverAddress = WebAddress.TryParse(series.CoverImageUrl);
            if (coverAddress is null) return false;

            byte[]? coverBytes = await _coverDownloader.DownloadAsync(coverAddress, cancellationToken: ct);
            if (coverBytes is null)
            {
                _logger.Warning("Serien-Cover Download fehlgeschlagen: \"{SeriesTitle}\" URL={CoverImageUrl}", series.Title, series.CoverImageUrl);
                return false;
            }

            await _coverService.SetSeriesCoverAsync(series.Id, coverBytes, coverAddress, ct);
            _logger.Debug(() => $"Serien-Cover geladen: \"{series.Title}\" ({coverBytes.Length} Bytes)");
            return true;
        }

        /// <summary>
        /// Lädt fehlende Episoden-Cover über Provider-URLs (<see cref="Episode.CoverImageUrl"/>)
        /// herunter. Keine Online-Suchkette, nur direkte URL-Downloads.
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

                (int candidates, int existing, int loadedHere) =
                    await LoadEpisodeCoversOfSeriesAsync(series.Id, episodeService, coverImageService, ct);
                totalEpisodeCandidates += candidates;
                totalEpisodeExisting += existing;
                loaded += loadedHere;
            }

            _logger.Info(
                "Provider-Check Episoden: {CandidateCount} mit CoverImageUrl, {ExistingCount} bereits in DB, {Loaded} neu geladen.",
                totalEpisodeCandidates, totalEpisodeExisting, loaded);

            return loaded;
        }

        /// <summary>
        /// Lädt die fehlenden Folgen-Cover einer Serie von den Adressen des Anbieters.
        /// </summary>
        /// <returns>Folgen mit Adresse, davon schon mit Cover, und neu geladene.</returns>
        private async Task<(int Candidates, int Existing, int Loaded)> LoadEpisodeCoversOfSeriesAsync(
            Guid seriesId, IEpisodeDataService episodeService, ICoverImageDataService coverImageService, CancellationToken ct)
        {
            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(seriesId, ct);
            List<Episode> candidates = [.. episodes.Where(episode => !string.IsNullOrEmpty(episode.CoverImageUrl))];
            if (candidates.Count == 0)
            {
                return (0, 0, 0);
            }

            IReadOnlyDictionary<Guid, byte[]> existing = await coverImageService.GetImageDataByEntitiesAsync(
                CoverEntityTypes.Episode, candidates.Select(e => e.Id).ToArray(), ct);

            int loaded = 0;
            foreach (Episode episode in candidates)
            {
                if (ct.IsCancellationRequested) break;
                if (existing.ContainsKey(episode.Id)) continue;

                Uri? coverAddress = WebAddress.TryParse(episode.CoverImageUrl);
                if (coverAddress is null) continue;

                byte[]? coverBytes = await _coverDownloader.DownloadAsync(coverAddress, cancellationToken: ct);
                if (coverBytes is not null)
                {
                    await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes, coverAddress, ct);
                    loaded++;
                }
            }

            return (candidates.Count, existing.Count, loaded);
        }
    }
}

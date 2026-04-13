using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Hintergrund-Service der fehlende Cover automatisch nachlädt.
    /// Läuft beim App-Start einmalig und danach periodisch (alle 30 Minuten).
    /// Der Nutzer merkt nichts davon – Cover erscheinen einfach irgendwann.
    ///
    /// Ablauf pro Durchlauf:
    /// 1. Alle lokalen Episoden mit Ordner aber ohne Cover in CoverImages ermitteln
    /// 2. Cover aus dem Dateisystem laden (cover.jpg / ID3-Tags)
    /// 3. In CoverImages speichern
    /// </summary>
    public sealed class BackgroundCoverService : IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly CoverService _coverService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ISpotifyCredentialStore _credentialStore;
        private readonly BackgroundCoverServiceOptions _options;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;


        /// <summary>
        /// Initialisiert den Background-Cover-Service.
        /// </summary>
        public BackgroundCoverService(
            IServiceScopeFactory scopeFactory,
            CoverService coverService,
            IHttpClientFactory httpClientFactory,
            ISpotifyCredentialStore credentialStore,
            BackgroundCoverServiceOptions options,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _coverService = coverService;
            _httpClientFactory = httpClientFactory;
            _credentialStore = credentialStore;
            _options = options;
            _logger = loggerFactory.CreateLogger("BackgroundCoverService");
        }

        /// <summary>
        /// Startet den Hintergrund-Task. Darf nur einmal aufgerufen werden.
        /// </summary>
        public void Start()
        {
            if (_backgroundTask is not null) return;

            _cts = new CancellationTokenSource();
            _backgroundTask = RunAsync(_cts.Token);
        }

        /// <summary>
        /// Stoppt den Hintergrund-Task sauber.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
        }

        /// <summary>
        /// Prüft alle Serien und Episoden auf fehlende Cover und lädt sie nach.
        /// Wird vom StartupValidator bei jedem App-Start aufgerufen.
        /// Wenn alle Cover vorhanden sind, läuft nur die Batch-Query (Millisekunden).
        /// Phase 1: Lokale Dateien (cover.jpg, ID3-Tags) → in DB
        /// Phase 2: Provider-URLs (Apple Music, Spotify) → in DB
        /// Keine Online-Suchkette (zu langsam für den Startup).
        /// </summary>
        /// <returns>Anzahl der geladenen Cover.</returns>
        public async Task<int> RunOnceAsync()
        {
            using CancellationTokenSource cts = new();
            CancellationToken ct = cts.Token;

            int loaded = 0;

            // Phase 1: Lokale Cover aus Dateisystem (Serien + Episoden)
            int localLoaded = await LoadMissingLocalCoversAsync(ct);
            loaded += localLoaded;
            _logger.Info($"RunOnce Phase 1 (lokal): {localLoaded} Cover geladen.");

            // Phase 1b: Cover von lokalen auf Online-Episoden kopieren (reines SQL)
            int copied = await CopyLocalToOnlineAsync();
            loaded += copied;
            _logger.Info($"RunOnce Phase 1b (lokal→online Kopie): {copied} Cover kopiert.");

            // Phase 2a: CoverImageUrl bei Online-Episoden nachtragen (Provider-API)
            int urlsUpdated = await UpdateMissingCoverUrlsAsync(ct);
            _logger.Info($"RunOnce Phase 2a (URL-Nachtrag): {urlsUpdated} URLs gesetzt.");

            // Phase 2b: Fehlende Cover über Provider-URLs herunterladen
            int providerLoaded = await DownloadMissingProviderCoversAsync(ct);
            loaded += providerLoaded;
            _logger.Info($"RunOnce Phase 2b (Provider-URL Download): {providerLoaded} Cover geladen.");

            return loaded;
        }

        /// <summary>
        /// Hauptschleife: einmaliger Durchlauf beim Start, dann periodisch.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Scan-Schleife: TagLib-, DB-, HTTP- oder IO-Fehler einer einzelnen Iteration duerfen die Cover-Schleife nicht beenden; Fehler werden als Warning geloggt und die naechste Iteration faehrt fort.")]
        private async Task RunAsync(CancellationToken ct)
        {
            // Kurz warten, damit die App vollständig initialisiert ist
            await Task.Delay(_options.InitialDelay, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    int localLoaded = await LoadMissingLocalCoversAsync(ct);
                    int copied = await CopyLocalToOnlineAsync();
                    int urlsUpdated = await UpdateMissingCoverUrlsAsync(ct);
                    int providerLoaded = await DownloadMissingProviderCoversAsync(ct);

                    int total = localLoaded + copied + providerLoaded;

                    if (total > 0)
                    {
                        _logger.Info($"Hintergrund: {localLoaded} lokal, {copied} kopiert, {providerLoaded} Provider.");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Hintergrund-Cover-Scan fehlgeschlagen: {ex.Message}");
                }

                try
                {
                    await Task.Delay(_options.Interval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Stellt sicher, dass alle lokalen Episoden einer Serie (nach Titel) ihre Cover
        /// in CoverImages haben. Wird synchron vor der Anzeige aufgerufen, damit der
        /// CoverCopyService danach Quellen findet.
        /// </summary>
        /// <param name="seriesTitle">Titel der Serie (z.B. "Fünf Freunde").</param>
        /// <returns>Anzahl der neu geladenen Cover.</returns>
        public async Task<int> EnsureLocalCoversForSeriesAsync(string seriesTitle)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider
                .GetRequiredService<IEpisodeDataService>();
            ILocalTrackDataService trackService = scope.ServiceProvider
                .GetRequiredService<ILocalTrackDataService>();
            ILocalCoverLoader coverLoader = scope.ServiceProvider
                .GetRequiredService<ILocalCoverLoader>();
            ICoverImageDataService coverImageService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            // Alle Serien mit gleichem Titel finden (lokal + online)
            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();
            int loaded = 0;

            foreach (Series series in allSeries)
            {
                if (!string.Equals(series.Title, seriesTitle, StringComparison.OrdinalIgnoreCase))
                    continue;

                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id);

                List<Episode> candidates = [];

                foreach (Episode episode in episodes)
                {
                    if (!string.IsNullOrEmpty(episode.LocalFolderPath))
                    {
                        candidates.Add(episode);
                    }
                }

                if (candidates.Count == 0) continue;

                List<Guid> candidateIds = candidates.Select(e => e.Id).ToList();
                IReadOnlyDictionary<Guid, byte[]> existing =
                    await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, candidateIds);

                foreach (Episode episode in candidates)
                {
                    if (existing.ContainsKey(episode.Id)) continue;

                    string? firstTrackPath = null;
                    IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episode.Id);

                    if (tracks.Count > 0)
                    {
                        firstTrackPath = tracks[0].FilePath;
                    }

                    byte[]? coverBytes = await coverLoader.LoadAsync(
                        episode.LocalFolderPath, firstTrackPath);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes);
                        loaded++;
                    }
                }
            }

            if (loaded > 0)
            {
                _logger.Info($"Lokale Cover für \"{seriesTitle}\": {loaded} in DB geladen.");
            }

            return loaded;
        }

        /// <summary>
        /// Kopiert vorhandene Cover von lokalen Episoden auf Online-Episoden derselben Serie.
        /// Nutzt <see cref="ICoverCopyService"/> – reines SQL (INSERT OR IGNORE), kein Netzwerk.
        /// Nur Episoden ohne vorhandenes Cover werden befüllt.
        /// Schnell genug für den Splash (eine SQL-Query pro Online-Serie).
        /// </summary>
        public async Task<int> CopyLocalToOnlineAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            ICoverCopyService coverCopy = scope.ServiceProvider
                .GetRequiredService<ICoverCopyService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();
            int totalCopied = 0;

            foreach (Series series in allSeries)
            {
                if (!series.IsOnlineImported) continue;

                int copied = await coverCopy.CopyFromMatchingEpisodesAsync(series.Id);
                totalCopied += copied;
            }

            return totalCopied;
        }

        /// <summary>
        /// Fragt die Provider-API (Spotify/Apple Music) für Online-Serien ab und trägt
        /// fehlende <see cref="Episode.CoverImageUrl"/> auf bestehenden Episoden nach.
        /// Überspringt Serien bei denen alle Episoden bereits eine URL haben.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "URL-Nachtrag pro Serie: HTTP- oder API-Fehler (Spotify/AppleMusic) einer Serie duerfen den Batch fuer die restlichen Serien nicht abbrechen; Einzelfehler werden als Warning geloggt.")]
        private async Task<int> UpdateMissingCoverUrlsAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider
                .GetRequiredService<IEpisodeDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();
            int totalUpdated = 0;

            foreach (Series series in allSeries)
            {
                if (ct.IsCancellationRequested) break;
                if (!series.IsOnlineImported) continue;

                // Provider-Key und Quell-ID ermitteln.
                // Spotify nur nutzen wenn Credentials vorhanden sind – ohne gültige
                // Client-ID/Secret schlägt der Token-Request fehl.
                string? providerKey = series.SpotifyArtistId is not null && _credentialStore.HasCredentials
                    ? "Spotify"
                    : series.AppleMusicArtistId is not null ? "AppleMusic"
                    : null;

                if (providerKey is null) continue;

                string sourceSeriesId = providerKey == "Spotify"
                    ? series.SpotifyArtistId!
                    : series.AppleMusicArtistId!;

                // Prüfen ob Episoden ohne CoverImageUrl existieren
                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id);

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
                        await episodeSource.GetEpisodesAsync(sourceSeriesId);

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
                    foreach (Episode episode in missingUrl)
                    {
                        if (titleToUrl.TryGetValue(episode.Title, out string? coverUrl))
                        {
                            episode.CoverImageUrl = coverUrl;
                            await episodeService.UpdateAsync(episode);
                            totalUpdated++;
                        }
                    }

                    if (totalUpdated > 0)
                    {
                        _logger.Debug($"URL-Nachtrag \"{series.Title}\": {missingUrl.Count} geprüft, URLs gesetzt.");
                    }
                }
                catch (Exception ex)
                {
                    // Einzelne Serien-Fehler nicht abbrechen
                    _logger.Warning($"URL-Nachtrag für \"{series.Title}\" fehlgeschlagen: {ex.Message}");
                }
            }

            return totalUpdated;
        }

        /// <summary>
        /// Sucht Serien und Episoden mit lokalem Ordner aber ohne Cover in CoverImages
        /// und lädt die Cover aus dem Dateisystem (cover.jpg / ID3-Tags).
        /// </summary>
        private async Task<int> LoadMissingLocalCoversAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider
                .GetRequiredService<IEpisodeDataService>();
            ILocalTrackDataService trackService = scope.ServiceProvider
                .GetRequiredService<ILocalTrackDataService>();
            ILocalCoverLoader coverLoader = scope.ServiceProvider
                .GetRequiredService<ILocalCoverLoader>();
            ICoverImageDataService coverImageService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            IReadOnlyList<Series> allSeries;

            using (IServiceScope seriesScope = _scopeFactory.CreateScope())
            {
                ISeriesDataService seriesService = seriesScope.ServiceProvider
                    .GetRequiredService<ISeriesDataService>();
                allSeries = await seriesService.GetAllAsync();
            }

            int loaded = 0;

            _logger.Info($"Lokal-Check: {allSeries.Count} Serien gesamt.");

            // ── Serien-Cover aus Dateisystem ────────────────────────────────────────

            List<Series> seriesWithFolder = [];

            foreach (Series series in allSeries)
            {
                if (!string.IsNullOrEmpty(series.LocalFolderPath))
                {
                    seriesWithFolder.Add(series);
                }
            }

            _logger.Info($"Lokal-Check Serien: {seriesWithFolder.Count} mit LocalFolderPath.");

            if (seriesWithFolder.Count > 0)
            {
                List<Guid> seriesIds = seriesWithFolder.Select(s => s.Id).ToList();
                IReadOnlyDictionary<Guid, byte[]> existingSeries =
                    await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Series, seriesIds);

                int missingSeries = seriesWithFolder.Count - existingSeries.Count;
                _logger.Info($"Lokal-Check Serien: {existingSeries.Count} in DB, {missingSeries} fehlen.");

                foreach (Series series in seriesWithFolder)
                {
                    if (ct.IsCancellationRequested) break;
                    if (existingSeries.ContainsKey(series.Id)) continue;

                    byte[]? coverBytes = await coverLoader.LoadAsync(series.LocalFolderPath, null);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetSeriesCoverAsync(series.Id, coverBytes);
                        loaded++;
                        _logger.Debug($"Lokal: Serien-Cover geladen \"{series.Title}\" aus {series.LocalFolderPath}");
                    }
                    else
                    {
                        _logger.Debug($"Lokal: Kein Cover gefunden für \"{series.Title}\" in {series.LocalFolderPath}");
                    }
                }
            }

            // ── Episoden-Cover aus Dateisystem ──────────────────────────────────────
            //
            // Drei Batch-Queries statt N+1:
            // 1) Alle Episoden aller Serien in einem Roundtrip (GetBySeriesIdsAsync).
            // 2) Alle bereits vorhandenen Episoden-Cover in einem Roundtrip (GetImageDataByEntitiesAsync).
            // 3) Erste Tracks aller Kandidaten in einem Roundtrip (GetFirstTracksByEpisodeIdsAsync).
            // Anschließend nur noch CPU-/IO-Arbeit pro Episode – kein DB-Roundtrip mehr in der Schleife.

            int totalEpCandidates = 0;
            int totalEpExisting   = 0;
            int totalEpLoaded     = 0;
            int totalEpNotFound   = 0;

            List<Guid> allSeriesIds = [.. allSeries.Select(s => s.Id)];
            IReadOnlyList<Episode> allEpisodes = await episodeService.GetBySeriesIdsAsync(allSeriesIds);

            List<Episode> candidates = [];
            foreach (Episode episode in allEpisodes)
            {
                if (!string.IsNullOrEmpty(episode.LocalFolderPath))
                {
                    candidates.Add(episode);
                }
            }

            if (candidates.Count == 0)
            {
                _logger.Info("Lokal-Check Episoden: keine Kandidaten mit lokalem Ordner gefunden.");
                return loaded;
            }

            totalEpCandidates = candidates.Count;

            List<Guid> candidateIds = [.. candidates.Select(e => e.Id)];
            IReadOnlyDictionary<Guid, byte[]> existing =
                await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, candidateIds);

            totalEpExisting = existing.Count;

            // Nur für die noch fehlenden Episoden den ersten Track laden – Batch-Query.
            List<Guid> missingIds = [.. candidates
                .Where(e => !existing.ContainsKey(e.Id))
                .Select(e => e.Id)];

            IReadOnlyDictionary<Guid, LocalTrack> firstTracks =
                await trackService.GetFirstTracksByEpisodeIdsAsync(missingIds);

            foreach (Episode episode in candidates)
            {
                if (ct.IsCancellationRequested) break;
                if (existing.ContainsKey(episode.Id)) continue;

                string? firstTrackPath = firstTracks.TryGetValue(episode.Id, out LocalTrack? firstTrack)
                    ? firstTrack.FilePath
                    : null;

                byte[]? coverBytes = await coverLoader.LoadAsync(
                    episode.LocalFolderPath, firstTrackPath);

                if (coverBytes is not null)
                {
                    await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes);
                    loaded++;
                    totalEpLoaded++;
                }
                else
                {
                    totalEpNotFound++;
                }
            }

            _logger.Info($"Lokal-Check Episoden: {totalEpCandidates} mit Ordner, " +
                $"{totalEpExisting} in DB, {totalEpLoaded} geladen, {totalEpNotFound} ohne Cover-Datei.");

            return loaded;
        }

        /// <summary>
        /// Lädt fehlende Cover über Provider-URLs herunter (Apple Music, Spotify).
        /// Nur für Serien/Episoden die eine CoverImageUrl haben aber kein Cover in der DB.
        /// Kein Online-Suchkette – nur direkte URL-Downloads.
        /// </summary>
        private async Task<int> DownloadMissingProviderCoversAsync(CancellationToken ct)
        {
            int loaded = 0;

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider
                .GetRequiredService<IEpisodeDataService>();
            ICoverImageDataService coverImageService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            // ── Serien ohne Cover mit Provider-URL ──────────────────────────────────

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();
            List<Series> seriesNeedingCover = [];

            foreach (Series series in allSeries)
            {
                if (!string.IsNullOrEmpty(series.CoverImageUrl))
                {
                    seriesNeedingCover.Add(series);
                }
            }

            _logger.Info($"Provider-Check Serien: {seriesNeedingCover.Count} mit CoverImageUrl von {allSeries.Count} gesamt.");

            if (seriesNeedingCover.Count > 0)
            {
                List<Guid> seriesIds = seriesNeedingCover.Select(s => s.Id).ToList();
                IReadOnlyDictionary<Guid, byte[]> existingSeries =
                    await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Series, seriesIds);

                int missingSeriesCount = seriesNeedingCover.Count - existingSeries.Count;
                _logger.Info($"Provider-Check Serien: {existingSeries.Count} bereits in DB, {missingSeriesCount} fehlen.");

                foreach (Series series in seriesNeedingCover)
                {
                    if (ct.IsCancellationRequested) break;
                    if (existingSeries.ContainsKey(series.Id)) continue;

                    byte[]? coverBytes = await DownloadSafeAsync(series.CoverImageUrl!);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetSeriesCoverAsync(series.Id, coverBytes, series.CoverImageUrl);
                        loaded++;
                        _logger.Debug($"Serien-Cover geladen: \"{series.Title}\" ({coverBytes.Length} Bytes)");
                    }
                    else
                    {
                        _logger.Warning($"Serien-Cover Download fehlgeschlagen: \"{series.Title}\" URL={series.CoverImageUrl}");
                    }
                }
            }

            // ── Episoden ohne Cover mit Provider-URL ────────────────────────────────

            int totalEpisodeCandidates = 0;
            int totalEpisodeExisting = 0;

            foreach (Series series in allSeries)
            {
                if (ct.IsCancellationRequested) break;

                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id);

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
                    await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, candidateIds);

                totalEpisodeExisting += existing.Count;

                foreach (Episode episode in candidates)
                {
                    if (ct.IsCancellationRequested) break;
                    if (existing.ContainsKey(episode.Id)) continue;

                    byte[]? coverBytes = await DownloadSafeAsync(episode.CoverImageUrl!);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes, episode.CoverImageUrl);
                        loaded++;
                    }
                }
            }

            _logger.Info($"Provider-Check Episoden: {totalEpisodeCandidates} mit CoverImageUrl, " +
                $"{totalEpisodeExisting} bereits in DB, {loaded} neu geladen.");

            return loaded;
        }

        /// <summary>
        /// Lädt Bilddaten von einer URL. Null bei Fehler.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Download-Wrapper: HTTP-, Timeout-, TLS- oder Redirect-Fehler beim Laden einzelner Cover-URLs werden alle zu 'null' normalisiert, damit der Aufrufer die Episode ueberspringen und mit anderen weitermachen kann.")]
        private async Task<byte[]?> DownloadSafeAsync(string url)
        {
            try
            {
                HttpClient client = _httpClientFactory.CreateClient("CoverDownload");
                byte[] data = await client.GetByteArrayAsync(new Uri(url, UriKind.Absolute)).ConfigureAwait(false);
                return data.Length > 0 ? data : null;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Cover-Download fehlgeschlagen: {ex.Message} URL={url}");
                return null;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}

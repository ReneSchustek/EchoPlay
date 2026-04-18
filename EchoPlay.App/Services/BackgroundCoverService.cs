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
    public class BackgroundCoverService : IDisposable
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
        /// Startet den Hintergrund-Task. Idempotent — mehrfacher Aufruf ist no-op.
        /// </summary>
        public void Start()
        {
            if (_backgroundTask is not null) return;

            _cts = new CancellationTokenSource();
            // Task.Run entkoppelt von einem evtl. vorhandenen UI-SynchronizationContext und
            // macht den Task als Referenz greifbar, damit StopAsync mit Timeout warten kann.
            _backgroundTask = Task.Run(() => RunAsync(_cts.Token));
        }

        /// <summary>
        /// Stoppt den Hintergrund-Task sauber und wartet mit Timeout auf das Ende
        /// der laufenden Iteration. Bei Timeout wird eine Warnung geloggt.
        /// </summary>
        /// <param name="timeout">Maximale Wartezeit.</param>
        public async Task StopAsync(TimeSpan timeout)
        {
            if (_cts is null || _backgroundTask is null) return;

            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await _backgroundTask.WaitAsync(timeout).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Erwartet: Iteration hat den CancellationToken sauber beobachtet.
            }
            catch (TimeoutException)
            {
                _logger.Warning($"BackgroundCoverService: Iteration hat Timeout ({timeout.TotalSeconds:F1}s) überschritten und wird hart abgebrochen.");
            }

            _cts.Dispose();
            _cts = null;
            _backgroundTask = null;
        }

        /// <summary>
        /// Prüft alle Serien und Episoden auf fehlende Cover und lädt sie nach.
        /// Wird vom periodischen Hintergrund-Loop aufgerufen – nicht mehr vom Splash-Pfad.
        /// Phase 1: Lokale Dateien (cover.jpg, ID3-Tags) für Serien und Episoden → in DB.
        /// Phase 2: Provider-URLs (Apple Music, Spotify) für Serien und Episoden → in DB.
        /// Keine Online-Suchkette (zu langsam für den Startup).
        /// </summary>
        /// <returns>Anzahl der geladenen Cover.</returns>
        public virtual async Task<int> RunOnceAsync()
        {
            using CancellationTokenSource cts = new();
            CancellationToken ct = cts.Token;

            int loaded = 0;

            // Phase 1a: Lokale Serien-Cover
            int seriesLocalLoaded = await LoadMissingLocalSeriesCoversAsync(ct);
            // Phase 1b: Lokale Episoden-Cover
            int episodeLocalLoaded = await LoadMissingLocalEpisodeCoversAsync(ct);
            loaded += seriesLocalLoaded + episodeLocalLoaded;
            _logger.Info($"RunOnce Phase 1 (lokal): {seriesLocalLoaded + episodeLocalLoaded} Cover geladen " +
                $"({seriesLocalLoaded} Serien, {episodeLocalLoaded} Episoden).");

            // Phase 1c: Cover von lokalen auf Online-Episoden kopieren (reines SQL)
            int copied = await CopyLocalToOnlineAsync();
            loaded += copied;
            _logger.Info($"RunOnce Phase 1b (lokal→online Kopie): {copied} Cover kopiert.");

            // Phase 2a: CoverImageUrl bei Online-Episoden nachtragen (Provider-API)
            int urlsUpdated = await UpdateMissingCoverUrlsAsync(ct);
            _logger.Info($"RunOnce Phase 2a (URL-Nachtrag): {urlsUpdated} URLs gesetzt.");

            // Phase 2b: Fehlende Cover über Provider-URLs herunterladen (Serien + Episoden)
            int seriesProviderLoaded = await DownloadMissingSeriesProviderCoversAsync(ct);
            int episodeProviderLoaded = await DownloadMissingEpisodeProviderCoversAsync(ct);
            loaded += seriesProviderLoaded + episodeProviderLoaded;
            _logger.Info($"RunOnce Phase 2b (Provider-URL Download): {seriesProviderLoaded + episodeProviderLoaded} Cover geladen " +
                $"({seriesProviderLoaded} Serien, {episodeProviderLoaded} Episoden).");

            return loaded;
        }

        /// <summary>
        /// Splash-Pfad: lädt ausschliesslich fehlende Serien-Cover (lokal + optional Provider-URL).
        /// Kein Episoden-Scan, kein ID3-Tag-Parsing, kein Provider-Call für Folgen.
        /// Provider-URL-Download wird übersprungen, wenn <paramref name="isOnlineAvailable"/>
        /// <see langword="false"/> ist (Offline-Modus oder fehlgeschlagener Konnektivitäts-Check).
        /// </summary>
        /// <param name="isOnlineAvailable">Steuert, ob der Provider-URL-Download laufen darf.</param>
        /// <param name="ct">Cancellation-Token des Splash-Pfades.</param>
        /// <returns>Anzahl der geladenen Serien-Cover.</returns>
        public virtual async Task<int> RunSeriesCoversOnceAsync(bool isOnlineAvailable, CancellationToken ct = default)
        {
            int loaded = 0;

            int localLoaded = await LoadMissingLocalSeriesCoversAsync(ct);
            loaded += localLoaded;
            _logger.Info($"SplashCoverPhase Serien lokal: {localLoaded} Cover geladen.");

            if (isOnlineAvailable)
            {
                int providerLoaded = await DownloadMissingSeriesProviderCoversAsync(ct);
                loaded += providerLoaded;
                _logger.Info($"SplashCoverPhase Serien Provider: {providerLoaded} Cover geladen.");
            }
            else
            {
                _logger.Info("SplashCoverPhase Serien Provider: übersprungen (offline).");
            }

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
                    int seriesLocalLoaded = await LoadMissingLocalSeriesCoversAsync(ct);
                    int episodeLocalLoaded = await LoadMissingLocalEpisodeCoversAsync(ct);
                    int copied = await CopyLocalToOnlineAsync();
                    int urlsUpdated = await UpdateMissingCoverUrlsAsync(ct);
                    int seriesProviderLoaded = await DownloadMissingSeriesProviderCoversAsync(ct);
                    int episodeProviderLoaded = await DownloadMissingEpisodeProviderCoversAsync(ct);

                    int localLoaded = seriesLocalLoaded + episodeLocalLoaded;
                    int providerLoaded = seriesProviderLoaded + episodeProviderLoaded;
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
        /// Lädt die Cover für die angegebenen Episoden (sofern fehlend) priorisiert im Hintergrund nach.
        /// Wird vom Dashboard nach dem ersten Rendern aufgerufen, damit Kacheln mit Serien-Cover-Fallback
        /// das spezifische Folgen-Cover progressiv nachbekommen. Kein Online-Such-Chain, nur:
        /// 1) vorhandene Bytes aus CoverImages → direkter Callback,
        /// 2) Dateisystem-Cover via <see cref="ILocalCoverLoader"/> (cover.jpg / ID3-Tag),
        /// 3) Provider-URL-Download (falls <see cref="Episode.CoverImageUrl"/> gesetzt).
        /// Nach jedem erfolgreichen Fund wird das Cover in CoverImages persistiert und der
        /// Callback mit den Rohdaten (nicht mit <c>BitmapImage</c>, da auf Hintergrund-Thread)
        /// aufgerufen.
        /// </summary>
        /// <param name="episodeIds">Zu prüfende Episoden – Duplikate sind erlaubt, werden entfernt.</param>
        /// <param name="onCoverReady">Callback pro Episode, die ein Cover bekommen hat. Darf <see langword="null"/> sein.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Cover-Queue: HTTP-/IO-/TagLib-Fehler einzelner Episoden duerfen die Queue fuer die anderen Kacheln nicht beenden; der Fehler wird als Debug geloggt und die naechste Episode wird verarbeitet.")]
        public void EnqueueForEpisodes(IReadOnlyList<Guid> episodeIds, Action<Guid, byte[]>? onCoverReady)
        {
            ArgumentNullException.ThrowIfNull(episodeIds);
            if (episodeIds.Count == 0) return;

            List<Guid> uniqueIds = [.. episodeIds.Distinct()];

            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessEnqueuedEpisodesAsync(uniqueIds, onCoverReady);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"EnqueueForEpisodes fehlgeschlagen: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Arbeitet die Queue sequentiell ab: erst DB-Treffer, dann Dateisystem, dann Provider-URL.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Pro-Episode-Schleife in der Cover-Queue: TagLib-, IO- oder HTTP-Fehler einer Episode werden als Debug protokolliert und die Queue faehrt mit der naechsten Episode fort, damit eine kaputte Datei nicht die ganze Kachelzeile blockiert.")]
        private async Task ProcessEnqueuedEpisodesAsync(IReadOnlyList<Guid> episodeIds, Action<Guid, byte[]>? onCoverReady)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
            ILocalCoverLoader coverLoader = scope.ServiceProvider.GetRequiredService<ILocalCoverLoader>();

            // Batch 1: bereits vorhandene Cover aus der DB (eine Abfrage)
            IReadOnlyDictionary<Guid, byte[]> existing =
                await _coverService.GetEpisodeCoverBytesAsync(episodeIds);

            // Vorhandene Cover sofort zurückspielen – UI kann sich aktualisieren
            foreach ((Guid episodeId, byte[] bytes) in existing)
            {
                onCoverReady?.Invoke(episodeId, bytes);
            }

            // Nur noch die fehlenden IDs weiterverarbeiten
            List<Guid> missing = [.. episodeIds.Where(id => !existing.ContainsKey(id))];
            if (missing.Count == 0) return;

            // Batch 2: erste Tracks der fehlenden Episoden (für ID3-Fallback)
            IReadOnlyDictionary<Guid, LocalTrack> firstTracks =
                await trackService.GetFirstTracksByEpisodeIdsAsync(missing);

            foreach (Guid episodeId in missing)
            {
                Episode? episode = await episodeService.GetByIdAsync(episodeId);
                if (episode is null) continue;

                byte[]? loaded = null;

                if (!string.IsNullOrEmpty(episode.LocalFolderPath))
                {
                    string? firstTrackPath = firstTracks.TryGetValue(episodeId, out LocalTrack? t) ? t.FilePath : null;
                    try
                    {
                        loaded = await coverLoader.LoadAsync(episode.LocalFolderPath, firstTrackPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"EnqueueForEpisodes Lokal-Cover fehlgeschlagen für \"{episode.Title}\": {ex.Message}");
                    }
                }

                if (loaded is null && !string.IsNullOrEmpty(episode.CoverImageUrl))
                {
                    loaded = await DownloadSafeAsync(episode.CoverImageUrl);
                }

                if (loaded is not null)
                {
                    await _coverService.SetEpisodeCoverAsync(episodeId, loaded, episode.CoverImageUrl);
                    onCoverReady?.Invoke(episodeId, loaded);
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
                        await episodeSource.GetEpisodesAsync(sourceSeriesId, ct);

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
        /// Sucht Serien mit lokalem Ordner aber ohne Cover in CoverImages
        /// und lädt <c>cover.jpg</c> aus dem Stammordner. ID3-Fallback entfällt bewusst,
        /// weil Serien-Cover nur als Dateien im Stammordner existieren.
        /// </summary>
        private async Task<int> LoadMissingLocalSeriesCoversAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            ILocalCoverLoader coverLoader = scope.ServiceProvider
                .GetRequiredService<ILocalCoverLoader>();
            ICoverImageDataService coverImageService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();

            _logger.Info($"Lokal-Check Serien: {allSeries.Count} Serien gesamt.");

            List<Series> seriesWithFolder = [];
            foreach (Series series in allSeries)
            {
                if (!string.IsNullOrEmpty(series.LocalFolderPath))
                {
                    seriesWithFolder.Add(series);
                }
            }

            if (seriesWithFolder.Count == 0)
            {
                return 0;
            }

            List<Guid> seriesIds = seriesWithFolder.Select(s => s.Id).ToList();
            IReadOnlyDictionary<Guid, byte[]> existingSeries =
                await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Series, seriesIds);

            int missingSeries = seriesWithFolder.Count - existingSeries.Count;
            _logger.Info($"Lokal-Check Serien: {seriesWithFolder.Count} mit LocalFolderPath, " +
                $"{existingSeries.Count} in DB, {missingSeries} fehlen.");

            int loaded = 0;

            foreach (Series series in seriesWithFolder)
            {
                if (ct.IsCancellationRequested) break;
                if (existingSeries.ContainsKey(series.Id)) continue;

                // firstTrackPath bewusst null – für Serien-Cover kein ID3-Fallback.
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

            return loaded;
        }

        /// <summary>
        /// Sucht Episoden mit lokalem Ordner aber ohne Cover in CoverImages und lädt die
        /// Cover aus dem Dateisystem (cover.jpg / ID3-Tags des ersten Tracks).
        /// Nutzt Batch-Queries, um N+1-DB-Roundtrips zu vermeiden.
        /// </summary>
        private async Task<int> LoadMissingLocalEpisodeCoversAsync(CancellationToken ct)
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

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();
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
                return 0;
            }

            List<Guid> candidateIds = [.. candidates.Select(e => e.Id)];
            IReadOnlyDictionary<Guid, byte[]> existing =
                await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, candidateIds);

            List<Guid> missingIds = [.. candidates
                .Where(e => !existing.ContainsKey(e.Id))
                .Select(e => e.Id)];

            IReadOnlyDictionary<Guid, LocalTrack> firstTracks =
                await trackService.GetFirstTracksByEpisodeIdsAsync(missingIds);

            int loaded = 0;
            int notFound = 0;

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
                }
                else
                {
                    notFound++;
                }
            }

            _logger.Info($"Lokal-Check Episoden: {candidates.Count} mit Ordner, " +
                $"{existing.Count} in DB, {loaded} geladen, {notFound} ohne Cover-Datei.");

            return loaded;
        }

        /// <summary>
        /// Lädt fehlende Serien-Cover über Provider-URLs (<see cref="Series.CoverImageUrl"/>)
        /// herunter. Kein Online-Suchkette – nur direkte URL-Downloads.
        /// </summary>
        private async Task<int> DownloadMissingSeriesProviderCoversAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            ICoverImageDataService coverImageService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();
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
                _logger.Info($"Provider-Check Serien: {allSeries.Count} Serien, keine mit CoverImageUrl.");
                return 0;
            }

            List<Guid> seriesIds = seriesNeedingCover.Select(s => s.Id).ToList();
            IReadOnlyDictionary<Guid, byte[]> existingSeries =
                await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Series, seriesIds);

            int missingSeriesCount = seriesNeedingCover.Count - existingSeries.Count;
            _logger.Info($"Provider-Check Serien: {seriesNeedingCover.Count} mit CoverImageUrl, " +
                $"{existingSeries.Count} bereits in DB, {missingSeriesCount} fehlen.");

            int loaded = 0;

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

            return loaded;
        }

        /// <summary>
        /// Lädt fehlende Episoden-Cover über Provider-URLs (<see cref="Episode.CoverImageUrl"/>)
        /// herunter. Kein Online-Suchkette – nur direkte URL-Downloads.
        /// </summary>
        private async Task<int> DownloadMissingEpisodeProviderCoversAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider
                .GetRequiredService<IEpisodeDataService>();
            ICoverImageDataService coverImageService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();
            int loaded = 0;
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
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Gibt die CancellationTokenSource des Hintergrund-Loops frei. Abgeleitete Typen
        /// können überschreiben, dürfen aber den Cleanup-Pfad der Basis (<c>base.Dispose(disposing)</c>)
        /// nicht auslassen.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> bei deterministischem Dispose; <see langword="false"/> beim Finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}

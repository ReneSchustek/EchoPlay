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
        private readonly IHostRateLimiter? _rateLimiter;
        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;
        private int _priorityInFlight;

        // Polling-Intervall, in dem der Hintergrund-Loop zwischen zwei Phasen prüft,
        // ob eine Foreground-Priority-Anfrage läuft. Klein genug, damit die sichtbare
        // UI zügig das HTTP- und Dateisystem-Kontingent übernimmt; groß genug, dass
        // der Thread-Pool keinen Spin aufbaut.
        private static readonly TimeSpan PriorityPollInterval = TimeSpan.FromMilliseconds(50);

        // Obergrenze der parallelen lokalen Cover-Loads im Foreground-Pfad. Nur Dateisystem,
        // kein externes Netz – vier Worker nutzen handelsübliche SSDs aus, ohne die Platte
        // zu saturieren.
        private const int ForegroundLocalParallelism = 4;


        /// <summary>
        /// Initialisiert den Background-Cover-Service.
        /// </summary>

        public BackgroundCoverService(
            IServiceScopeFactory scopeFactory,
            CoverService coverService,
            IHttpClientFactory httpClientFactory,
            ISpotifyCredentialStore credentialStore,
            BackgroundCoverServiceOptions options,
            ILoggerFactory loggerFactory,
            IHostRateLimiter? rateLimiter = null)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _coverService = coverService;
            _httpClientFactory = httpClientFactory;
            _credentialStore = credentialStore;
            _options = options;
            _logger = loggerFactory.CreateLogger("BackgroundCoverService");
            _rateLimiter = rateLimiter;
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
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (_cts is null || _backgroundTask is null) return;

            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await _backgroundTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
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
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        public virtual async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            // Eigene CTS, die mit dem externen Token verkettet ist — Aufrufer kann den Lauf
            // abbrechen, ohne dass der interne Loop seinen eigenen Schutz verliert.
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
            int copied = await CopyLocalToOnlineAsync(cancellationToken);
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
        /// Splash-Pfad: lädt ausschließlich fehlende Serien-Cover (lokal + optional Provider-URL).
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Scan-Schleife: TagLib-, DB-, HTTP- oder IO-Fehler einer einzelnen Iteration dürfen die Cover-Schleife nicht beenden; Fehler werden als Warning geloggt und die nächste Iteration fährt fort.")]
        private async Task RunAsync(CancellationToken ct)
        {
            // Kurz warten, damit die App vollständig initialisiert ist
            await Task.Delay(_options.InitialDelay, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await WaitWhilePriorityInFlightAsync(ct).ConfigureAwait(false);
                    int seriesLocalLoaded = await LoadMissingLocalSeriesCoversAsync(ct);
                    int episodeLocalLoaded = await LoadMissingLocalEpisodeCoversAsync(ct);
                    int copied = await CopyLocalToOnlineAsync(ct);
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
        /// <param name="priority">
        /// Priorität der Anfrage. <see cref="CoverFetchPriority.Foreground"/> läuft parallel
        /// zum laufenden Hintergrund-Scan, markiert den Service aber als "Priority aktiv",
        /// sodass die nächste Loop-Iteration pausiert, bis die Queue abgearbeitet ist.
        /// </param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Cover-Queue: HTTP-/IO-/TagLib-Fehler einzelner Episoden dürfen die Queue für die anderen Kacheln nicht beenden; der Fehler wird als Debug geloggt und die nächste Episode wird verarbeitet.")]
        public void EnqueueForEpisodes(
            IReadOnlyList<Guid> episodeIds,
            Action<Guid, byte[]>? onCoverReady,
            CoverFetchPriority priority = CoverFetchPriority.Background)
        {
            ArgumentNullException.ThrowIfNull(episodeIds);
            if (episodeIds.Count == 0) return;

            List<Guid> uniqueIds = [.. episodeIds.Distinct()];

            _ = Task.Run(async () =>
            {
                if (priority == CoverFetchPriority.Foreground)
                {
                    _ = Interlocked.Increment(ref _priorityInFlight);
                }

                try
                {
                    await ProcessEnqueuedEpisodesAsync(uniqueIds, onCoverReady);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"EnqueueForEpisodes fehlgeschlagen: {ex.Message}");
                }
                finally
                {
                    if (priority == CoverFetchPriority.Foreground)
                    {
                        _ = Interlocked.Decrement(ref _priorityInFlight);
                    }
                }
            });
        }

        /// <summary>
        /// Arbeitet die Queue sequentiell ab: erst DB-Treffer, dann Dateisystem, dann Provider-URL.
        /// </summary>
        /// <param name="episodeIds">IDs der Episoden, fuer die ein Cover nachgeladen werden soll.</param>
        /// <param name="onCoverReady">Callback fuer jedes gefundene Cover (EpisodenId + Bytes).</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Pro-Episode-Schleife in der Cover-Queue: TagLib-, IO- oder HTTP-Fehler einer Episode werden als Debug protokolliert und die Queue fährt mit der nächsten Episode fort, damit eine kaputte Datei nicht die ganze Kachelzeile blockiert.")]
        private async Task ProcessEnqueuedEpisodesAsync(IReadOnlyList<Guid> episodeIds, Action<Guid, byte[]>? onCoverReady, CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
            ILocalCoverLoader coverLoader = scope.ServiceProvider.GetRequiredService<ILocalCoverLoader>();

            // Batch 1: bereits vorhandene Cover aus der DB (eine Abfrage)
            IReadOnlyDictionary<Guid, byte[]> existing =
                await _coverService.GetEpisodeCoverBytesAsync(episodeIds, cancellationToken);

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
                await trackService.GetFirstTracksByEpisodeIdsAsync(missing, cancellationToken);

            foreach (Guid episodeId in missing)
            {
                Episode? episode = await episodeService.GetByIdAsync(episodeId, cancellationToken);
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
                        _logger.Debug(() => $"EnqueueForEpisodes Lokal-Cover fehlgeschlagen für \"{episode.Title}\": {ex.Message}");
                    }
                }

                if (loaded is null && !string.IsNullOrEmpty(episode.CoverImageUrl))
                {
                    loaded = await DownloadSafeAsync(episode.CoverImageUrl, cancellationToken);
                }

                if (loaded is not null)
                {
                    await _coverService.SetEpisodeCoverAsync(episodeId, loaded, episode.CoverImageUrl, cancellationToken);
                    onCoverReady?.Invoke(episodeId, loaded);
                }
            }
        }

        /// <summary>
        /// Priorisiert das Laden der Folgen-Cover für die angegebene Serie. Markiert
        /// den Service als "Foreground aktiv", sodass der Hintergrund-Loop zwischen
        /// zwei Phasen pausiert, lädt fehlende Episoden-Cover zunächst lokal
        /// (<see cref="ILocalCoverLoader"/>, parallelisiert) und danach über die
        /// Provider-URL. Keine Online-Suchkette – die bleibt dem langsamen Hintergrund-Loop
        /// vorbehalten. Wird die Priorität abgebrochen (Nutzer verlässt die Detailseite),
        /// endet die Methode ohne Exception.
        /// </summary>
        /// <param name="seriesId">Serie, deren Folgen-Cover priorisiert geladen werden.</param>
        /// <param name="ct">Abbruch-Token der aufrufenden Detail-Ansicht.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Foreground-Priority-Pfad: Einzelne TagLib-/IO-/HTTP-Fehler pro Episode werden geloggt, damit das Priorisierungs-Fenster für die sichtbare Serie nicht wegen einer kaputten Datei abbricht.")]
        public virtual async Task RequestPriorityForSeriesAsync(Guid seriesId, CancellationToken ct = default)
        {
            if (seriesId == Guid.Empty) return;

            _ = Interlocked.Increment(ref _priorityInFlight);

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
                ILocalCoverLoader coverLoader = scope.ServiceProvider.GetRequiredService<ILocalCoverLoader>();
                ICoverImageDataService coverImageService = scope.ServiceProvider
                    .GetRequiredService<ICoverImageDataService>();

                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(seriesId, ct);
                if (episodes.Count == 0) return;

                List<Guid> episodeIds = [.. episodes.Select(e => e.Id)];
                IReadOnlyDictionary<Guid, byte[]> existing =
                    await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, episodeIds, ct);

                List<Episode> missing = [.. episodes.Where(e => !existing.ContainsKey(e.Id))];
                if (missing.Count == 0) return;

                List<Guid> missingIds = [.. missing.Select(e => e.Id)];
                IReadOnlyDictionary<Guid, LocalTrack> firstTracks =
                    await trackService.GetFirstTracksByEpisodeIdsAsync(missingIds, ct);

                _logger.Info($"Priority SeriesOpen: starte {missing.Count} Folgen-Cover für Serie {seriesId}.");

                ParallelOptions parallelOptions = new()
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = ForegroundLocalParallelism
                };

                // Phase 1 (Foreground): lokale Quellen parallel. Keine externen HTTP-Calls
                // – reines Dateisystem, daher Parallelismus sicher.
                await Parallel.ForEachAsync(missing, parallelOptions, async (episode, token) =>
                {
                    if (string.IsNullOrEmpty(episode.LocalFolderPath)) return;

                    string? firstTrackPath = firstTracks.TryGetValue(episode.Id, out LocalTrack? t) ? t.FilePath : null;
                    try
                    {
                        byte[]? bytes = await coverLoader.LoadAsync(episode.LocalFolderPath, firstTrackPath);
                        if (bytes is not null)
                        {
                            await _coverService.SetEpisodeCoverAsync(episode.Id, bytes, cancellationToken: token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(() => $"Priority Lokal-Cover fehlgeschlagen für \"{episode.Title}\": {ex.Message}");
                    }
                }).ConfigureAwait(false);

                // Phase 2 (Foreground): Provider-URLs. HTTP, daher seriell, damit der
                // Rate-Limiter nicht gesprengt wird; der Foreground-Slot ueberholt
                // Background-Waits via IHostRateLimiter automatisch.
                IReadOnlyDictionary<Guid, byte[]> stillMissing =
                    await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, missingIds, ct);

                foreach (Episode episode in missing)
                {
                    ct.ThrowIfCancellationRequested();
                    if (stillMissing.ContainsKey(episode.Id)) continue;
                    if (string.IsNullOrEmpty(episode.CoverImageUrl)) continue;

                    try
                    {
                        byte[]? bytes = await DownloadSafeAsync(episode.CoverImageUrl, ct);
                        if (bytes is not null)
                        {
                            await _coverService.SetEpisodeCoverAsync(episode.Id, bytes, episode.CoverImageUrl, ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(() => $"Priority Provider-Cover fehlgeschlagen für \"{episode.Title}\": {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Erwartet: Nutzer hat die Detailseite verlassen. Kein Log-Rauschen.
            }
            finally
            {
                _ = Interlocked.Decrement(ref _priorityInFlight);
            }
        }

        /// <summary>
        /// Pausiert den Hintergrund-Loop, solange eine Foreground-Priorität läuft. Liest
        /// den Counter atomar und wartet in kleinen Ticks; bei Cancel gibt die Methode
        /// die <see cref="OperationCanceledException"/> weiter, die die Run-Schleife beendet.
        /// </summary>

        /// <param name="ct">Parameter <c>ct</c>.</param>
        private async Task WaitWhilePriorityInFlightAsync(CancellationToken ct)
        {
            while (Volatile.Read(ref _priorityInFlight) > 0)
            {
                await Task.Delay(PriorityPollInterval, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Gibt an, ob aktuell eine Foreground-Priority-Anfrage verarbeitet wird.
        /// Für Tests und Telemetry.
        /// </summary>
        public bool IsPriorityActive => Volatile.Read(ref _priorityInFlight) > 0;

        /// <summary>
        /// Stellt sicher, dass alle lokalen Episoden einer Serie (nach Titel) ihre Cover
        /// in CoverImages haben. Wird synchron vor der Anzeige aufgerufen, damit der
        /// CoverCopyService danach Quellen findet.
        /// </summary>
        /// <param name="seriesTitle">Titel der Serie (z.B. "Fünf Freunde").</param>
        /// <returns>Anzahl der neu geladenen Cover.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        public async Task<int> EnsureLocalCoversForSeriesAsync(string seriesTitle, CancellationToken cancellationToken = default)
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
            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync(cancellationToken);
            int loaded = 0;

            foreach (Series series in allSeries)
            {
                if (!string.Equals(series.Title, seriesTitle, StringComparison.OrdinalIgnoreCase))
                    continue;

                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id, cancellationToken);

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
                    await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, candidateIds, cancellationToken);

                foreach (Episode episode in candidates)
                {
                    if (existing.ContainsKey(episode.Id)) continue;

                    string? firstTrackPath = null;
                    IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episode.Id, cancellationToken);

                    if (tracks.Count > 0)
                    {
                        firstTrackPath = tracks[0].FilePath;
                    }

                    byte[]? coverBytes = await coverLoader.LoadAsync(
                        episode.LocalFolderPath, firstTrackPath);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes, cancellationToken: cancellationToken);
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
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        public async Task<int> CopyLocalToOnlineAsync(CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            ICoverCopyService coverCopy = scope.ServiceProvider
                .GetRequiredService<ICoverCopyService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync(cancellationToken);
            int totalCopied = 0;

            foreach (Series series in allSeries)
            {
                if (!series.IsOnlineImported) continue;

                int copied = await coverCopy.CopyFromMatchingEpisodesAsync(series.Id, cancellationToken);
                totalCopied += copied;
            }

            return totalCopied;
        }

        /// <summary>
        /// Fragt die Provider-API (Spotify/Apple Music) für Online-Serien ab und trägt
        /// fehlende <see cref="Episode.CoverImageUrl"/> auf bestehenden Episoden nach.
        /// Überspringt Serien bei denen alle Episoden bereits eine URL haben.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "URL-Nachtrag pro Serie: HTTP- oder API-Fehler (Spotify/AppleMusic) einer Serie dürfen den Batch für die restlichen Serien nicht abbrechen; Einzelfehler werden als Warning geloggt.")]
        private async Task<int> UpdateMissingCoverUrlsAsync(CancellationToken ct)
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
                            await episodeService.UpdateAsync(episode, ct);
                            totalUpdated++;
                        }
                    }

                    if (totalUpdated > 0)
                    {
                        _logger.Debug(() => $"URL-Nachtrag \"{series.Title}\": {missingUrl.Count} geprüft, URLs gesetzt.");
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

        /// <param name="ct">Parameter <c>ct</c>.</param>
        private async Task<int> LoadMissingLocalSeriesCoversAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            ILocalCoverLoader coverLoader = scope.ServiceProvider
                .GetRequiredService<ILocalCoverLoader>();
            ICoverImageDataService coverImageService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync(ct);

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
                await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Series, seriesIds, ct);

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
                    await _coverService.SetSeriesCoverAsync(series.Id, coverBytes, cancellationToken: ct);
                    loaded++;
                    _logger.Debug(() => $"Lokal: Serien-Cover geladen \"{series.Title}\" aus {series.LocalFolderPath}");
                }
                else
                {
                    _logger.Debug(() => $"Lokal: Kein Cover gefunden für \"{series.Title}\" in {series.LocalFolderPath}");
                }
            }

            return loaded;
        }

        /// <summary>
        /// Sucht Episoden mit lokalem Ordner aber ohne Cover in CoverImages und lädt die
        /// Cover aus dem Dateisystem (cover.jpg / ID3-Tags des ersten Tracks).
        /// Nutzt Batch-Queries, um N+1-DB-Roundtrips zu vermeiden.
        /// </summary>

        /// <param name="ct">Parameter <c>ct</c>.</param>
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

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync(ct);
            List<Guid> allSeriesIds = [.. allSeries.Select(s => s.Id)];

            IReadOnlyList<Episode> allEpisodes = await episodeService.GetBySeriesIdsAsync(allSeriesIds, ct);

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
                await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, candidateIds, ct);

            List<Guid> missingIds = [.. candidates
                .Where(e => !existing.ContainsKey(e.Id))
                .Select(e => e.Id)];

            IReadOnlyDictionary<Guid, LocalTrack> firstTracks =
                await trackService.GetFirstTracksByEpisodeIdsAsync(missingIds, ct);

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
                    await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes, cancellationToken: ct);
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

        /// <param name="ct">Parameter <c>ct</c>.</param>
        private async Task<int> DownloadMissingSeriesProviderCoversAsync(CancellationToken ct)
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
                _logger.Info($"Provider-Check Serien: {allSeries.Count} Serien, keine mit CoverImageUrl.");
                return 0;
            }

            List<Guid> seriesIds = seriesNeedingCover.Select(s => s.Id).ToList();
            IReadOnlyDictionary<Guid, byte[]> existingSeries =
                await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Series, seriesIds, ct);

            int missingSeriesCount = seriesNeedingCover.Count - existingSeries.Count;
            _logger.Info($"Provider-Check Serien: {seriesNeedingCover.Count} mit CoverImageUrl, " +
                $"{existingSeries.Count} bereits in DB, {missingSeriesCount} fehlen.");

            int loaded = 0;

            foreach (Series series in seriesNeedingCover)
            {
                if (ct.IsCancellationRequested) break;
                if (existingSeries.ContainsKey(series.Id)) continue;

                byte[]? coverBytes = await DownloadSafeAsync(series.CoverImageUrl!, cancellationToken: ct);

                if (coverBytes is not null)
                {
                    await _coverService.SetSeriesCoverAsync(series.Id, coverBytes, series.CoverImageUrl, ct);
                    loaded++;
                    _logger.Debug(() => $"Serien-Cover geladen: \"{series.Title}\" ({coverBytes.Length} Bytes)");
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

        /// <param name="ct">Parameter <c>ct</c>.</param>
        private async Task<int> DownloadMissingEpisodeProviderCoversAsync(CancellationToken ct)
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

                    byte[]? coverBytes = await DownloadSafeAsync(episode.CoverImageUrl!, cancellationToken: ct);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes, episode.CoverImageUrl, ct);
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
        /// <param name="url">Absolute Cover-URL.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Download-Wrapper: HTTP-, Timeout-, TLS- oder Redirect-Fehler beim Laden einzelner Cover-URLs werden alle zu 'null' normalisiert, damit der Aufrufer die Episode ueberspringen und mit anderen weitermachen kann.")]
        private async Task<byte[]?> DownloadSafeAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                HttpClient client = _httpClientFactory.CreateClient("CoverDownload");
                byte[] data = await client.GetByteArrayAsync(new Uri(url, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
                return data.Length > 0 ? data : null;
            }
            catch (Exception ex)
            {
                _logger.Debug(() => $"Cover-Download fehlgeschlagen: {ex.Message} URL={url}");
                return null;
            }
        }

        /// <summary>
        /// Lädt das Cover für ein Such-Treffer-Element. Erst DB-First (falls die Serie
        /// bereits in der lokalen Bibliothek existiert und dort ein Cover hinterlegt ist),
        /// danach Provider-URL über <see cref="IHostRateLimiter"/> mit
        /// <see cref="CoverFetchPriority.Foreground"/>. Markiert den Service als
        /// "Foreground aktiv", sodass der Hintergrund-Loop pausiert. Persistiert das
        /// Cover **nicht** in <c>CoverImages</c> – Such-Treffer sind noch nicht importiert.
        /// </summary>
        /// <param name="source">Provider-Schlüssel aus <see cref="ProviderKeys"/>. Andere Werte verhindern den DB-Lookup.</param>
        /// <param name="sourceSeriesId">Provider-spezifische Serien-ID (Spotify-Artist-ID oder iTunes-Artist-ID).</param>
        /// <param name="coverUrl">Cover-URL aus dem Such-Treffer.</param>
        /// <param name="ct">Abbruch-Token der laufenden Suche.</param>
        /// <returns>Cover-Bytes oder <see langword="null"/> bei Fehler/Abbruch ohne Daten.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "Cover-URL stammt aus DTO der externen Provider-API und wird in der gesamten Cover-Pipeline als string verwaltet (gleiches Muster wie DownloadSafeAsync).")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Such-Treffer-Cover: HTTP-/Timeout-/TLS-Fehler einer einzelnen Provider-URL werden zu null normalisiert, damit die Trefferkachel den Platzhalter behält und die anderen Treffer weiterlaufen.")]
        public virtual async Task<byte[]?> RequestCoverForSearchResultAsync(
            string source, string sourceSeriesId, string coverUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(coverUrl)) return null;

            byte[]? cached = await TryGetCachedSeriesCoverAsync(source, sourceSeriesId, ct).ConfigureAwait(false);
            if (cached is not null) return cached;

            if (!Uri.TryCreate(coverUrl, UriKind.Absolute, out Uri? uri)) return null;

            _ = Interlocked.Increment(ref _priorityInFlight);
            try
            {
                if (_rateLimiter is not null)
                {
                    await _rateLimiter.WaitAsync(uri.Host, CoverFetchPriority.Foreground, ct).ConfigureAwait(false);
                }

                HttpClient client = _httpClientFactory.CreateClient("CoverDownload");
                byte[] data = await client.GetByteArrayAsync(uri, ct).ConfigureAwait(false);
                return data.Length > 0 ? data : null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Debug(() => $"Such-Treffer-Cover-Download fehlgeschlagen: {ex.Message} URL={coverUrl}");
                return null;
            }
            finally
            {
                _ = Interlocked.Decrement(ref _priorityInFlight);
            }
        }

        /// <summary>
        /// Findet eine bereits importierte Serie über ihre Provider-Quell-ID und liefert
        /// deren persistiertes Cover aus <c>CoverImages</c>. Liefert <see langword="null"/>,
        /// wenn die Serie noch nicht importiert ist oder die Quelle unbekannt ist.
        /// </summary>

        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="source">Parameter <c>source</c>.</param>
        /// <param name="sourceSeriesId">Parameter <c>sourceSeriesId</c>.</param>
        private async Task<byte[]?> TryGetCachedSeriesCoverAsync(string source, string sourceSeriesId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(sourceSeriesId)) return null;

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            ICoverImageDataService coverImageService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            Series? series = source switch
            {
                ProviderKeys.Spotify => await seriesService.GetBySpotifyArtistIdAsync(sourceSeriesId, cancellationToken).ConfigureAwait(false),
                ProviderKeys.AppleMusic => await seriesService.GetByAppleMusicArtistIdAsync(sourceSeriesId, cancellationToken).ConfigureAwait(false),
                _ => null
            };

            if (series is null) return null;

            CoverImage? cover = await coverImageService
                .GetByEntityAsync(CoverEntityTypes.Series, series.Id, cancellationToken)
                .ConfigureAwait(false);

            return cover?.ImageData is { Length: > 0 } bytes ? bytes : null;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Gibt die CancellationTokenSource und den laufenden Hintergrund-Task frei.
        /// Wartet kurz auf das Ende der aktuellen Iteration, damit kein Service-Scope
        /// als Closure im Task-State-Machine hängen bleibt. Abgeleitete Typen können
        /// überschreiben, dürfen aber den Cleanup-Pfad der Basis
        /// (<c>base.Dispose(disposing)</c>) nicht auslassen.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> bei deterministischem Dispose; <see langword="false"/> beim Finalizer.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Dispose-Pfad: AggregateException/ObjectDisposedException aus dem abgebrochenen Hintergrund-Task dürfen den Shutdown nicht zerlegen, weil der Service als Singleton meist im App-Exit disposed wird.")]
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            try { _cts?.Cancel(); }
            catch (ObjectDisposedException)
            {
                // Race im App-Exit: CTS wurde parallel bereits disposed – Cancel ist dann ein No-Op.
            }

            // 2 s sind ein Kompromiss: Task.Delay-Iterationen schlafen 30 min,
            // ein laufendes RunOnceAsync braucht selten so lange — länger warten würde
            // den App-Exit blockieren.
            try { _ = _backgroundTask?.Wait(TimeSpan.FromSeconds(2)); }
            catch (Exception)
            {
                // AggregateException (Cancel) oder Timeout im Shutdown sind erwartet – Dispose darf hier nicht werfen.
            }

            _cts?.Dispose();
            _cts = null;
            _backgroundTask = null;
        }
    }
}

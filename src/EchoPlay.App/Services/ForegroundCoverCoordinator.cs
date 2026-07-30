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
    /// Alles, was am Hintergrundlauf vorbei darf, weil der Nutzer davor sitzt: die Queue des
    /// Dashboards, der Vorrang beim Öffnen einer Serie und die Cover der Such-Treffer.
    /// Führt den Zähler, an dem der Hintergrundlauf erkennt, dass er pausieren soll.
    /// </summary>
    /// <remarks>
    /// Der Zähler ist der ganze Grund für diese Klasse. Er wird von mehreren Threads erhöht
    /// und gesenkt (Queue-Task, Detailseite, Suchtreffer) und vom Hintergrundlauf gelesen -
    /// deshalb <see cref="Interlocked"/> und <see cref="Volatile"/>, und deshalb liegt er an
    /// genau einer Stelle statt verteilt über die drei Einstiegspunkte.
    /// </remarks>
    internal sealed class ForegroundCoverCoordinator
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ICoverService _coverService;
        private readonly ICoverDownloader _coverDownloader;
        private readonly IHostRateLimiter? _rateLimiter;
        private readonly ILogger _logger;
        private int _priorityInFlight;

        // Polling-Intervall, in dem der Hintergrund-Loop zwischen zwei Phasen prüft,
        // ob eine Foreground-Priority-Anfrage läuft. Klein genug, damit die sichtbare
        // UI zügig das HTTP- und Dateisystem-Kontingent übernimmt; groß genug, dass
        // der Thread-Pool keinen Spin aufbaut.
        private static readonly TimeSpan PriorityPollInterval = TimeSpan.FromMilliseconds(50);

        // Obergrenze der parallelen lokalen Cover-Loads im Foreground-Pfad. Nur Dateisystem,
        // kein externes Netz - vier Worker nutzen handelsübliche SSDs aus, ohne die Platte
        // zu saturieren.
        private const int ForegroundLocalParallelism = 4;

        /// <summary>
        /// Initialisiert den Vorrang-Koordinator. Der Logger kommt vom
        /// <see cref="BackgroundCoverService"/>, damit alle Meldungen unter derselben Quelle
        /// im Protokoll stehen.
        /// </summary>
        internal ForegroundCoverCoordinator(
            IServiceScopeFactory scopeFactory,
            ICoverService coverService,
            ICoverDownloader coverDownloader,
            IHostRateLimiter? rateLimiter,
            ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _coverService = coverService;
            _coverDownloader = coverDownloader;
            _rateLimiter = rateLimiter;
            _logger = logger;
        }
        /// <summary>
        /// Gibt an, ob aktuell eine Foreground-Priority-Anfrage verarbeitet wird.
        /// Für Tests und Telemetry.
        /// </summary>
        internal bool IsActive => Volatile.Read(ref _priorityInFlight) > 0;
        /// <summary>
        /// Pausiert den Hintergrund-Loop, solange eine Foreground-Priorität läuft. Liest
        /// den Counter atomar und wartet in kleinen Ticks; bei Cancel gibt die Methode
        /// die <see cref="OperationCanceledException"/> weiter, die die Run-Schleife beendet.
        /// </summary>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        internal async Task WaitWhileInFlightAsync(CancellationToken ct)
        {
            while (Volatile.Read(ref _priorityInFlight) > 0)
            {
                await Task.Delay(PriorityPollInterval, ct).ConfigureAwait(false);
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
        /// zum laufenden Hintergrund-Scan, markiert aber den Vorrang als aktiv,
        /// sodass die nächste Loop-Iteration pausiert, bis die Queue abgearbeitet ist.
        /// </param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Cover-Queue: HTTP-/IO-/TagLib-Fehler einzelner Episoden dürfen die Queue für die anderen Kacheln nicht beenden; der Fehler wird als Debug geloggt und die nächste Episode wird verarbeitet.")]
        internal void EnqueueForEpisodes(
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
                    _logger.Warning("EnqueueForEpisodes fehlgeschlagen: {Reason}", ex.Message);
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
        /// <param name="episodeIds">IDs der Episoden, für die ein Cover nachgeladen werden soll.</param>
        /// <param name="onCoverReady">Callback für jedes gefundene Cover (EpisodenId + Bytes).</param>
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
                    loaded = await _coverDownloader.DownloadAsync(episode.CoverImageUrl, cancellationToken);
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
        internal async Task RequestPriorityForSeriesAsync(Guid seriesId, CancellationToken ct = default)
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

                _logger.Info("Priority SeriesOpen: starte {EpisodeCount} Folgen-Cover für Serie {SeriesId}.", missing.Count, seriesId);

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
                // Rate-Limiter nicht gesprengt wird; der Foreground-Slot überholt
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
                        byte[]? bytes = await _coverDownloader.DownloadAsync(episode.CoverImageUrl, ct);
                        if (bytes is not null)
                        {
                            await _coverService.SetEpisodeCoverAsync(episode.Id, bytes, episode.CoverImageUrl, ct);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Gleiche Behandlung wie in Phase 1 darüber: Der Nutzer hat die
                        // Detailseite verlassen, das ist kein Fehlschlag dieser Folge.
                        throw;
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
        /// Lädt das Cover für ein Such-Treffer-Element. Erst DB-First (falls die Serie
        /// bereits in der lokalen Bibliothek existiert und dort ein Cover hinterlegt ist),
        /// danach Provider-URL über <see cref="IHostRateLimiter"/> mit
        /// <see cref="CoverFetchPriority.Foreground"/>. Markiert den Vorrang als
        /// "Foreground aktiv", sodass der Hintergrund-Loop pausiert. Persistiert das
        /// Cover **nicht** in <c>CoverImages</c> – Such-Treffer sind noch nicht importiert.
        /// </summary>
        /// <param name="source">Provider-Schlüssel aus <see cref="ProviderKeys"/>. Andere Werte verhindern den DB-Lookup.</param>
        /// <param name="sourceSeriesId">Provider-spezifische Serien-ID (Spotify-Artist-ID oder iTunes-Artist-ID).</param>
        /// <param name="coverUrl">Cover-URL aus dem Such-Treffer.</param>
        /// <param name="ct">Abbruch-Token der laufenden Suche.</param>
        /// <returns>Cover-Bytes oder <see langword="null"/> bei Fehler/Abbruch ohne Daten.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "Cover-URL stammt aus DTO der externen Provider-API und wird in der gesamten Cover-Pipeline als string verwaltet (gleiches Muster wie ICoverDownloader).")]
        internal async Task<byte[]?> RequestCoverForSearchResultAsync(
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

                return await _coverDownloader.DownloadAsync(coverUrl, ct).ConfigureAwait(false);
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
        /// <param name="source">Bezeichnung des Anbieters, z. B. <c>Spotify</c> oder <c>AppleMusic</c>.</param>
        /// <param name="sourceSeriesId">ID der Serie beim Anbieter.</param>
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
    }
}

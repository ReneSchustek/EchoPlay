using EchoPlay.Core.Http;
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

        // Polling-Intervall, in dem der Hintergrund-Loop vor jedem Durchgang prüft,
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
        /// Für Tests und Diagnose.
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
        /// das spezifische Folgen-Cover progressiv nachbekommen. Keine Online-Suchkette, nur:
        /// 1) vorhandene Bytes aus CoverImages → direkter Callback,
        /// 2) Dateisystem-Cover via <see cref="ILocalCoverLoader"/> (cover.jpg / ID3-Tag),
        /// 3) Provider-URL-Download (falls <see cref="Episode.CoverImageUrl"/> gesetzt).
        /// Nach jedem erfolgreichen Fund wird das Cover in CoverImages persistiert und der
        /// Callback mit den Rohdaten (nicht mit <c>BitmapImage</c>, da auf Hintergrund-Thread)
        /// aufgerufen.
        /// </summary>
        /// <param name="episodeIds">Zu prüfende Episoden. Duplikate sind erlaubt, werden entfernt.</param>
        /// <param name="onCoverReady">Callback pro Episode, die ein Cover bekommen hat. Darf <see langword="null"/> sein.</param>
        /// <param name="priority">
        /// Priorität der Anfrage. <see cref="CoverFetchPriority.Foreground"/> läuft parallel
        /// zum laufenden Hintergrund-Scan, markiert aber den Vorrang als aktiv,
        /// sodass die nächste Loop-Iteration pausiert, bis die Queue abgearbeitet ist.
        /// </param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Cover-Queue: Ein Fehler, der bis hierher durchschlägt, beendet nur diesen Auftrag und wird als Warnung geloggt; ohne den Fang ginge er als unbeobachtete Task-Ausnahme verloren.")]
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
        private async Task ProcessEnqueuedEpisodesAsync(IReadOnlyList<Guid> episodeIds, Action<Guid, byte[]>? onCoverReady, CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
            ILocalCoverLoader coverLoader = scope.ServiceProvider.GetRequiredService<ILocalCoverLoader>();

            // Batch 1: bereits vorhandene Cover aus der DB (eine Abfrage)
            IReadOnlyDictionary<Guid, byte[]> existing =
                await _coverService.GetEpisodeCoverBytesAsync(episodeIds, cancellationToken);

            // Vorhandene Cover sofort zurückspielen, damit die UI sich aktualisieren kann
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

                string? firstTrackPath = firstTracks.TryGetValue(episodeId, out LocalTrack? t) ? t.FilePath : null;
                byte[]? loaded = await LoadAndStoreCoverAsync(coverLoader, episode, firstTrackPath, cancellationToken);
                if (loaded is not null)
                {
                    onCoverReady?.Invoke(episodeId, loaded);
                }
            }
        }

        /// <summary>
        /// Lädt das Cover lokal, sonst von der Adresse beim Anbieter, und legt es in der Datenbank ab.
        /// </summary>
        /// <returns>Die Bilddaten oder <see langword="null"/>, wenn keine Quelle etwas lieferte.</returns>
        private async Task<byte[]?> LoadAndStoreCoverAsync(
            ILocalCoverLoader coverLoader,
            Episode episode,
            string? firstTrackPath,
            CancellationToken cancellationToken)
        {
            byte[]? loaded = await TryLoadLocalCoverAsync(coverLoader, episode, firstTrackPath);

            Uri? coverAddress = WebAddress.TryParse(episode.CoverImageUrl);
            if (loaded is null && coverAddress is not null)
            {
                loaded = await _coverDownloader.DownloadAsync(coverAddress, cancellationToken);
            }

            if (loaded is not null)
            {
                await _coverService.SetEpisodeCoverAsync(episode.Id, loaded, coverAddress, cancellationToken);
            }

            return loaded;
        }

        /// <summary>
        /// Das Cover aus dem Ordner der Folge oder aus dem ID3-Tag ihrer ersten Spur.
        /// </summary>
        /// <returns>Die Bilddaten, oder leer, wenn die Folge keinen Ordner hat oder nichts lesbar ist.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Pro-Episode-Schleife in der Cover-Queue: TagLib- oder IO-Fehler einer Episode werden als Debug protokolliert und die Queue fährt mit der nächsten Episode fort, damit eine kaputte Datei nicht die ganze Kachelzeile blockiert.")]
        private async Task<byte[]?> TryLoadLocalCoverAsync(ILocalCoverLoader coverLoader, Episode episode, string? firstTrackPath)
        {
            if (string.IsNullOrEmpty(episode.LocalFolderPath))
            {
                return null;
            }

            try
            {
                return await coverLoader.LoadAsync(episode.LocalFolderPath, firstTrackPath);
            }
            catch (Exception ex)
            {
                _logger.Debug(() => $"EnqueueForEpisodes Lokal-Cover fehlgeschlagen für \"{episode.Title}\": {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Priorisiert das Laden der Folgen-Cover für die angegebene Serie. Markiert
        /// den Service als "Foreground aktiv", sodass kein neuer Hintergrund-Durchgang
        /// beginnt, solange der Vorrang läuft; ein schon laufender Durchgang weicht nur bei
        /// seinen eigenen Netzabrufen aus. Lädt fehlende Episoden-Cover zunächst lokal
        /// (<see cref="ILocalCoverLoader"/>, parallelisiert) und danach über die
        /// Provider-URL. Keine Online-Suchkette; die bleibt dem langsamen Hintergrund-Loop
        /// vorbehalten. Wird die Priorität abgebrochen (Nutzer verlässt die Detailseite),
        /// endet die Methode ohne Exception.
        /// </summary>
        /// <param name="seriesId">Serie, deren Folgen-Cover priorisiert geladen werden.</param>
        /// <param name="ct">Abbruch-Token der aufrufenden Detail-Ansicht.</param>
        internal async Task RequestPriorityForSeriesAsync(Guid seriesId, CancellationToken ct = default)
        {
            if (seriesId == Guid.Empty) return;

            _ = Interlocked.Increment(ref _priorityInFlight);

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ICoverImageDataService coverImageService = scope.ServiceProvider.GetRequiredService<ICoverImageDataService>();
                List<Episode> missing = await FindEpisodesWithoutCoverAsync(scope.ServiceProvider, coverImageService, seriesId, ct);
                if (missing.Count == 0) return;

                _logger.Info("Priority SeriesOpen: starte {EpisodeCount} Folgen-Cover für Serie {SeriesId}.", missing.Count, seriesId);
                await LoadLocalCoversAsync(scope.ServiceProvider, missing, ct);
                await LoadProviderCoversAsync(coverImageService, missing, ct);
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

        /// <summary>Die Folgen der Serie, für die noch kein Cover gespeichert ist.</summary>
        private static async Task<List<Episode>> FindEpisodesWithoutCoverAsync(
            IServiceProvider services, ICoverImageDataService coverImageService, Guid seriesId, CancellationToken ct)
        {
            IReadOnlyList<Episode> episodes = await services.GetRequiredService<IEpisodeDataService>().GetBySeriesIdAsync(seriesId, ct);
            if (episodes.Count == 0) return [];

            IReadOnlyDictionary<Guid, byte[]> existing =
                await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, episodes.Select(e => e.Id).ToArray(), ct);
            return [.. episodes.Where(e => !existing.ContainsKey(e.Id))];
        }

        /// <summary>
        /// Phase 1 (Foreground): lokale Quellen parallel. Keine externen HTTP-Calls, reines
        /// Dateisystem, daher Parallelismus sicher.
        /// </summary>
        private async Task LoadLocalCoversAsync(IServiceProvider services, List<Episode> missing, CancellationToken ct)
        {
            ILocalCoverLoader coverLoader = services.GetRequiredService<ILocalCoverLoader>();
            IReadOnlyDictionary<Guid, LocalTrack> firstTracks = await services.GetRequiredService<ILocalTrackDataService>()
                .GetFirstTracksByEpisodeIdsAsync(missing.Select(e => e.Id).ToArray(), ct);

            ParallelOptions parallelOptions = new()
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = ForegroundLocalParallelism
            };

            await Parallel.ForEachAsync(missing, parallelOptions, async (episode, token) =>
            {
                if (string.IsNullOrEmpty(episode.LocalFolderPath)) return;

                string? firstTrackPath = firstTracks.TryGetValue(episode.Id, out LocalTrack? t) ? t.FilePath : null;
                await TryForEpisodeAsync(episode, "Lokal-Cover", async () =>
                {
                    byte[]? bytes = await coverLoader.LoadAsync(episode.LocalFolderPath, firstTrackPath);
                    if (bytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, bytes, cancellationToken: token);
                    }
                }, token);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Phase 2 (Foreground): Provider-URLs. HTTP, daher seriell und jeder Abruf beim Begrenzer
        /// angemeldet: Eine Serie mit Hunderten Folgen ohne Cover hält so den Mindestabstand zum
        /// Bild-Server ein, und die Vordergrund-Priorität lässt wartende Hintergrund-Abrufe
        /// zurückstehen.
        /// </summary>
        private async Task LoadProviderCoversAsync(ICoverImageDataService coverImageService, List<Episode> missing, CancellationToken ct)
        {
            IReadOnlyDictionary<Guid, byte[]> stillMissing =
                await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, missing.Select(e => e.Id).ToArray(), ct);

            foreach (Episode episode in missing)
            {
                ct.ThrowIfCancellationRequested();
                if (stillMissing.ContainsKey(episode.Id)) continue;
                Uri? coverAddress = WebAddress.TryParse(episode.CoverImageUrl);
                if (coverAddress is null) continue;

                await TryForEpisodeAsync(episode, "Provider-Cover", async () =>
                {
                    if (_rateLimiter is not null)
                    {
                        await _rateLimiter.WaitAsync(coverAddress.Host, CoverFetchPriority.Foreground, ct).ConfigureAwait(false);
                    }

                    byte[]? bytes = await _coverDownloader.DownloadAsync(coverAddress, ct);
                    if (bytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, bytes, coverAddress, ct);
                    }
                }, ct);
            }
        }

        /// <summary>
        /// Führt den Ladeschritt einer Folge aus. Ein Abbruch durch den Nutzer geht durch, jeder
        /// andere Fehler nur ins Protokoll.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Foreground-Priority-Pfad: Einzelne TagLib-/IO-/HTTP-Fehler pro Episode werden geloggt, damit das Priorisierungs-Fenster für die sichtbare Serie nicht wegen einer kaputten Datei abbricht.")]
        private async Task TryForEpisodeAsync(Episode episode, string source, Func<Task> step, CancellationToken token)
        {
            try
            {
                await step();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Der Nutzer hat die Detailseite verlassen; das ist kein Fehlschlag dieser Folge.
                throw;
            }
            catch (Exception ex)
            {
                _logger.Debug(() => $"Priority {source} fehlgeschlagen für \"{episode.Title}\": {ex.Message}");
            }
        }

        /// <summary>
        /// Lädt das Cover für ein Such-Treffer-Element. Erst DB-First (falls die Serie
        /// bereits in der lokalen Bibliothek existiert und dort ein Cover hinterlegt ist),
        /// danach Provider-URL über <see cref="IHostRateLimiter"/> mit
        /// <see cref="CoverFetchPriority.Foreground"/>. Markiert den Vorrang als
        /// "Foreground aktiv", sodass der Hintergrund-Loop pausiert. Persistiert das
        /// Cover <b>nicht</b> in <c>CoverImages</c>, denn Such-Treffer sind noch nicht importiert.
        /// </summary>
        /// <param name="source">Quelle des Treffers aus <see cref="ProviderKeys"/>. Andere Werte verhindern den DB-Lookup.</param>
        /// <param name="sourceSeriesId">Kennung der Serie bei der Quelle (Anbieter-Kennung oder Datenbank-Kennung).</param>
        /// <param name="coverUrl">
        /// Cover-Adresse aus dem Such-Treffer. Bleibt sie leer, endet der Weg beim
        /// Datenbank-Zweig. Für Treffer aus dem eigenen Bestand ist das der einzige und
        /// zugleich der schnellste Weg.
        /// </param>
        /// <param name="ct">Abbruch-Token der laufenden Suche.</param>
        /// <returns>Cover-Bytes oder <see langword="null"/> bei Fehler/Abbruch ohne Daten.</returns>
        internal async Task<byte[]?> RequestCoverForSearchResultAsync(
            string source, string sourceSeriesId, Uri? coverUrl, CancellationToken ct = default)
        {
            byte[]? cached = await TryGetCachedSeriesCoverAsync(source, sourceSeriesId, ct).ConfigureAwait(false);
            if (cached is not null) return cached;

            if (coverUrl is null) return null;

            _ = Interlocked.Increment(ref _priorityInFlight);
            try
            {
                if (_rateLimiter is not null)
                {
                    await _rateLimiter.WaitAsync(coverUrl.Host, CoverFetchPriority.Foreground, ct).ConfigureAwait(false);
                }

                return await _coverDownloader.DownloadAsync(coverUrl, ct).ConfigureAwait(false);
            }
            finally
            {
                _ = Interlocked.Decrement(ref _priorityInFlight);
            }
        }

        /// <summary>
        /// Findet eine bereits importierte Serie über ihre Quell-Kennung und liefert
        /// deren persistiertes Cover aus <c>CoverImages</c>. Liefert <see langword="null"/>,
        /// wenn die Serie noch nicht importiert ist oder die Quelle unbekannt ist.
        /// Treffer aus dem eigenen Bestand tragen die Datenbank-Kennung selbst.
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
                ProviderKeys.Local => Guid.TryParse(sourceSeriesId, out Guid seriesId)
                    ? await seriesService.GetByIdAsync(seriesId, cancellationToken).ConfigureAwait(false)
                    : null,
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

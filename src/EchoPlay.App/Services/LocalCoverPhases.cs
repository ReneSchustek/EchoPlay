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
    /// Die Nachlade-Phasen, die ohne Netz auskommen: Cover aus dem Dateisystem
    /// (<c>cover.jpg</c>, ID3-Tags) und die SQL-Kopie von lokalen auf Online-Folgen.
    /// </summary>
    /// <remarks>
    /// Die Trennung von <see cref="OnlineCoverPhases"/> ist keine Kosmetik: Diese Phasen
    /// laufen auch im Offline-Modus, brauchen keine Zugangsdaten und kein Wartelimit. Wer
    /// hier einen HTTP-Aufruf einbaut, verletzt genau die Zusage, wegen der der Splash-Pfad
    /// sie ungebremst aufrufen darf.
    /// </remarks>
    internal sealed class LocalCoverPhases
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ICoverService _coverService;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert die lokalen Phasen. Der Logger kommt vom
        /// <see cref="BackgroundCoverService"/>, damit alle Meldungen eines Durchlaufs unter
        /// derselben Quelle im Protokoll stehen.
        /// </summary>
        internal LocalCoverPhases(
            IServiceScopeFactory scopeFactory,
            ICoverService coverService,
            ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _coverService = coverService;
            _logger = logger;
        }
        /// <summary>
        /// Sucht Serien mit lokalem Ordner aber ohne Cover in CoverImages
        /// und lädt <c>cover.jpg</c> aus dem Stammordner. ID3-Fallback entfällt bewusst,
        /// weil Serien-Cover nur als Dateien im Stammordner existieren.
        /// </summary>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        internal async Task<int> LoadMissingLocalSeriesCoversAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>();
            ILocalCoverLoader coverLoader = scope.ServiceProvider
                .GetRequiredService<ILocalCoverLoader>();
            ICoverImageDataService coverImageService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync(ct);

            _logger.Info("Lokal-Check Serien: {SeriesCount} Serien gesamt.", allSeries.Count);

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
            _logger.Info(
                "Lokal-Check Serien: {WithFolderCount} mit LocalFolderPath, {ExistingCount} in DB, {MissingCount} fehlen.",
                seriesWithFolder.Count, existingSeries.Count, missingSeries);

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
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        internal async Task<int> LoadMissingLocalEpisodeCoversAsync(CancellationToken ct)
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

            _logger.Info(
                "Lokal-Check Episoden: {CandidateCount} mit Ordner, {ExistingCount} in DB, {Loaded} geladen, {NotFound} ohne Cover-Datei.",
                candidates.Count, existing.Count, loaded, notFound);

            return loaded;
        }
        /// <summary>
        /// Kopiert vorhandene Cover von lokalen Episoden auf Online-Episoden derselben Serie.
        /// Nutzt <see cref="ICoverCopyService"/> – reines SQL (INSERT OR IGNORE), kein Netzwerk.
        /// Nur Episoden ohne vorhandenes Cover werden befüllt.
        /// Schnell genug für den Splash (eine SQL-Query pro Online-Serie).
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        internal async Task<int> CopyLocalToOnlineAsync(CancellationToken cancellationToken = default)
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
        /// Stellt sicher, dass alle lokalen Episoden einer Serie (nach Titel) ihre Cover
        /// in CoverImages haben. Wird synchron vor der Anzeige aufgerufen, damit der
        /// CoverCopyService danach Quellen findet.
        /// </summary>
        /// <param name="seriesTitle">Titel der Serie (z.B. "Fünf Freunde").</param>
        /// <returns>Anzahl der neu geladenen Cover.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        internal async Task<int> EnsureLocalCoversForSeriesAsync(string seriesTitle, CancellationToken cancellationToken = default)
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
                _logger.Info("Lokale Cover für \"{SeriesTitle}\": {Loaded} in DB geladen.", seriesTitle, loaded);
            }

            return loaded;
        }
    }
}

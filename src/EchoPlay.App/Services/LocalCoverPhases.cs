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
        /// und lädt <c>cover.jpg</c> aus dem Stammordner. ID3-Fallback entfällt,
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

            List<Series> seriesWithFolder = [.. allSeries.Where(series => !string.IsNullOrEmpty(series.LocalFolderPath))];
            if (seriesWithFolder.Count == 0)
            {
                return 0;
            }

            IReadOnlyDictionary<Guid, byte[]> existingSeries = await coverImageService.GetImageDataByEntitiesAsync(
                CoverEntityTypes.Series, seriesWithFolder.Select(s => s.Id).ToArray(), ct);
            _logger.Info(
                "Lokal-Check Serien: {WithFolderCount} mit LocalFolderPath, {ExistingCount} in DB, {MissingCount} fehlen.",
                seriesWithFolder.Count, existingSeries.Count, seriesWithFolder.Count - existingSeries.Count);

            return await LoadMissingSeriesCoversAsync(
                coverLoader, seriesWithFolder.Where(series => !existingSeries.ContainsKey(series.Id)), ct);
        }

        /// <summary>Lädt die Cover nacheinander, bis der Abbruch kommt.</summary>
        /// <returns>Wie viele Cover gefunden wurden.</returns>
        private async Task<int> LoadMissingSeriesCoversAsync(ILocalCoverLoader coverLoader, IEnumerable<Series> missing, CancellationToken ct)
        {
            int loaded = 0;
            foreach (Series series in missing)
            {
                if (ct.IsCancellationRequested) break;
                if (await TryLoadLocalSeriesCoverAsync(coverLoader, series, ct))
                {
                    loaded++;
                }
            }

            return loaded;
        }

        /// <summary>
        /// Lädt <c>cover.jpg</c> aus dem Stammordner einer Serie und speichert es. Ohne erste Spur,
        /// denn für Serien-Cover gibt es keinen ID3-Rückfall.
        /// </summary>
        /// <returns><see langword="true"/>, wenn ein Cover gefunden wurde.</returns>
        private async Task<bool> TryLoadLocalSeriesCoverAsync(ILocalCoverLoader coverLoader, Series series, CancellationToken ct)
        {
            byte[]? coverBytes = await coverLoader.LoadAsync(series.LocalFolderPath, null);
            if (coverBytes is null)
            {
                _logger.Debug(() => $"Lokal: Kein Cover gefunden für \"{series.Title}\" in {EchoPlay.Core.Logging.PathRedactor.Redact(series.LocalFolderPath)}");
                return false;
            }

            await _coverService.SetSeriesCoverAsync(series.Id, coverBytes, cancellationToken: ct);
            _logger.Debug(() => $"Lokal: Serien-Cover geladen \"{series.Title}\" aus {EchoPlay.Core.Logging.PathRedactor.Redact(series.LocalFolderPath)}");
            return true;
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
            CoverTools tools = CoverTools.From(scope.ServiceProvider);

            IReadOnlyList<Series> allSeries = await scope.ServiceProvider.GetRequiredService<ISeriesDataService>().GetAllAsync(ct);
            IReadOnlyList<Episode> allEpisodes = await tools.Episodes.GetBySeriesIdsAsync(allSeries.Select(s => s.Id).ToArray(), ct);

            EpisodeCoverResult result = await LoadForEpisodesAsync(allEpisodes, tools, ct);
            if (result.Candidates == 0)
            {
                _logger.Info("Lokal-Check Episoden: keine Kandidaten mit lokalem Ordner gefunden.");
                return 0;
            }

            _logger.Info(
                "Lokal-Check Episoden: {CandidateCount} mit Ordner, {ExistingCount} in DB, {Loaded} geladen, {NotFound} ohne Cover-Datei.",
                result.Candidates, result.Existing, result.Loaded, result.NotFound);
            return result.Loaded;
        }

        /// <summary>
        /// Lädt für Folgen mit lokalem Ordner, aber ohne gespeichertes Cover, das Cover aus dem
        /// Ordner oder dem ersten Track. Die ersten Tracks kommen in einer Abfrage, nicht je Folge.
        /// </summary>
        private async Task<EpisodeCoverResult> LoadForEpisodesAsync(IReadOnlyList<Episode> episodes, CoverTools tools, CancellationToken ct)
        {
            List<Episode> candidates = [.. episodes.Where(e => !string.IsNullOrEmpty(e.LocalFolderPath))];
            if (candidates.Count == 0)
            {
                return new EpisodeCoverResult(0, 0, 0, 0);
            }

            IReadOnlyDictionary<Guid, byte[]> existing =
                await tools.Images.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, candidates.Select(e => e.Id).ToArray(), ct);
            List<Episode> missing = [.. candidates.Where(e => !existing.ContainsKey(e.Id))];
            IReadOnlyDictionary<Guid, LocalTrack> firstTracks =
                await tools.Tracks.GetFirstTracksByEpisodeIdsAsync(missing.Select(e => e.Id).ToArray(), ct);

            int loaded = 0;
            int notFound = 0;
            foreach (Episode episode in missing)
            {
                if (ct.IsCancellationRequested) break;

                string? firstTrackPath = firstTracks.TryGetValue(episode.Id, out LocalTrack? firstTrack) ? firstTrack.FilePath : null;
                byte[]? coverBytes = await tools.Loader.LoadAsync(episode.LocalFolderPath, firstTrackPath);
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

            return new EpisodeCoverResult(candidates.Count, existing.Count, loaded, notFound);
        }

        /// <summary>
        /// Kopiert vorhandene Cover von lokalen Episoden auf Online-Episoden derselben Serie.
        /// Nutzt <see cref="ICoverCopyService"/>: reines SQL (INSERT OR IGNORE), kein Netzwerk.
        /// Nur Episoden ohne vorhandenes Cover werden befüllt.
        /// Schnell genug für den Splash: je Online-Serie zwei Leseabfragen und höchstens drei
        /// INSERT-Anweisungen, ohne die Bilddaten in die Anwendung zu laden.
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
        /// Lädt die fehlenden Cover aller lokalen Episoden einer Serie (nach Titel) in
        /// CoverImages. Wird synchron vor der Anzeige aufgerufen, damit der
        /// CoverCopyService danach Quellen findet.
        /// </summary>
        /// <param name="seriesTitle">Titel der Serie (z.B. "Fünf Freunde").</param>
        /// <returns>Anzahl der neu geladenen Cover.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        internal async Task<int> EnsureLocalCoversForSeriesAsync(string seriesTitle, CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            CoverTools tools = CoverTools.From(scope.ServiceProvider);

            // Alle Serien mit gleichem Titel finden (lokal + online)
            IReadOnlyList<Series> allSeries = await scope.ServiceProvider.GetRequiredService<ISeriesDataService>().GetAllAsync(cancellationToken);
            int loaded = 0;
            foreach (Series series in allSeries.Where(s => string.Equals(s.Title, seriesTitle, StringComparison.OrdinalIgnoreCase)))
            {
                IReadOnlyList<Episode> episodes = await tools.Episodes.GetBySeriesIdAsync(series.Id, cancellationToken);
                loaded += (await LoadForEpisodesAsync(episodes, tools, cancellationToken)).Loaded;
            }

            if (loaded > 0)
            {
                _logger.Info("Lokale Cover für \"{SeriesTitle}\": {Loaded} in DB geladen.", seriesTitle, loaded);
            }

            return loaded;
        }

        /// <summary>Was das Laden der Folgen-Cover ergeben hat.</summary>
        /// <param name="Candidates">Folgen mit lokalem Ordner.</param>
        /// <param name="Existing">Davon schon mit Cover.</param>
        /// <param name="Loaded">Neu geladen.</param>
        /// <param name="NotFound">Ohne Cover im Ordner oder in der ersten Spur.</param>
        private sealed record EpisodeCoverResult(int Candidates, int Existing, int Loaded, int NotFound);

        /// <summary>Die Dienste zum Laden lokaler Folgen-Cover, aus dem Bereich des Aufrufers.</summary>
        /// <param name="Episodes">Folgen lesen.</param>
        /// <param name="Tracks">Erste Spuren lesen.</param>
        /// <param name="Loader">Liest Cover aus Ordner und Tags.</param>
        /// <param name="Images">Gespeicherte Cover.</param>
        private sealed record CoverTools(IEpisodeDataService Episodes, ILocalTrackDataService Tracks, ILocalCoverLoader Loader, ICoverImageDataService Images)
        {
            /// <summary>Holt die Dienste aus dem Bereich.</summary>
            public static CoverTools From(IServiceProvider services) => new(
                services.GetRequiredService<IEpisodeDataService>(),
                services.GetRequiredService<ILocalTrackDataService>(),
                services.GetRequiredService<ILocalCoverLoader>(),
                services.GetRequiredService<ICoverImageDataService>());
        }
    }
}

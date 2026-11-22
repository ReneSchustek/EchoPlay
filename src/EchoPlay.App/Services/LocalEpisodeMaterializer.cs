using EchoPlay.App.Helpers;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Matching;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Bringt die eingelesenen Folgen und ihre Spuren in die Datenbank: neu erkannte
    /// Serien in einem Zug, bestehende Folge für Folge abgeglichen.
    /// </summary>
    /// <remarks>
    /// Das ist der Schritt, der aus einem Dateisystem-Befund einen Bestand macht. Er ist die
    /// vierte Phase des Einlesens, hat mit dem Suchen und Erkennen davor aber nichts zu tun
    /// und steht deshalb für sich.
    /// </remarks>
    internal sealed class LocalEpisodeMaterializer
    {
        private readonly ILogger _logger;
        private readonly IScanEventService _scanEventService;

        /// <summary>
        /// Richtet den Schritt ein.
        /// </summary>
        /// <param name="logger">Protokollkanal für nicht lesbare Dateien.</param>
        /// <param name="scanEventService">Meldet jede fertige Serie, damit die Anzeige mitzählt.</param>
        public LocalEpisodeMaterializer(ILogger logger, IScanEventService scanEventService)
        {
            _logger = logger;
            _scanEventService = scanEventService;
        }

        /// <summary>
        /// Persistiert die Episoden für jede materialisierte Serie. Neu angelegte
        /// Serien bekommen einen Batch-Import; bestehende werden Episode-für-Episode
        /// gegen die DB abgeglichen und Tracks aktualisiert.
        /// </summary>
        // Helper-Methode: Provider kommt aus dem aufrufenden Scope (kein Service-Locator im Konstruktor).
        public async Task<(int EpisodesUpdated, int TracksCreated)> MaterializeEpisodesAsync(
            IServiceProvider sp,
            SyncService.MaterializationResult materialization,
            IProgress<ScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            Stores stores = new(
                sp.GetRequiredService<IEpisodeDataService>(),
                sp.GetRequiredService<ILocalTrackDataService>(),
                sp.GetRequiredService<IAudioMetadataReader>(),
                sp.GetRequiredService<ITrackMatcher>());

            // Muster erst in eine Variable: mit dem Aufruf direkt im string.Format verlangt
            // der Analyzer ein zwischengespeichertes CompositeFormat, was bei einem zur
            // Laufzeit wechselnden Sprachtext nichts brächte.
            string syncStatusPattern = SafeResourceLoader.Get(
                "ScanStatusSyncingSeries", "Synchronisiere „{0}“ …");

            int episodesUpdated = 0;
            int tracksCreated = 0;
            int processedSeries = 0;
            foreach (SyncService.SeriesPipelineEntry entry in materialization.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedSeries++;
                ReportProgress(progress, syncStatusPattern, entry, processedSeries, materialization.Entries.Count);

                (int episodes, int tracks) = entry.IsNewlyCreated
                    ? await ImportNewSeriesAsync(entry, stores, cancellationToken)
                    : await SyncExistingSeriesAsync(entry, stores, cancellationToken);
                episodesUpdated += episodes;
                tracksCreated += tracks;

                // Erneut melden, jetzt sind die Episoden persistiert. Die in Phase 3 mit noch
                // 0 Episoden angelegte Kachel zieht dadurch live auf die korrekte Zahl nach,
                // statt bis zum Abschluss-Reload auf "0 / 0" zu verharren.
                _scanEventService.NotifySeriesSynced(entry.Series);
            }

            return (episodesUpdated, tracksCreated);
        }

        /// <summary>Meldet, welche Serie gerade abgeglichen wird und die wievielte sie ist.</summary>
        private static void ReportProgress(
            IProgress<ScanProgress>? progress, string statusPattern, SyncService.SeriesPipelineEntry entry, int processed, int total)
        {
            progress?.Report(new ScanProgress
            {
                StatusText = string.Format(CultureInfo.CurrentCulture, statusPattern, entry.ScanResult.SeriesName),
                DetailText = string.Format(
                    CultureInfo.CurrentCulture,
                    PluralText.Pattern(
                        total,
                        "ScanDetailSeriesProgressSingular",
                        "ScanDetailSeriesProgressPlural",
                        "{0} / {1} Serie",
                        "{0} / {1} Serien"),
                    processed, total),
                ProcessedSeries = processed,
                TotalSeries = total
            });
        }

        /// <summary>Legt alle Folgen einer neu erkannten Serie in einem Zug an.</summary>
        private async Task<(int Episodes, int Tracks)> ImportNewSeriesAsync(
            SyncService.SeriesPipelineEntry entry, Stores stores, CancellationToken cancellationToken)
        {
            (int created, int createdTracks) = await ImportEpisodesAsync(
                entry.Series.Id, entry.ScanResult.Episodes, stores, cancellationToken);
            _logger.Info(
                "Auto-Import: Neue Serie \"{SeriesName}\" mit {CreatedEpisodes} Episoden angelegt",
                entry.ScanResult.SeriesName, created);
            return (created, createdTracks);
        }

        /// <summary>
        /// Gleicht eine bestehende Serie ab: erst über den Ordner, dann über die Nummer. Ordner, die
        /// noch keiner Folge gehören, sind neu hinzugekommen und werden wie bei einer neuen Serie
        /// angelegt.
        /// </summary>
        private async Task<(int Episodes, int Tracks)> SyncExistingSeriesAsync(
            SyncService.SeriesPipelineEntry entry, Stores stores, CancellationToken cancellationToken)
        {
            IReadOnlyList<Episode> episodes = await stores.Episodes.GetBySeriesIdAsync(entry.Series.Id, cancellationToken);
            IReadOnlyList<(LocalEpisodeScan Scan, Episode Episode)> matches =
                StoredEpisodeMatcher.Match(entry.ScanResult.Episodes, episodes);

            int updated = 0;
            int tracks = 0;
            int unchanged = 0;
            foreach ((LocalEpisodeScan episodeScan, Episode episode) in matches)
            {
                int? created = await UpdateMatchedEpisodeAsync(episodeScan, episode, stores, cancellationToken);
                if (created is null)
                {
                    unchanged++;
                    continue;
                }

                updated++;
                tracks += created.Value;
            }

            if (unchanged > 0)
            {
                _logger.Debug(() => $"Einlesen: {unchanged} unveränderte Folgen in \"{entry.ScanResult.SeriesName}\" übersprungen");
            }

            (int added, int addedTracks) = await ImportNewFoldersAsync(entry, matches, stores, cancellationToken);
            return (updated + added, tracks + addedTracks);
        }

        /// <summary>Legt Folgen für Ordner an, die noch keiner bekannten Folge gehören.</summary>
        private async Task<(int Episodes, int Tracks)> ImportNewFoldersAsync(
            SyncService.SeriesPipelineEntry entry,
            IReadOnlyList<(LocalEpisodeScan Scan, Episode Episode)> matches,
            Stores stores,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<LocalEpisodeScan> newScans = StoredEpisodeMatcher.Unmatched(entry.ScanResult.Episodes, matches);
            if (newScans.Count == 0)
            {
                return (0, 0);
            }

            (int added, int addedTracks) = await ImportEpisodesAsync(entry.Series.Id, newScans, stores, cancellationToken);
            _logger.Info(
                "Einlesen: neu angelegt in \"{SeriesName}\": {AddedEpisodes}",
                entry.ScanResult.SeriesName, added);
            return (added, addedTracks);
        }

        /// <summary>
        /// Gleicht eine schon bekannte Folge mit ihrem Ordner ab.
        /// </summary>
        /// <returns>Die Zahl der neu angelegten Spuren, oder <see langword="null"/>, wenn die Folge unverändert ist.</returns>
        private async Task<int?> UpdateMatchedEpisodeAsync(
            LocalEpisodeScan episodeScan, Episode episode, Stores stores, CancellationToken cancellationToken)
        {
            // Eine unveränderte Folge braucht weder neue Tags noch neue Spuren. Das Lesen der
            // Tags kostet je Folge rund eine Sekunde; über die ganze Sammlung sind das Stunden.
            IReadOnlyList<LocalTrack> storedTracks = await stores.Tracks.GetByEpisodeIdAsync(episode.Id, cancellationToken);
            if (StoredEpisodeMatcher.IsUnchanged(episode, episodeScan, storedTracks))
            {
                return null;
            }

            int onlineTrackCount = episode.LocalTrackCount ?? 0;

            // Die Nummer folgt dem Ordnernamen. Eine abweichend gespeicherte Nummer wird so
            // beim Einlesen berichtigt, und eine Sonderfolge gibt die Nummer einer
            // regulären Folge frei.
            episode.EpisodeNumber = episodeScan.ParsedNumber;
            episode.LocalFolderPath = episodeScan.FolderPath;
            episode.LocalTrackCount = episodeScan.TrackCount;
            episode.TrackMatchKind = stores.Matcher.Classify(episodeScan.TrackCount, onlineTrackCount);

            await stores.Episodes.UpdateAsync(episode, cancellationToken);
            return await CreateLocalTracksAsync(episode.Id, episodeScan.TrackPaths, stores, cancellationToken);
        }

        /// <summary>
        /// Legt Episoden und lokale Tracks für eine neu auto-importierte Serie an.
        /// Die Nummern hat der Scanner schon vergeben; eine Folge ohne Nummer ist eine
        /// Sonderfolge und bleibt ohne, damit sie keiner regulären Folge die Nummer nimmt.
        /// </summary>
        private async Task<(int Episodes, int Tracks)> ImportEpisodesAsync(
            Guid seriesId,
            IReadOnlyList<LocalEpisodeScan> episodeScans,
            Stores stores,
            CancellationToken cancellationToken)
        {
            List<Episode> newEpisodes = new(episodeScans.Count);

            foreach (LocalEpisodeScan episodeScan in episodeScans)
            {
                newEpisodes.Add(new Episode
                {
                    SeriesId = seriesId,
                    EpisodeNumber = episodeScan.ParsedNumber,
                    Title = episodeScan.ParsedTitle ?? System.IO.Path.GetFileName(episodeScan.FolderPath),
                    LocalFolderPath = episodeScan.FolderPath,
                    LocalTrackCount = episodeScan.TrackCount
                });
            }

            await stores.Episodes.AddRangeAsync(newEpisodes, cancellationToken);

            int trackCount = 0;
            for (int i = 0; i < episodeScans.Count; i++)
            {
                int created = await CreateLocalTracksAsync(
                    newEpisodes[i].Id,
                    episodeScans[i].TrackPaths,
                    stores,
                    cancellationToken);
                trackCount += created;
            }

            return (newEpisodes.Count, trackCount);
        }

        /// <summary>
        /// Legt <see cref="LocalTrack"/>-Einträge für eine Episode an, liest
        /// Metadaten aus den Audiodateien. TagLib# läuft synchron auf dem Threadpool.
        /// Nicht lesbare Dateien werden übersprungen.
        /// </summary>
        private async Task<int> CreateLocalTracksAsync(
            Guid episodeId,
            IReadOnlyList<string> trackPaths,
            Stores stores,
            CancellationToken cancellationToken = default)
        {
            // Liegen die Spuren auf mehreren CDs, beginnt die Spurnummer im Tag je CD wieder bei 1.
            // Die Wiedergabe sortiert nach Spurnummer; dann zählt die Reihenfolge der Dateien.
            bool spansSeveralFolders = EchoPlay.LocalLibrary.Scanning.EpisodeAudioFiles.FolderCount(trackPaths) > 1;
            List<LocalTrack> tracks = await Task.Run(
                () => trackPaths.Select((path, index) => ReadTrack(episodeId, path, index, spansSeveralFolders, stores.Metadata)).ToList(),
                cancellationToken);

            await stores.Tracks.SaveTracksForEpisodeAsync(episodeId, tracks, cancellationToken);
            return tracks.Count;
        }

        /// <summary>
        /// Liest Dauer und Spurnummer einer Datei. Eine nicht lesbare Datei bekommt die Position
        /// in der Liste als Nummer und keine Dauer.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "TagLib-/IO-/DB-Fehler einzelner Audio-Dateien (korrupte Tags, gesperrte Dateien, Pfad-zu-lang) dürfen die Track-Anlage für die restlichen Dateien nicht abbrechen; Einzelfehler werden geloggt und übersprungen.")]
        private LocalTrack ReadTrack(Guid episodeId, string path, int index, bool spansSeveralFolders, IAudioMetadataReader metadata)
        {
            TimeSpan duration = TimeSpan.Zero;
            int trackNumber = index + 1;
            try
            {
                (TimeSpan readDuration, int readTrackNumber) = metadata.Read(path);
                duration = readDuration;
                if (readTrackNumber > 0 && !spansSeveralFolders)
                {
                    trackNumber = readTrackNumber;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("Metadaten nicht lesbar, Standardwerte werden verwendet: {Path} ({Reason})", EchoPlay.Core.Logging.PathRedactor.Redact(path), ex.Message);
            }

            return new LocalTrack
            {
                EpisodeId = episodeId,
                FilePath = path,
                TrackNumber = trackNumber,
                Duration = duration
            };
        }

        /// <summary>Die Dienste eines Durchgangs, aus dem Bereich des Aufrufers.</summary>
        /// <param name="Episodes">Folgen lesen und schreiben.</param>
        /// <param name="Tracks">Spuren lesen und schreiben.</param>
        /// <param name="Metadata">Liest Dauer und Spurnummer aus den Dateien.</param>
        /// <param name="Matcher">Vergleicht lokale mit bekannten Spurzahlen.</param>
        private sealed record Stores(
            IEpisodeDataService Episodes,
            ILocalTrackDataService Tracks,
            IAudioMetadataReader Metadata,
            ITrackMatcher Matcher);
    }
}

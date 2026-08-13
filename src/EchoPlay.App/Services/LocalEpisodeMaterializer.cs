using EchoPlay.App.Helpers;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Matching;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Bringt die eingelesenen Folgen und ihre Spuren in die Datenbank: neu erkannte
    /// Serien in einem Zug, bestehende Folge für Folge abgeglichen.
    /// </summary>
    /// <remarks>
    /// Das ist der Schritt, der aus einem Dateisystem-Befund einen Bestand macht. Er
    /// stand als vierte Phase im Einlese-Dienst und machte dort ein Drittel der Klasse
    /// aus, obwohl er mit dem Suchen und Erkennen davor nichts zu tun hat.
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
            IEpisodeDataService episodeService = sp.GetRequiredService<IEpisodeDataService>();
            ILocalTrackDataService trackService = sp.GetRequiredService<ILocalTrackDataService>();
            IAudioMetadataReader metadataReader = sp.GetRequiredService<IAudioMetadataReader>();
            ITrackMatcher trackMatcher = sp.GetRequiredService<ITrackMatcher>();

            int episodesUpdated = 0;
            int tracksCreated = 0;
            int processedSeries = 0;

            // Muster erst in eine Variable: mit dem Aufruf direkt im string.Format verlangt
            // der Analyzer ein zwischengespeichertes CompositeFormat, was bei einem zur
            // Laufzeit wechselnden Sprachtext nichts brächte.
            string syncStatusPattern = SafeResourceLoader.Get(
                "ScanStatusSyncingSeries", "Synchronisiere „{0}\" …");

            foreach (SyncService.SeriesPipelineEntry entry in materialization.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedSeries++;

                progress?.Report(new ScanProgress
                {
                    StatusText = string.Format(
                        CultureInfo.CurrentCulture,
                        syncStatusPattern,
                        entry.ScanResult.SeriesName),
                    DetailText = string.Format(
                        CultureInfo.CurrentCulture,
                        PluralText.Pattern(
                            materialization.Entries.Count,
                            "ScanDetailSeriesProgressSingular",
                            "ScanDetailSeriesProgressPlural",
                            "{0} / {1} Serie",
                            "{0} / {1} Serien"),
                        processedSeries, materialization.Entries.Count),
                    ProcessedSeries = processedSeries,
                    TotalSeries = materialization.Entries.Count
                });

                if (entry.IsNewlyCreated)
                {
                    (int created, int createdTracks) = await ImportEpisodesAsync(
                        entry.Series.Id, entry.ScanResult.Episodes,
                        episodeService, trackService, metadataReader, cancellationToken);
                    episodesUpdated += created;
                    tracksCreated += createdTracks;

                    _logger.Info(
                        "Auto-Import: Neue Serie \"{SeriesName}\" mit {CreatedEpisodes} Episoden angelegt",
                        entry.ScanResult.SeriesName, created);

                    // Erneut melden – jetzt sind die Episoden persistiert. Die in Phase 3 mit noch
                    // 0 Episoden angelegte Kachel zieht dadurch live auf die korrekte Zahl nach,
                    // statt bis zum Abschluss-Reload auf "0 / 0" zu verharren.
                    _scanEventService.RaiseSeriesSynced(entry.Series);
                    continue;
                }

                // Bestehende Serie: Episoden anhand Nummer abgleichen
                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(entry.Series.Id, cancellationToken);

                foreach (LocalEpisodeScan episodeScan in entry.ScanResult.Episodes)
                {
                    if (episodeScan.ParsedNumber is null) continue;

                    Episode? episode = FindEpisodeByNumber(episodes, episodeScan.ParsedNumber.Value);
                    if (episode is null) continue;

                    int onlineTrackCount = episode.LocalTrackCount ?? 0;
                    TrackMatchKind matchKind = trackMatcher.Classify(episodeScan.TrackCount, onlineTrackCount);

                    episode.LocalFolderPath = episodeScan.FolderPath;
                    episode.LocalTrackCount = episodeScan.TrackCount;
                    episode.TrackMatchKind = matchKind;

                    await episodeService.UpdateAsync(episode, cancellationToken);
                    episodesUpdated++;

                    int created = await CreateLocalTracksAsync(
                        episode.Id, episodeScan.TrackPaths, trackService, metadataReader, cancellationToken);
                    tracksCreated += created;
                }

                // Bestehende Serie fertig abgeglichen – Kachelzähler live nachziehen.
                _scanEventService.RaiseSeriesSynced(entry.Series);
            }

            return (episodesUpdated, tracksCreated);
        }

        /// <summary>
        /// Legt Episoden und lokale Tracks für eine neu auto-importierte Serie an.
        /// Episoden ohne geparste Nummer erhalten eine sequenzielle Nummer (1, 2, 3 …).
        /// </summary>
        private async Task<(int Episodes, int Tracks)> ImportEpisodesAsync(
            Guid seriesId,
            IReadOnlyList<LocalEpisodeScan> episodeScans,
            IEpisodeDataService episodeService,
            ILocalTrackDataService trackService,
            IAudioMetadataReader metadataReader,
            CancellationToken cancellationToken)
        {
            List<Episode> newEpisodes = new(episodeScans.Count);
            int sequentialIndex = 0;

            foreach (LocalEpisodeScan episodeScan in episodeScans)
            {
                sequentialIndex++;
                int episodeNumber = episodeScan.ParsedNumber ?? sequentialIndex;

                newEpisodes.Add(new Episode
                {
                    SeriesId = seriesId,
                    EpisodeNumber = episodeNumber,
                    Title = episodeScan.ParsedTitle ?? $"Folge {episodeNumber}",
                    LocalFolderPath = episodeScan.FolderPath,
                    LocalTrackCount = episodeScan.TrackCount
                });
            }

            await episodeService.AddRangeAsync(newEpisodes, cancellationToken);

            int trackCount = 0;
            for (int i = 0; i < episodeScans.Count; i++)
            {
                int created = await CreateLocalTracksAsync(
                    newEpisodes[i].Id,
                    episodeScans[i].TrackPaths,
                    trackService,
                    metadataReader,
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "TagLib-/IO-/DB-Fehler einzelner Audio-Dateien (korrupte Tags, gesperrte Dateien, Pfad-zu-lang) dürfen die Track-Anlage für die restlichen Dateien nicht abbrechen; Einzelfehler werden geloggt und übersprungen.")]
        private async Task<int> CreateLocalTracksAsync(
            Guid episodeId,
            IReadOnlyList<string> trackPaths,
            ILocalTrackDataService trackService,
            IAudioMetadataReader metadataReader,
            CancellationToken cancellationToken = default)
        {
            List<LocalTrack> tracks = await Task.Run(() =>
            {
                List<LocalTrack> result = new(trackPaths.Count);

                for (int i = 0; i < trackPaths.Count; i++)
                {
                    string path = trackPaths[i];
                    TimeSpan duration = TimeSpan.Zero;
                    int trackNumber = i + 1;

                    try
                    {
                        (TimeSpan readDuration, int readTrackNumber) = metadataReader.Read(path);
                        duration = readDuration;
                        if (readTrackNumber > 0)
                        {
                            trackNumber = readTrackNumber;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Metadaten nicht lesbar, Standardwerte werden verwendet: {Path} ({Reason})", path, ex.Message);
                    }

                    result.Add(new LocalTrack
                    {
                        EpisodeId = episodeId,
                        FilePath = path,
                        TrackNumber = trackNumber,
                        Duration = duration
                    });
                }

                return result;
            }, cancellationToken);

            await trackService.SaveTracksForEpisodeAsync(episodeId, tracks, cancellationToken);
            return tracks.Count;
        }

        /// <summary>
        /// Sucht die Folge mit dieser Nummer im vorhandenen Bestand.
        /// </summary>
        private static Episode? FindEpisodeByNumber(IReadOnlyList<Episode> episodes, int number)
        {
            foreach (Episode episode in episodes)
            {
                if (episode.EpisodeNumber == number)
                {
                    return episode;
                }
            }
            return null;
        }
    }
}

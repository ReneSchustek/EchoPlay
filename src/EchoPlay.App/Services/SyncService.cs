using EchoPlay.App.Helpers;
using EchoPlay.Core.Models;
using EchoPlay.Core.Scoring;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Matching;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Gleicht die lokale Bibliothek (Dateisystem) mit den in der Datenbank
    /// gespeicherten Serien und Episoden ab. Der Sync ist manuell auslösbar
    /// und idempotent — jeder Durchlauf überschreibt vorherige Ergebnisse.
    ///
    /// Die Hauptmethode <see cref="SyncAsync"/> orchestriert vier Phasen
    /// (Detection / Scan / Materialize-Series / Materialize-Episodes), jede
    /// in einer eigenen privaten Methode. Phasen-Records (siehe unten)
    /// koppeln den Datenfluss zwischen den Phasen.
    /// </summary>
    public sealed class SyncService : ISyncService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private readonly IScanEventService _scanEventService;
        private readonly CoverService _coverService;

        /// <summary>
        /// Initialisiert den SyncService.
        /// </summary>
        /// <param name="scopeFactory">Fabrik für DI-Scopes.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        /// <param name="scanEventService">
        /// Singleton-Dienst zur navigationsübergreifenden Benachrichtigung über Scan-Ereignisse.
        /// </param>
        /// <param name="coverService">Singleton-Dienst für Cover-Operationen über die CoverImages-Tabelle.</param>
        public SyncService(
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory,
            IScanEventService scanEventService,
            CoverService coverService)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("SyncService");
            _scanEventService = scanEventService;
            _coverService = coverService;
        }

        // ── Phasen-Records ──────────────────────────────────────────────────

        /// <summary>Output der Detection-Phase.</summary>
        internal sealed record DetectionResult(
            IReadOnlyList<string> SeriesFolders,
            HashSet<string> UsedFolderPaths,
            IReadOnlyList<Series> DbSeries);

        /// <summary>Eine vom Materialize-Series-Schritt vorbereitete Serie.</summary>
        internal sealed record SeriesPipelineEntry(
            Series Series,
            LocalScanResult ScanResult,
            bool IsNewlyCreated);

        /// <summary>Output der Materialize-Series-Phase.</summary>
        internal sealed record MaterializationResult(
            IReadOnlyList<SeriesPipelineEntry> Entries,
            int Matched,
            int Unmatched);

        /// <inheritdoc />
        public async Task<SyncResult> SyncAsync(
            IProgress<ScanProgress>? progress = null,
            bool forceImportAll = false,
            IProgress<Series>? onSeriesSynced = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.Sync);
            using IServiceScope scope = _scopeFactory.CreateScope();

            IServiceProvider sp = scope.ServiceProvider;
            IAppSettingsDataService settingsService = sp.GetRequiredService<IAppSettingsDataService>();
            AppSettings settings = await settingsService.GetAsync(cancellationToken);

            // Kein aktiver Bibliothekspfad — Sync nicht möglich
            if (!settings.LocalLibraryEnabled || string.IsNullOrWhiteSpace(settings.LocalLibraryRootPath))
            {
                return new SyncResult();
            }

            _logger.Info("Sync gestartet: {RootPath}", settings.LocalLibraryRootPath);

            _scanEventService.BeginScan();
            try
            {
                // Phase 1: Sofortige Erkennung bekannter Serien — emit-only
                DetectionResult detection = await RunDetectionPhaseAsync(
                    sp, settings, progress, onSeriesSynced, cancellationToken);

                // Phase 2: Vollständiger Filesystem-Scan
                IReadOnlyList<LocalScanResult> scanResults = await RunScanPhaseAsync(
                    sp, settings, progress, cancellationToken);

                // Phase 3: Serien anlegen oder mit DB matchen
                MaterializationResult materialization = await MaterializeSeriesAsync(
                    sp, scanResults, detection, settings, forceImportAll,
                    onSeriesSynced, cancellationToken);

                // Phase 4: Episoden, Tracks und Cover für jede Serie persistieren
                (int episodesUpdated, int tracksCreated) = await MaterializeEpisodesAsync(
                    sp, materialization, progress, cancellationToken);

                // Cover-Abgleich: lokale Episoden ohne Cover ggf. aus DB übernehmen
                await ApplyDbCoversToLocalEpisodesAsync(sp, cancellationToken);

                SyncResult result = new()
                {
                    SeriesMatched = materialization.Matched,
                    SeriesUnmatched = materialization.Unmatched,
                    EpisodesUpdated = episodesUpdated,
                    TracksCreated = tracksCreated
                };

                _logger.Info("Sync abgeschlossen: {Result}", result);
                return result;
            }
            finally
            {
                _scanEventService.EndScan();
            }
        }

        // ── Phase 1: Detection ─────────────────────────────────────────────────

        /// <summary>
        /// Liest die DB-Serien, ermittelt verwendete Ordnerpfade und durchsucht
        /// das Wurzelverzeichnis nach Serienordnern. Bekannte Serien werden
        /// sofort über <paramref name="onSeriesSynced"/> gemeldet.
        /// </summary>
        // Helper-Methode: Provider kommt aus dem aufrufenden Scope (kein Service-Locator im Konstruktor).
        internal async Task<DetectionResult> RunDetectionPhaseAsync(
            IServiceProvider sp,
            AppSettings settings,
            IProgress<ScanProgress>? progress,
            IProgress<Series>? onSeriesSynced,
            CancellationToken cancellationToken)
        {
            ISeriesDataService seriesService = sp.GetRequiredService<ISeriesDataService>();
            ILocalLibraryScanner scanner = sp.GetRequiredService<ILocalLibraryScanner>();

            IReadOnlyList<Series> dbSeries = await seriesService.GetAllAsync(cancellationToken);

            // Alle bereits genutzten Ordnerpfade vorberechnen — verhindert Duplikate.
            HashSet<string> usedFolderPaths = new(
                dbSeries
                    .Where(s => s.LocalFolderPath is not null)
                    .Select(s => s.LocalFolderPath!),
                StringComparer.OrdinalIgnoreCase);

            progress?.Report(new ScanProgress { StatusText = SafeResourceLoader.Get("ScanStatusDetectingFolders") });
            IReadOnlyList<string> seriesFolders = scanner.GetSeriesFolders(settings.LocalLibraryRootPath!);

            // Bekannte Serien sofort melden — der Nutzer sieht sie noch vor dem
            // eigentlichen Filesystem-Scan.
            foreach (string folder in seriesFolders)
            {
                Series? known = FindMatchingSeries(dbSeries, Path.GetFileName(folder));
                if (known is not null)
                {
                    onSeriesSynced?.Report(known);
                    _scanEventService.RaiseSeriesSynced(known);
                }
            }

            return new DetectionResult(seriesFolders, usedFolderPaths, dbSeries);
        }

        // ── Phase 2: Scan ──────────────────────────────────────────────────────

        /// <summary>
        /// Läuft den orchestrierten Filesystem-Scan (Phase 2-4 intern beim
        /// Orchestrator: Audiodateien zählen, Serien, Folgen, Tracks).
        /// </summary>
        // Helper-Methode: Provider kommt aus dem aufrufenden Scope (kein Service-Locator im Konstruktor).
        internal static async Task<IReadOnlyList<LocalScanResult>> RunScanPhaseAsync(
            IServiceProvider sp,
            AppSettings settings,
            IProgress<ScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            IScanOrchestrator orchestrator = sp.GetRequiredService<IScanOrchestrator>();
            return await orchestrator.ScanAsync(
                settings.LocalLibraryRootPath!,
                settings.EpisodeFolderPattern,
                progress,
                cancellationToken);
        }

        // ── Phase 3: Materialize Series ────────────────────────────────────────

        /// <summary>
        /// Entscheidet pro <see cref="LocalScanResult"/>, ob eine Serie neu angelegt,
        /// einer bestehenden Serie zugeordnet oder ignoriert wird. Sammelt das
        /// Ergebnis in einer Pipeline-Liste, mit der Phase 4 die Episoden
        /// effizient durchläuft.
        /// </summary>
        // Helper-Methode: Provider kommt aus dem aufrufenden Scope (kein Service-Locator im Konstruktor).
        internal async Task<MaterializationResult> MaterializeSeriesAsync(
            IServiceProvider sp,
            IReadOnlyList<LocalScanResult> scanResults,
            DetectionResult detection,
            AppSettings settings,
            bool forceImportAll,
            IProgress<Series>? onSeriesSynced,
            CancellationToken cancellationToken)
        {
            ISeriesDataService seriesService = sp.GetRequiredService<ISeriesDataService>();
            ILocalCoverService localCoverService = sp.GetRequiredService<ILocalCoverService>();

            List<SeriesPipelineEntry> entries = new(scanResults.Count);
            int matched = 0;
            int unmatched = 0;

            foreach (LocalScanResult scanResult in scanResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Series? matchedSeries = FindMatchingSeries(detection.DbSeries, scanResult.SeriesName);

                if (matchedSeries is null)
                {
                    bool shouldImport = (settings.AutoImportAfterScan || forceImportAll)
                        && !detection.UsedFolderPaths.Contains(scanResult.SeriesFolderPath);

                    if (!shouldImport)
                    {
                        unmatched++;
                        continue;
                    }

                    // Neue Serie anlegen
                    Series importedSeries = new()
                    {
                        Title = scanResult.SeriesName,
                        LocalFolderPath = scanResult.SeriesFolderPath,
                        IsSubscribed = true
                    };

                    await seriesService.AddAsync(importedSeries, cancellationToken);
                    _ = detection.UsedFolderPaths.Add(scanResult.SeriesFolderPath);

                    // Cover für die neue Serie auflösen, falls noch keins vorhanden
                    byte[]? coverData = await ResolveCoverSafelyAsync(
                        localCoverService, scanResult.SeriesFolderPath, coverImageUrl: null, cancellationToken);
                    if (coverData is not null && !await _coverService.HasSeriesCoverAsync(importedSeries.Id, cancellationToken))
                    {
                        await _coverService.SetSeriesCoverAsync(importedSeries.Id, coverData, cancellationToken: cancellationToken);
                    }

                    onSeriesSynced?.Report(importedSeries);
                    _scanEventService.RaiseSeriesSynced(importedSeries);

                    entries.Add(new SeriesPipelineEntry(importedSeries, scanResult, IsNewlyCreated: true));
                    matched++;
                    continue;
                }

                // Vorhandene Serie: Ordnerpfad aktualisieren und Cover ggf. setzen
                matched++;
                matchedSeries.LocalFolderPath = scanResult.SeriesFolderPath;

                if (!await _coverService.HasSeriesCoverAsync(matchedSeries.Id, cancellationToken))
                {
                    byte[]? coverData = await ResolveCoverSafelyAsync(
                        localCoverService, scanResult.SeriesFolderPath, matchedSeries.CoverImageUrl, cancellationToken);
                    if (coverData is not null)
                    {
                        await _coverService.SetSeriesCoverAsync(matchedSeries.Id, coverData, cancellationToken: cancellationToken);
                    }
                }

                await seriesService.UpdateAsync(matchedSeries, cancellationToken);
                _scanEventService.RaiseSeriesSynced(matchedSeries);

                entries.Add(new SeriesPipelineEntry(matchedSeries, scanResult, IsNewlyCreated: false));
            }

            return new MaterializationResult(entries, matched, unmatched);
        }

        // ── Phase 4: Materialize Episodes ─────────────────────────────────────

        /// <summary>
        /// Persistiert die Episoden für jede materialisierte Serie. Neu angelegte
        /// Serien bekommen einen Batch-Import; bestehende werden Episode-für-Episode
        /// gegen die DB abgeglichen und Tracks aktualisiert.
        /// </summary>
        // Helper-Methode: Provider kommt aus dem aufrufenden Scope (kein Service-Locator im Konstruktor).
        internal async Task<(int EpisodesUpdated, int TracksCreated)> MaterializeEpisodesAsync(
            IServiceProvider sp,
            MaterializationResult materialization,
            IProgress<ScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            IEpisodeDataService episodeService = sp.GetRequiredService<IEpisodeDataService>();
            ILocalTrackDataService trackService = sp.GetRequiredService<ILocalTrackDataService>();
            IMp3MetadataReader metadataReader = sp.GetRequiredService<IMp3MetadataReader>();
            ITrackMatcher trackMatcher = sp.GetRequiredService<ITrackMatcher>();

            int episodesUpdated = 0;
            int tracksCreated = 0;
            int processedSeries = 0;

            foreach (SeriesPipelineEntry entry in materialization.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedSeries++;

                progress?.Report(new ScanProgress
                {
                    StatusText = $"Synchronisiere \"{entry.ScanResult.SeriesName}\" …",
                    DetailText = $"{processedSeries} / {materialization.Entries.Count} Serien",
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
            }

            return (episodesUpdated, tracksCreated);
        }

        // ── Helper-Methoden ───────────────────────────────────────────────────

        /// <summary>
        /// Legt Episoden und lokale Tracks für eine neu auto-importierte Serie an.
        /// Episoden ohne geparste Nummer erhalten eine sequenzielle Nummer (1, 2, 3 …).
        /// </summary>
        private async Task<(int Episodes, int Tracks)> ImportEpisodesAsync(
            Guid seriesId,
            IReadOnlyList<LocalEpisodeScan> episodeScans,
            IEpisodeDataService episodeService,
            ILocalTrackDataService trackService,
            IMp3MetadataReader metadataReader,
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
            IMp3MetadataReader metadataReader,
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

        private static Series? FindMatchingSeries(IReadOnlyList<Series> series, string folderName)
        {
            string normalizedFolder = HoerspielTextNormalizer.Normalize(folderName);
            foreach (Series s in series)
            {
                if (HoerspielTextNormalizer.Normalize(s.Title) == normalizedFolder)
                {
                    return s;
                }
            }
            return null;
        }

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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Auflösung pro Ordner: IO-/HTTP-/Bild-Dekodier-Fehler dürfen den Scan-Vorgang nicht stoppen; ein fehlendes Cover wird zu 'null', damit die Episode ohne Cover angelegt wird.")]
        private async Task<byte[]?> ResolveCoverSafelyAsync(
            ILocalCoverService coverService,
            string folderPath,
            string? coverImageUrl,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await coverService.ResolveAsync(folderPath, coverImageUrl, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Warning("Cover-Auflösung fehlgeschlagen für '{FolderPath}': {Reason}", folderPath, ex.Message);
                return null;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Optionaler Cover-Copy-Schritt nach Scan: DB-/IO-Fehler beim Uebernehmen von Covern aus anderen Episoden oder beim Schreiben von cover.jpg dürfen den Scan-Abschluss nicht blockieren.")]
        // Helper-Methode: Provider kommt aus dem aufrufenden Scope (kein Service-Locator im Konstruktor).
        private async Task ApplyDbCoversToLocalEpisodesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            try
            {
                ICoverCopyService coverCopy = serviceProvider.GetRequiredService<ICoverCopyService>();
                ISeriesDataService seriesService = serviceProvider.GetRequiredService<ISeriesDataService>();
                IEpisodeDataService episodeService = serviceProvider.GetRequiredService<IEpisodeDataService>();
                ICoverImageDataService coverImageService = serviceProvider.GetRequiredService<ICoverImageDataService>();

                IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync(cancellationToken);
                int totalCopied = 0;

                foreach (Series series in allSeries)
                {
                    if (string.IsNullOrEmpty(series.LocalFolderPath)) continue;
                    int copied = await coverCopy.CopyFromMatchingEpisodesAsync(series.Id, cancellationToken);
                    totalCopied += copied;
                }

                if (totalCopied > 0)
                {
                    _logger.Info("Cover-Abgleich nach Scan: {TotalCopied} Cover aus DB übernommen.", totalCopied);
                }

                foreach (Series series in allSeries)
                {
                    if (string.IsNullOrEmpty(series.LocalFolderPath)) continue;

                    IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id, cancellationToken);

                    List<Episode> localEpisodes = [];
                    foreach (Episode episode in episodes)
                    {
                        if (!string.IsNullOrEmpty(episode.LocalFolderPath))
                        {
                            localEpisodes.Add(episode);
                        }
                    }

                    if (localEpisodes.Count == 0) continue;

                    List<Guid> ids = localEpisodes.Select(e => e.Id).ToList();
                    IReadOnlyDictionary<Guid, byte[]> covers =
                        await coverImageService.GetImageDataByEntitiesAsync(
                            CoverEntityTypes.Episode, ids, cancellationToken);

                    foreach (Episode episode in localEpisodes)
                    {
                        if (!covers.TryGetValue(episode.Id, out byte[]? coverData)) continue;

                        string coverPath = Path.Combine(
                            episode.LocalFolderPath!, Core.CoverConstants.CoverFileName);

                        if (File.Exists(coverPath)) continue;

                        try
                        {
                            await File.WriteAllBytesAsync(coverPath, coverData, cancellationToken);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            _logger.Debug(() => $"Cover-Datei konnte nicht geschrieben werden: {coverPath} – {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("Cover-Abgleich nach Scan fehlgeschlagen: {Reason}", ex.Message);
            }
        }
    }
}

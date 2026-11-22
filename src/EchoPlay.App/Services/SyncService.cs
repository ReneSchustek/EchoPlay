using EchoPlay.Core.Http;
using EchoPlay.App.Helpers;
using EchoPlay.Core.Scoring;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Gleicht die lokale Bibliothek (Dateisystem) mit den in der Datenbank
    /// gespeicherten Serien und Episoden ab. Der Sync ist manuell auslösbar
    /// und idempotent; jeder Durchlauf überschreibt vorherige Ergebnisse.
    ///
    /// Die Hauptmethode <see cref="SyncAsync"/> führt vier Phasen nacheinander aus:
    /// Erkennen, Einlesen, Serien anlegen und Folgen anlegen. Die ersten drei stehen als
    /// interne Methoden hier, die vierte liegt in <see cref="LocalEpisodeMaterializer"/>.
    /// Danach folgt der Cover-Abgleich. Die Records unten tragen die Daten von Phase zu Phase.
    /// </summary>
    public sealed class SyncService : ISyncService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private readonly IScanEventService _scanEventService;
        private readonly ICoverService _coverService;

        // Bringt Folgen und Spuren in die Datenbank, die vierte Phase des Einlesens.
        private readonly LocalEpisodeMaterializer _episodeMaterializer;

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
            ICoverService coverService)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("SyncService");
            _scanEventService = scanEventService;
            _coverService = coverService;
            _episodeMaterializer = new LocalEpisodeMaterializer(_logger, scanEventService);
        }

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
            SyncImportScope importScope = SyncImportScope.AsConfigured,
            IProgress<Series>? onSeriesSynced = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.Sync);
            using IServiceScope scope = _scopeFactory.CreateScope();

            IServiceProvider sp = scope.ServiceProvider;
            IAppSettingsDataService settingsService = sp.GetRequiredService<IAppSettingsDataService>();
            AppSettings settings = await settingsService.GetAsync(cancellationToken);

            // Kein aktiver Bibliothekspfad, Sync nicht möglich
            if (!settings.LocalLibraryEnabled || string.IsNullOrWhiteSpace(settings.LocalLibraryRootPath))
            {
                return new SyncResult();
            }

            _logger.Info("Sync gestartet: {RootPath}", EchoPlay.Core.Logging.PathRedactor.Redact(settings.LocalLibraryRootPath));

            _scanEventService.BeginScan();
            try
            {
                SyncResult result = await RunPhasesAsync(
                    sp, settings, progress, importScope, onSeriesSynced, cancellationToken);

                _logger.Info("Sync abgeschlossen: {Result}", result);
                return result;
            }
            finally
            {
                _scanEventService.EndScan();
            }
        }

        /// <summary>
        /// Führt die vier Phasen des Abgleichs nacheinander aus und danach den Cover-Abgleich.
        /// </summary>
        private async Task<SyncResult> RunPhasesAsync(
            IServiceProvider sp,
            AppSettings settings,
            IProgress<ScanProgress>? progress,
            SyncImportScope importScope,
            IProgress<Series>? onSeriesSynced,
            CancellationToken cancellationToken)
        {
            // Phase 1: Sofortige Erkennung bekannter Serien (emit-only)
            DetectionResult detection = await RunDetectionPhaseAsync(
                sp, settings, progress, onSeriesSynced, cancellationToken);

            // Phase 2: Vollständiger Filesystem-Scan
            IReadOnlyList<LocalScanResult> scanResults = await RunScanPhaseAsync(
                sp, settings, progress, cancellationToken);

            // Phase 3: Serien anlegen oder mit DB matchen
            MaterializationResult materialization = await MaterializeSeriesAsync(
                sp, scanResults, detection, settings, importScope,
                onSeriesSynced, cancellationToken);

            // Phase 4: Folgen und Spuren jeder Serie speichern; die Cover folgen im Abgleich danach.
            (int episodesUpdated, int tracksCreated) = await _episodeMaterializer.MaterializeEpisodesAsync(
                sp, materialization, progress, cancellationToken);

            // Cover-Abgleich: fehlende Folgen-Cover aus passenden Folgen der Datenbank
            // übernehmen und, wenn eingeschaltet, als cover.jpg in die Folgenordner legen.
            await ApplyDbCoversToLocalEpisodesAsync(sp, settings, cancellationToken);

            return new SyncResult
            {
                SeriesMatched = materialization.Matched,
                SeriesUnmatched = materialization.Unmatched,
                EpisodesUpdated = episodesUpdated,
                TracksCreated = tracksCreated
            };
        }

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

            // Alle bereits genutzten Ordnerpfade vorberechnen, das verhindert Duplikate.
            HashSet<string> usedFolderPaths = new(
                dbSeries
                    .Select(s => s.LocalFolderPath)
                    .OfType<string>(),
                StringComparer.OrdinalIgnoreCase);

            progress?.Report(new ScanProgress { StatusText = SafeResourceLoader.Get("ScanStatusDetectingFolders") });
            IReadOnlyList<string> seriesFolders = scanner.GetSeriesFolders(settings.LocalLibraryRootPath!); // SyncAsync bricht ohne Wurzelpfad vorher ab

            // Bekannte Serien sofort melden. Der Nutzer sieht sie noch vor dem
            // eigentlichen Filesystem-Scan.
            foreach (string folder in seriesFolders)
            {
                Series? known = FindMatchingSeries(dbSeries, Path.GetFileName(folder));
                if (known is not null)
                {
                    onSeriesSynced?.Report(known);
                    _scanEventService.NotifySeriesSynced(known);
                }
            }

            return new DetectionResult(seriesFolders, usedFolderPaths, dbSeries);
        }

        /// <summary>
        /// Liest das Dateisystem über den <see cref="IScanOrchestrator"/> ein. Dessen
        /// Fortschritt zeigt eigene vier Phasen: Vorbereitung, Serien, Folgen, Spuren.
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
                settings.LocalLibraryRootPath!, // SyncAsync bricht ohne Wurzelpfad vorher ab
                settings.EpisodeFolderPattern,
                progress,
                cancellationToken);
        }

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
            SyncImportScope importScope,
            IProgress<Series>? onSeriesSynced,
            CancellationToken cancellationToken)
        {
            IWatchedTitleDataService watchedTitleService = sp.GetRequiredService<IWatchedTitleDataService>();

            // Früher überwachte Titel: gibt neu eingelesenen Serien ihre Überwachung zurück,
            // nachdem die Mediathek geleert wurde (Series-Zeilen sind dabei physisch weg).
            SeriesStores stores = new(
                sp.GetRequiredService<ISeriesDataService>(),
                sp.GetRequiredService<ILocalCoverService>(),
                await watchedTitleService.GetAllAsync(cancellationToken));

            List<SeriesPipelineEntry> entries = new(scanResults.Count);
            int unmatched = 0;
            foreach (LocalScanResult scanResult in scanResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Series? matchedSeries = FindMatchingSeries(detection.DbSeries, scanResult.SeriesName);
                if (matchedSeries is not null)
                {
                    await UpdateMatchedSeriesAsync(matchedSeries, scanResult, stores, cancellationToken);
                    entries.Add(new SeriesPipelineEntry(matchedSeries, scanResult, IsNewlyCreated: false));
                    continue;
                }

                if (!(settings.AutoImportAfterScan || importScope == SyncImportScope.AllFolders) || detection.UsedFolderPaths.Contains(scanResult.SeriesFolderPath))
                {
                    unmatched++;
                    continue;
                }

                Series importedSeries = await ImportNewSeriesAsync(scanResult, detection, stores, cancellationToken);
                onSeriesSynced?.Report(importedSeries);
                _scanEventService.NotifySeriesSynced(importedSeries);
                entries.Add(new SeriesPipelineEntry(importedSeries, scanResult, IsNewlyCreated: true));
            }

            return new MaterializationResult(entries, entries.Count, unmatched);
        }

        /// <summary>Legt eine neu gefundene Serie samt Cover an.</summary>
        private async Task<Series> ImportNewSeriesAsync(
            LocalScanResult scanResult, DetectionResult detection, SeriesStores stores, CancellationToken cancellationToken)
        {
            Series importedSeries = new()
            {
                Title = scanResult.SeriesName,
                LocalFolderPath = scanResult.SeriesFolderPath,
                IsSubscribed = true,
                IsWatched = stores.WatchedTitles.Contains(
                    EchoPlay.Core.Scoring.HoerspielTextNormalizer.Normalize(scanResult.SeriesName))
            };

            await stores.Series.AddAsync(importedSeries, cancellationToken);
            _ = detection.UsedFolderPaths.Add(scanResult.SeriesFolderPath);

            // Cover für die neue Serie auflösen, falls noch keins vorhanden
            byte[]? coverData = await ResolveCoverSafelyAsync(
                stores.Covers, scanResult.SeriesFolderPath, coverImageUrl: null, cancellationToken);
            if (coverData is not null && !await _coverService.HasSeriesCoverAsync(importedSeries.Id, cancellationToken))
            {
                await _coverService.SetSeriesCoverAsync(importedSeries.Id, coverData, cancellationToken: cancellationToken);
            }

            return importedSeries;
        }

        /// <summary>Vorhandene Serie: Ordnerpfad aktualisieren und Cover ggf. setzen.</summary>
        private async Task UpdateMatchedSeriesAsync(
            Series matchedSeries, LocalScanResult scanResult, SeriesStores stores, CancellationToken cancellationToken)
        {
            matchedSeries.LocalFolderPath = scanResult.SeriesFolderPath;

            if (!await _coverService.HasSeriesCoverAsync(matchedSeries.Id, cancellationToken))
            {
                byte[]? coverData = await ResolveCoverSafelyAsync(
                    stores.Covers, scanResult.SeriesFolderPath, matchedSeries.CoverImageUrl, cancellationToken);
                if (coverData is not null)
                {
                    await _coverService.SetSeriesCoverAsync(matchedSeries.Id, coverData, cancellationToken: cancellationToken);
                }
            }

            await stores.Series.UpdateAsync(matchedSeries, cancellationToken);
            _scanEventService.NotifySeriesSynced(matchedSeries);
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Auflösung je Serienordner: IO-/HTTP-/Bild-Dekodier-Fehler dürfen den Scan-Vorgang nicht stoppen; ein fehlendes Cover wird zu 'null', damit die Serie ohne Cover angelegt wird.")]
        private async Task<byte[]?> ResolveCoverSafelyAsync(
            ILocalCoverService coverService,
            string folderPath,
            string? coverImageUrl,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await coverService.ResolveAsync(folderPath, WebAddress.TryParse(coverImageUrl), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ein Abbruch des Aufrufers ist kein Fehlschlag und gehört nach oben.
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning("Cover-Auflösung fehlgeschlagen für '{FolderPath}': {Reason}", EchoPlay.Core.Logging.PathRedactor.Redact(folderPath), ex.Message);
                return null;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Optionaler Cover-Copy-Schritt nach Scan: DB-/IO-Fehler beim Übernehmen von Covern aus anderen Episoden oder beim Schreiben von cover.jpg dürfen den Scan-Abschluss nicht blockieren.")]
        // Helper-Methode: Provider kommt aus dem aufrufenden Scope (kein Service-Locator im Konstruktor).
        private async Task ApplyDbCoversToLocalEpisodesAsync(
            IServiceProvider serviceProvider,
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            try
            {
                IReadOnlyList<Series> allSeries = await serviceProvider.GetRequiredService<ISeriesDataService>().GetAllAsync(cancellationToken);
                List<Series> localSeries = [.. allSeries.Where(series => !string.IsNullOrEmpty(series.LocalFolderPath))];

                await CopyCoversFromDatabaseAsync(serviceProvider.GetRequiredService<ICoverCopyService>(), localSeries, cancellationToken);

                // Wer das Speichern im Ordner abgeschaltet hat, will keine Dateien in seiner
                // Sammlung; dieselbe Einstellung gilt auch beim Setzen eines Covers von Hand.
                if (!settings.SaveCoverToDirectory)
                {
                    return;
                }

                foreach (Series series in localSeries)
                {
                    await WriteEpisodeCoverFilesAsync(serviceProvider, series, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ein Abbruch des Aufrufers ist kein Fehlschlag und gehört nach oben.
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning("Cover-Abgleich nach Scan fehlgeschlagen: {Reason}", ex.Message);
            }
        }

        /// <summary>Übernimmt für jede lokale Serie die Cover gleicher Folgen aus der Datenbank.</summary>
        private async Task CopyCoversFromDatabaseAsync(ICoverCopyService coverCopy, List<Series> localSeries, CancellationToken cancellationToken)
        {
            int totalCopied = 0;
            foreach (Series series in localSeries)
            {
                totalCopied += await coverCopy.CopyFromMatchingEpisodesAsync(series.Id, cancellationToken);
            }

            if (totalCopied > 0)
            {
                _logger.Info("Cover-Abgleich nach Scan: {TotalCopied} Cover aus DB übernommen.", totalCopied);
            }
        }

        /// <summary>
        /// Schreibt für die lokalen Folgen einer Serie eine <c>cover.jpg</c> aus der Datenbank,
        /// wo noch keine liegt. Eine Datei, die sich nicht schreiben lässt, wird übergangen.
        /// </summary>
        private async Task WriteEpisodeCoverFilesAsync(IServiceProvider serviceProvider, Series series, CancellationToken cancellationToken)
        {
            IReadOnlyList<Episode> episodes = await serviceProvider.GetRequiredService<IEpisodeDataService>().GetBySeriesIdAsync(series.Id, cancellationToken);
            List<Episode> localEpisodes = [.. episodes.Where(episode => !string.IsNullOrEmpty(episode.LocalFolderPath))];
            if (localEpisodes.Count == 0) return;

            IReadOnlyDictionary<Guid, byte[]> covers = await serviceProvider.GetRequiredService<ICoverImageDataService>()
                .GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, localEpisodes.Select(e => e.Id).ToArray(), cancellationToken);

            foreach (Episode episode in localEpisodes)
            {
                if (!covers.TryGetValue(episode.Id, out byte[]? coverData)) continue;

                string coverPath = Path.Combine(episode.LocalFolderPath!, Core.CoverConstants.CoverFileName); // localEpisodes hat nur Folgen mit Ordner
                if (File.Exists(coverPath)) continue;

                try
                {
                    await File.WriteAllBytesAsync(coverPath, coverData, cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.Debug(() => $"Cover-Datei konnte nicht geschrieben werden: {EchoPlay.Core.Logging.PathRedactor.Redact(coverPath)} – {ex.Message}");
                }
            }
        }

        /// <summary>Die Dienste und Merkmale eines Materialisierungs-Durchgangs.</summary>
        /// <param name="Series">Serien lesen und schreiben.</param>
        /// <param name="Covers">Löst Cover aus dem Serienordner auf.</param>
        /// <param name="WatchedTitles">Früher überwachte Titel, normalisiert.</param>
        private sealed record SeriesStores(ISeriesDataService Series, ILocalCoverService Covers, IReadOnlySet<string> WatchedTitles);
    }
}

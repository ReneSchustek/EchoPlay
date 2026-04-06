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
    /// Hintergrund-Service der fehlende Cover automatisch nachlädt.
    /// Läuft beim App-Start einmalig und danach periodisch (alle 30 Minuten).
    /// Der Nutzer merkt nichts davon – Cover erscheinen einfach irgendwann.
    ///
    /// Ablauf pro Durchlauf:
    /// 1. Alle lokalen Episoden mit Ordner aber ohne Cover in CoverImages ermitteln
    /// 2. Cover aus dem Dateisystem laden (cover.jpg / ID3-Tags)
    /// 3. In CoverImages speichern
    /// </summary>
    public sealed class BackgroundCoverService : IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly CoverService _coverService;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;

        /// <summary>
        /// Intervall zwischen periodischen Durchläufen.
        /// </summary>
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);


        /// <summary>
        /// Initialisiert den Background-Cover-Service.
        /// </summary>
        public BackgroundCoverService(
            IServiceScopeFactory scopeFactory,
            CoverService coverService,
            ILoggerFactory loggerFactory)
        {
            _scopeFactory = scopeFactory;
            _coverService = coverService;
            _logger = loggerFactory.CreateLogger("BackgroundCoverService");
        }

        /// <summary>
        /// Startet den Hintergrund-Task. Darf nur einmal aufgerufen werden.
        /// </summary>
        public void Start()
        {
            if (_backgroundTask is not null) return;

            _cts = new CancellationTokenSource();
            _backgroundTask = RunAsync(_cts.Token);
        }

        /// <summary>
        /// Stoppt den Hintergrund-Task sauber.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
        }

        /// <summary>
        /// Führt einen einzelnen synchronen Durchlauf aus: lädt alle fehlenden lokalen Cover in die DB.
        /// Wird vom StartupValidator aufgerufen, wenn der Cache geleert wurde und Cover
        /// während des Splashs neu aufgebaut werden müssen.
        /// </summary>
        /// <returns>Anzahl der geladenen Cover.</returns>
        public async Task<int> RunOnceAsync()
        {
            using CancellationTokenSource cts = new();
            return await LoadMissingLocalCoversAsync(cts.Token);
        }

        /// <summary>
        /// Hauptschleife: einmaliger Durchlauf beim Start, dann periodisch.
        /// </summary>
        private async Task RunAsync(CancellationToken ct)
        {
            // Kurz warten, damit die App vollständig initialisiert ist
            await Task.Delay(3000, ct).ConfigureAwait(false);

            // Einmaliger Cleanup: Cover von Online-Episoden entfernen, die aus der
            // Migration oder einem früheren Fehl-Match stammen. Danach werden die
            // richtigen Cover aus den lokalen Episoden neu kopiert.
            try
            {
                int removed = await CleanupOnlineEpisodeCoversAsync();

                if (removed > 0)
                {
                    _logger.Info($"Cleanup: {removed} fehlerhafte Online-Episoden-Cover entfernt.");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Cleanup fehlgeschlagen: {ex.Message}");
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    int loaded = await LoadMissingLocalCoversAsync(ct);

                    if (loaded > 0)
                    {
                        _logger.Info($"Hintergrund: {loaded} lokale Episoden-Cover in DB gespeichert.");
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
                    await Task.Delay(Interval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
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
        /// Entfernt Cover von Online-Episoden, damit sie aus den lokalen Quellen
        /// neu kopiert werden können. Betrifft nur Episoden von Online-importierten
        /// Serien, die kein eigenes lokales Verzeichnis haben.
        /// </summary>
        private async Task<int> CleanupOnlineEpisodeCoversAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ICoverImageDataService coverImageService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            return await coverImageService.DeleteOnlineEpisodeCoversAsync();
        }

        /// <summary>
        /// Sucht lokale Episoden mit Ordner aber ohne Cover in CoverImages
        /// und lädt die Cover aus dem Dateisystem (cover.jpg / ID3-Tags).
        /// </summary>
        private async Task<int> LoadMissingLocalCoversAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider
                .GetRequiredService<IEpisodeDataService>();
            ILocalTrackDataService trackService = scope.ServiceProvider
                .GetRequiredService<ILocalTrackDataService>();
            ILocalCoverLoader coverLoader = scope.ServiceProvider
                .GetRequiredService<ILocalCoverLoader>();
            ICoverImageDataService coverImageService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            // Alle Episoden mit lokalem Ordner laden (nur Metadaten, kein Blob)
            IReadOnlyList<Series> allSeries;

            using (IServiceScope seriesScope = _scopeFactory.CreateScope())
            {
                ISeriesDataService seriesService = seriesScope.ServiceProvider
                    .GetRequiredService<ISeriesDataService>();
                allSeries = await seriesService.GetAllAsync();
            }

            int loaded = 0;

            foreach (Series series in allSeries)
            {
                if (ct.IsCancellationRequested) break;

                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id);

                // Nur Episoden mit lokalem Ordner und ohne Cover in CoverImages
                List<Episode> candidates = [];

                foreach (Episode episode in episodes)
                {
                    if (string.IsNullOrEmpty(episode.LocalFolderPath)) continue;
                    candidates.Add(episode);
                }

                if (candidates.Count == 0) continue;

                // Batch-Prüfung: welche haben schon ein Cover?
                List<Guid> candidateIds = candidates.Select(e => e.Id).ToList();
                IReadOnlyDictionary<Guid, byte[]> existing =
                    await coverImageService.GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, candidateIds);

                foreach (Episode episode in candidates)
                {
                    if (ct.IsCancellationRequested) break;
                        if (existing.ContainsKey(episode.Id)) continue;

                    // Erste Track-Datei für ID3-Fallback ermitteln
                    string? firstTrackPath = null;
                    IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episode.Id);

                    if (tracks.Count > 0)
                    {
                        firstTrackPath = tracks[0].FilePath;
                    }

                    // Cover aus Dateisystem laden
                    byte[]? coverBytes = await coverLoader.LoadAsync(
                        episode.LocalFolderPath, firstTrackPath);

                    if (coverBytes is not null)
                    {
                        await _coverService.SetEpisodeCoverAsync(episode.Id, coverBytes);
                        loaded++;
                    }
                }
            }

            return loaded;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}

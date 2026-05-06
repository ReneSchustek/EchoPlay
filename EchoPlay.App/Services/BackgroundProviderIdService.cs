using EchoPlay.Core.Abstractions;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
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
    /// Hintergrund-Dienst, der fehlende Provider-IDs (SpotifyAlbumId, AppleMusicAlbumId)
    /// für bestehende Serien und Episoden nachträglich über die jeweilige Provider-API ergänzt.
    /// Läuft einmal beim App-Start und danach stündlich. Fehler blockieren nicht andere Serien.
    /// </summary>

    internal sealed class BackgroundProviderIdService : IDisposable
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISpotifyCredentialStore _credentialStore;
        private readonly IHostRateLimiter _rateLimiter;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cts;
        private Task? _runningTask;

        public BackgroundProviderIdService(
            IServiceScopeFactory scopeFactory,
            ISpotifyCredentialStore credentialStore,
            IHostRateLimiter rateLimiter,
            ILoggerFactory loggerFactory)
        {
            _scopeFactory = scopeFactory;
            _credentialStore = credentialStore;
            _rateLimiter = rateLimiter;
            _logger = loggerFactory.CreateLogger("BackgroundProviderIdService");
        }

        /// <summary>Startet den periodischen Enrichment-Lauf. Idempotent — mehrfacher Aufruf ist no-op.</summary>

        public void Start()
        {
            if (_runningTask is not null) return;

            _cts = new CancellationTokenSource();
            // Task.Run entkoppelt von Aufrufer-SynchronizationContext und macht den
            // Task als Referenz greifbar, damit StopAsync mit Timeout auf das Ende warten kann.
            _runningTask = Task.Run(() => RunLoopAsync(_cts.Token));
        }

        /// <summary>
        /// Stoppt den Hintergrund-Dienst und wartet mit Timeout auf das Ende der laufenden Iteration.
        /// Bei Timeout wird eine Warnung geloggt, aber nicht geworfen.
        /// </summary>
        /// <param name="timeout">Maximale Wartezeit.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (_cts is null || _runningTask is null) return;

            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await _runningTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Erwartet: Iteration hat den CancellationToken sauber beobachtet.
            }
            catch (TimeoutException)
            {
                _logger.Warning($"BackgroundProviderIdService: Iteration hat Timeout ({timeout.TotalSeconds:F1}s) überschritten und wird hart abgebrochen.");
            }

            _cts.Dispose();
            _cts = null;
            _runningTask = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _runningTask = null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Enrichment-Schleife: HTTP-Fehler (Spotify/AppleMusic), DB-Concurrency-Fehler oder Provider-Timeouts dürfen die Schleife nicht beenden; Fehler werden als Error geloggt und der nächste Intervall-Zyklus fährt fort.")]
        private async Task RunLoopAsync(CancellationToken ct)
        {
            // Erster Lauf nach kurzem Delay (App-Start nicht blockieren)
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error("Fehler im ID-Enrichment-Lauf.", ex);
                }

                await Task.Delay(Interval, ct).ConfigureAwait(false);
            }
        }

        /// <summary>Ein einzelner Enrichment-Durchlauf.</summary>

        /// <param name="ct">Parameter <c>ct</c>.</param>
        public async Task RunOnceAsync(CancellationToken ct = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            AppSettings settings = await settingsService.GetAsync(ct);

            ProviderType provider = settings.ActiveProvider;
            if (provider == ProviderType.None)
            {
                return;
            }

            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IReadOnlyList<Series> subscribedSeries = await seriesService.GetSubscribedAsync(ct);

            // Apple-Music-IDs ergänzen (iTunes Search API, ohne Credentials)
            if (provider.Includes(ProviderType.AppleMusic))
            {
                await EnrichAppleMusicIdsAsync(subscribedSeries, episodeService, ct);
            }

            // Spotify-IDs nur mit Credentials
            if (provider.Includes(ProviderType.Spotify) && _credentialStore.HasCredentials)
            {
                _logger.Info("Spotify-ID-Enrichment übersprungen (noch nicht implementiert, kommt mit Spotify-Search-Integration).");
            }
        }

        private async Task EnrichAppleMusicIdsAsync(
            IReadOnlyList<Series> series,
            IEpisodeDataService episodeService,
            CancellationToken ct)
        {
            int enrichedEpisodes = 0;

            foreach (Series s in series)
            {
                if (ct.IsCancellationRequested) break;

                // Episoden ohne AppleMusicAlbumId, aber mit ProviderUrl (iTunes-Import)
                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(s.Id, ct);
                List<Episode> missing = episodes
                    .Where(e => e.AppleMusicAlbumId is null && e.ProviderUrl is not null)
                    .ToList();

                foreach (Episode episode in missing)
                {
                    if (ct.IsCancellationRequested) break;

                    // CollectionId aus der ProviderUrl extrahieren (iTunes-Format: .../id{CollectionId})
                    string? collectionId = ExtractITunesCollectionId(episode.ProviderUrl);
                    if (collectionId is not null)
                    {
                        episode.AppleMusicAlbumId = collectionId;
                        await episodeService.UpdateAsync(episode, ct);
                        enrichedEpisodes++;
                    }
                }
            }

            if (enrichedEpisodes > 0)
            {
                _logger.Info($"{enrichedEpisodes} Episoden mit Apple-Music-Album-ID ergänzt.");
            }
        }

        /// <summary>
        /// Extrahiert die CollectionId aus einer iTunes-URL.
        /// Format: https://music.apple.com/de/album/.../{CollectionId} oder .../id{CollectionId}
        /// </summary>

        /// <param name="url">Parameter <c>url</c>.</param>
        internal static string? ExtractITunesCollectionId(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            // Letztes Pfadsegment, das nur aus Ziffern besteht oder mit "id" beginnt
            string[] segments = url.Split('/');
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                string segment = segments[i].Split('?')[0]; // Query-Parameter abschneiden
                if (segment.StartsWith("id", StringComparison.OrdinalIgnoreCase))
                {
                    return segment[2..]; // "id" abschneiden
                }
                if (segment.Length > 0 && segment.All(char.IsDigit))
                {
                    return segment;
                }
            }

            return null;
        }
    }
}

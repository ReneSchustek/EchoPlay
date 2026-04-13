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

        /// <summary>Startet den periodischen Enrichment-Lauf.</summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
            _ = RunLoopAsync(_cts.Token);
        }

        /// <summary>Stoppt den Hintergrund-Dienst.</summary>
        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Stop();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Enrichment-Schleife: HTTP-Fehler (Spotify/AppleMusic), DB-Concurrency-Fehler oder Provider-Timeouts duerfen die Schleife nicht beenden; Fehler werden als Error geloggt und der naechste Intervall-Zyklus faehrt fort.")]
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
        public async Task RunOnceAsync(CancellationToken ct = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            AppSettings settings = await settingsService.GetAsync();

            ProviderType provider = settings.ActiveProvider;
            if (provider == ProviderType.None)
            {
                return;
            }

            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            IReadOnlyList<Series> subscribedSeries = await seriesService.GetSubscribedAsync();

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
                IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(s.Id);
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
                        await episodeService.UpdateAsync(episode);
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

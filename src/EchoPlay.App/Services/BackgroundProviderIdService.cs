using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Hintergrund-Dienst, der fehlende Apple-Music-Album-Kennungen an Folgen nachträgt. Er
    /// liest sie aus der gespeicherten Anbieter-Adresse der Folge, ganz ohne Netzabfrage.
    /// Läuft eine halbe Minute nach dem Start und danach stündlich.
    /// </summary>
    /// <remarks>
    /// Spotify-Kennungen ergänzt er nicht: Sie kommen beim Import mit, und eine Suche über die
    /// Spotify-Web-API ist nicht gebaut.
    /// </remarks>
    internal sealed class BackgroundProviderIdService : IDisposable
    {
        // Stündlich genügt: Neue Folgen ohne Kennung entstehen nur durch einen Import, und die
        // Arbeit je Durchlauf ist ein Durchsehen der Datenbank ohne Netz.
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

        // Bis dahin sind Startbild und erstes Zeichnen der Startseite durch; der Durchlauf
        // konkurriert dann nicht mehr mit ihnen um die Datenbank.
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cts;
        private Task? _runningTask;

        /// <summary>
        /// Initialisiert den Dienst.
        /// </summary>
        /// <param name="scopeFactory">Öffnet je Durchlauf einen eigenen Datenbankbereich.</param>
        /// <param name="loggerFactory">Fabrik für den Protokollkanal.</param>
        public BackgroundProviderIdService(
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory)
        {
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("BackgroundProviderIdService");
        }

        /// <summary>Startet den periodischen Enrichment-Lauf. Idempotent: Ein weiterer Aufruf bewirkt nichts.</summary>
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
                _logger.Warning("BackgroundProviderIdService: Iteration hat Timeout ({TimeoutSeconds}s) überschritten und wird hart abgebrochen.", timeout.TotalSeconds.ToString("F1", CultureInfo.CurrentCulture));
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Schleife: DB-Fehler (gesperrte Datei, Concurrency) eines Durchlaufs dürfen die Schleife nicht beenden; Fehler werden als Error geloggt und der nächste Intervall-Zyklus fährt fort.")]
        private async Task RunLoopAsync(CancellationToken ct)
        {
            await Task.Delay(InitialDelay, ct).ConfigureAwait(false);

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
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        public async Task RunOnceAsync(CancellationToken ct = default)
        {
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.ProviderIdEnrichment);
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

            // Apple-Music-Kennungen aus den gespeicherten Adressen ergänzen; kein Netzzugriff.
            if (provider.Includes(ProviderType.AppleMusic))
            {
                await EnrichAppleMusicIdsAsync(subscribedSeries, episodeService, ct);
            }

            // Spotify-IDs werden hier nicht ergänzt: die Spotify-Web-API-Suche ist
            // nicht gebaut (Nutzer-Entscheidung), und die Album-IDs kommen beim Import mit.
        }

        /// <summary>
        /// Trägt an Folgen mit Anbieter-Adresse, aber ohne Apple-Kennung die Kennung aus der Adresse nach.
        /// </summary>
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
                _logger.Info("{EnrichedEpisodes} Episoden mit Apple-Music-Album-ID ergänzt.", enrichedEpisodes);
            }
        }

        /// <summary>
        /// Extrahiert die CollectionId aus einer iTunes-URL.
        /// Format: https://music.apple.com/de/album/.../{CollectionId} oder .../id{CollectionId}
        /// </summary>
        /// <param name="url">Anbieter-Adresse der Folge, etwa ein Link auf music.apple.com.</param>
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

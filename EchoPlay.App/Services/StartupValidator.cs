using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Führt alle Startup-Validierungen während des Begrüßungsbildschirms durch.
    /// Die Ergebnisse werden im <see cref="StartupResult"/> zusammengefasst, damit das Dashboard
    /// direkt auf aktuelle, bereinigte Daten zugreifen kann – ohne eigene Checks.
    /// </summary>
    internal sealed class StartupValidator : IStartupValidator
    {
        /// <summary>
        /// Maximale Wartezeit für den Online-Konnektivitätscheck.
        /// 3 Sekunden sind ein guter Kompromiss: kurz genug für schnellen App-Start,
        /// lang genug für langsame Verbindungen (Mobilfunk, VPN).
        /// </summary>
        private static readonly TimeSpan OnlineCheckTimeout = TimeSpan.FromSeconds(3);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly BackgroundCoverService _backgroundCoverService;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger;

        /// <summary>
        /// Initialisiert den Validator mit den benötigten Abhängigkeiten.
        /// </summary>
        /// <param name="scopeFactory">Für scoped DB-Zugriffe.</param>
        /// <param name="backgroundCoverService">Für den synchronen Cover-Rebuild bei Cache-Clear.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        public StartupValidator(
            IServiceScopeFactory scopeFactory,
            BackgroundCoverService backgroundCoverService,
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory)
        {
            _scopeFactory = scopeFactory;
            _backgroundCoverService = backgroundCoverService;
            _logger = loggerFactory.CreateLogger("StartupValidator");
        }

        /// <inheritdoc />
        public async Task<StartupResult> ValidateAsync(
            Action<string>? onStatus = null,
            CancellationToken cancellationToken = default)
        {
            _logger.Info("Startup-Validierung gestartet.");

            onStatus?.Invoke("Lade Einstellungen …");

            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService =
                scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            ISeriesDataService seriesService =
                scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            ICachedNewReleaseDataService cacheService =
                scope.ServiceProvider.GetRequiredService<ICachedNewReleaseDataService>();

            AppSettings settings = await settingsService.GetAsync();
            IReadOnlyList<Series> subscribedSeries = await seriesService.GetSubscribedAsync();

            DateTime cutoffDate = (settings.LastAppStart ?? DateTime.UtcNow)
                .AddDays(-settings.NewReleaseDays);

            // Einmaliges Flag: alle Cache-Tabellen leeren.
            // Das Flag wird erst ganz am Ende zurückgesetzt, nachdem der vollständige
            // Neuaufbau abgeschlossen ist. Bei Fehlschlag bleibt es gesetzt → nächster
            // Start versucht es erneut.
            bool cacheCleared = settings.ClearCacheOnNextStart;

            if (cacheCleared)
            {
                onStatus?.Invoke("Leere Cache …");

                await cacheService.ClearAllAsync();

                ICoverImageDataService coverImageService =
                    scope.ServiceProvider.GetRequiredService<ICoverImageDataService>();
                await coverImageService.ClearAllAsync();

                _logger.Info("Cache geleert (Neuerscheinungen + Cover) – Neuaufbau läuft.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Schritt 1: Online-Konnektivitätscheck
            bool isOnlineAvailable = true;
            string? onlineHint = null;

            if (!settings.OfflineMode && settings.ActiveProvider != ProviderType.None)
            {
                onStatus?.Invoke("Prüfe Internetverbindung …");
                isOnlineAvailable = await CheckOnlineConnectivityAsync(cancellationToken);

                if (!isOnlineAvailable)
                {
                    onlineHint = "StartupOnlineUnavailableHint";
                    _logger.Warning("Online-Konnektivitätscheck fehlgeschlagen – Offline-Modus temporär aktiv.");
                }
            }
            else if (settings.OfflineMode)
            {
                isOnlineAvailable = false;
            }

            // Schritt 2: Lokales Verzeichnis prüfen
            bool isLocalAvailable = true;
            string? localHint = null;

            if (settings.LocalLibraryEnabled && !string.IsNullOrWhiteSpace(settings.LocalLibraryRootPath))
            {
                onStatus?.Invoke("Prüfe lokale Bibliothek …");
                isLocalAvailable = CheckLocalLibraryAccess(settings.LocalLibraryRootPath);

                if (!isLocalAvailable)
                {
                    localHint = "StartupLocalLibraryUnavailableHint";
                    _logger.Warning($"Lokales Verzeichnis nicht erreichbar: {settings.LocalLibraryRootPath}");
                }
            }

            // Schritt 3: Cache-Bereinigung für nicht-überwachte Serien
            onStatus?.Invoke("Aktualisiere Serien …");
            List<Guid> unwatchedSeriesIds = subscribedSeries
                .Where(s => !s.IsWatched)
                .Select(s => s.Id)
                .ToList();

            if (unwatchedSeriesIds.Count > 0)
            {
                int cleaned = await cacheService.RemoveBySeriesIdsAsync(unwatchedSeriesIds);
                if (cleaned > 0)
                {
                    _logger.Info($"{cleaned} Cache-Einträge für nicht-überwachte Serien entfernt.");
                }
            }

            // Schritt 4: Abgelaufene Einträge bereinigen
            int expired = await cacheService.RemoveOlderThanAsync(cutoffDate);
            if (expired > 0)
            {
                _logger.Debug($"{expired} abgelaufene Cache-Einträge entfernt.");
            }

            // Schritt 5: Neuerscheinungen-Refresh (nur wenn online verfügbar).
            // Eigener Scope, weil der Haupt-Scope nach Batch-Deletes (Schritt 3+4)
            // einen inkonsistenten Change-Tracker haben kann.
            if (isOnlineAvailable && !settings.OfflineMode)
            {
                onStatus?.Invoke("Überprüfe auf Neuerscheinungen …");

                using IServiceScope refreshScope = _scopeFactory.CreateScope();
                ICachedNewReleaseDataService refreshCacheService = refreshScope.ServiceProvider
                    .GetRequiredService<ICachedNewReleaseDataService>();

                await RefreshNewReleaseCacheAsync(
                    subscribedSeries, cutoffDate, refreshCacheService, refreshScope.ServiceProvider);
            }

            // Schritt 6: Fehlende Cover prüfen und nachladen.
            // Läuft bei jedem Start – der Splash bleibt bis alles durch ist.
            // Wenn alle Cover vorhanden sind, laufen nur Batch-Queries (ms).
            // Nach Cache-Clear werden Cover aus Dateisystem + Provider-URLs geladen.
            try
            {
                if (cacheCleared)
                {
                    onStatus?.Invoke("Lade Cover neu – das kann einen Moment dauern …");
                }
                else
                {
                    onStatus?.Invoke("Prüfe Cover …");
                }

                int coverCount = await _backgroundCoverService.RunOnceAsync();

                if (coverCount > 0)
                {
                    _logger.Info($"Cover-Check: {coverCount} fehlende Cover nachgeladen.");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Cover-Check fehlgeschlagen: {ex.Message}");
            }

            // Schritt 7: Flag zurücksetzen – auch wenn der Rebuild teilweise fehlgeschlagen ist,
            // damit der Cache nicht bei jedem Start erneut geleert wird.
            // Eigener Scope, weil der Haupt-Scope nach ExecuteDeleteAsync (Cache + Cover)
            // und weiteren Batch-Operationen keinen sauberen Change-Tracker mehr hat.
            if (cacheCleared)
            {
                using IServiceScope resetScope = _scopeFactory.CreateScope();
                IAppSettingsDataService resetService = resetScope.ServiceProvider
                    .GetRequiredService<IAppSettingsDataService>();
                AppSettings current = await resetService.GetAsync();
                current.ClearCacheOnNextStart = false;
                await resetService.SaveAsync(current);
                _logger.Info("Cache-Clear-Flag zurückgesetzt – Neuaufbau abgeschlossen.");
            }

            // Schritt 8: Bereinigte Cache-Einträge laden
            onStatus?.Invoke("Bereite Dashboard vor …");
            IReadOnlyList<CachedNewRelease> cachedReleases = await cacheService.GetAllAsync();

            _logger.Info($"Startup-Validierung abgeschlossen: Online={isOnlineAvailable}, " +
                $"Lokal={isLocalAvailable}, Cache={cachedReleases.Count} Einträge.");

            return new StartupResult
            {
                IsOnlineAvailable = isOnlineAvailable,
                IsLocalLibraryAvailable = isLocalAvailable,
                OnlineHintText = onlineHint,
                LocalLibraryHintText = localHint,
                SubscribedSeries = subscribedSeries,
                CachedReleases = cachedReleases,
                Settings = settings,
                NewReleaseCutoffDate = cutoffDate
            };
        }

        /// <summary>
        /// Prüft die Online-Konnektivität per HTTP-HEAD-Request auf die iTunes-API.
        /// Leichtgewichtig: kein Body, nur Verbindungsaufbau und Antwort.
        /// </summary>
        private async Task<bool> CheckOnlineConnectivityAsync(CancellationToken cancellationToken)
        {
            try
            {
                using HttpClient client = new() { Timeout = OnlineCheckTimeout };
                using HttpRequestMessage request = new(HttpMethod.Head, "https://itunes.apple.com/search?term=test&limit=1");
                using HttpResponseMessage response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                _logger.Debug($"Online-Check fehlgeschlagen: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Prüft, ob das lokale Bibliotheksverzeichnis existiert und lesbar ist.
        /// Ein reiner <see cref="Directory.Exists"/>-Check reicht nicht – das Verzeichnis könnte
        /// existieren, aber nicht lesbar sein (z.B. Netzlaufwerk ohne Verbindung).
        /// </summary>
        /// <param name="path">Pfad zum lokalen Bibliotheksverzeichnis.</param>
        /// <returns><see langword="true"/> wenn das Verzeichnis erreichbar und lesbar ist.</returns>
        private bool CheckLocalLibraryAccess(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return false;
                }

                // Lesezugriff testen – löst IOException bei nicht erreichbaren Netzlaufwerken aus
                Directory.GetDirectories(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Debug($"Lokales Verzeichnis nicht lesbar: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Aktualisiert den Neuerscheinungen-Cache gegen die iTunes-API.
        /// Identische Logik wie bisher in DashboardViewModel.RefreshNewReleaseCacheAsync,
        /// aber hier zentral im Startup ausgeführt.
        /// </summary>
        private async Task RefreshNewReleaseCacheAsync(
            IReadOnlyList<Series> subscribedSeries,
            DateTime cutoffDate,
            ICachedNewReleaseDataService cacheService,
            IServiceProvider serviceProvider)
        {
            if (subscribedSeries.Count == 0)
            {
                return;
            }

            // Prüfen ob ein Update nötig ist (letzte Prüfung < 24h)
            DateTime? lastCheck = await cacheService.GetLatestCheckTimeAsync();
            bool needsRefresh = lastCheck is null
                || DateTime.UtcNow - lastCheck.Value > TimeSpan.FromHours(24);

            if (!needsRefresh)
            {
                _logger.Debug("Neuerscheinungen-Cache ist aktuell (< 24h) – kein iTunes-Update nötig.");
                return;
            }

            // Nur überwachte Serien an die iTunes-API senden
            List<CheckableSeriesInfo> checkable = [];
            foreach (Series series in subscribedSeries)
            {
                if (!series.IsWatched) continue;

                checkable.Add(new CheckableSeriesInfo
                {
                    SeriesId = series.Id,
                    Title = series.Title,
                    AppleMusicArtistId = series.AppleMusicArtistId,
                    LocalFolderPath = series.LocalFolderPath,
                    CoverImageUrl = series.CoverImageUrl
                });
            }

            if (checkable.Count == 0)
            {
                _logger.Debug("Keine überwachten Serien – iTunes-Prüfung übersprungen.");
                return;
            }

            try
            {
                IOnlineEpisodeChecker checker =
                    serviceProvider.GetRequiredService<IOnlineEpisodeChecker>();

                IReadOnlyList<OnlineEpisodeCheckResult> results =
                    await checker.CheckNewReleasesAsync(checkable, cutoffDate);

                // Ergebnisse in Cache-Einträge umwandeln und speichern
                DateTime checkedAt = DateTime.UtcNow;
                List<CachedNewRelease> newEntries = [];

                foreach (OnlineEpisodeCheckResult result in results)
                {
                    foreach (NewReleaseEpisode release in result.NewReleaseEpisodes)
                    {
                        newEntries.Add(new CachedNewRelease
                        {
                            SeriesId = result.SeriesId,
                            Title = release.Title,
                            EpisodeNumber = release.EpisodeNumber,
                            ReleaseDate = release.ReleaseDate,
                            CoverUrl = release.CoverUrl,
                            CollectionId = release.CollectionId,
                            CheckedAtUtc = checkedAt
                        });
                    }
                }

                if (newEntries.Count > 0)
                {
                    await cacheService.UpsertRangeAsync(newEntries);
                }

                _logger.Info($"Neuerscheinungen-Cache aktualisiert: {newEntries.Count} Einträge aus {results.Count} Serien.");
            }
            catch (Exception ex)
            {
                // Fehler beim Refresh dürfen den Startup nicht blockieren –
                // der Cache enthält dann weiterhin die alten (bereinigten) Daten.
                string innerMsg = ex.InnerException?.Message ?? "keine InnerException";
                _logger.Warning($"Neuerscheinungen-Refresh fehlgeschlagen: {ex.Message} | Inner: {innerMsg}");
            }
        }
    }
}

using EchoPlay.Core.Abstractions.Time;
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
    public sealed class StartupValidator : IStartupValidator
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly BackgroundCoverService _backgroundCoverService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger;
        private readonly IClock _clock;

        /// <summary>
        /// Initialisiert den Validator mit den benötigten Abhängigkeiten.
        /// </summary>
        /// <param name="scopeFactory">Für scoped DB-Zugriffe.</param>
        /// <param name="backgroundCoverService">Für den synchronen Cover-Rebuild bei Cache-Clear.</param>
        /// <param name="httpClientFactory">Fabrik für den Online-Check-HttpClient (Named „OnlineCheck").</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        /// <param name="clock">Zeitquelle für Zeitstempel.</param>
        public StartupValidator(
            IServiceScopeFactory scopeFactory,
            BackgroundCoverService backgroundCoverService,
            IHttpClientFactory httpClientFactory,
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory,
            IClock clock)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _backgroundCoverService = backgroundCoverService;
            _httpClientFactory = httpClientFactory;
            _logger = loggerFactory.CreateLogger("StartupValidator");
            _clock = clock;
        }

        /// <inheritdoc />
        public async Task<StartupResult> ValidateAsync(
            Action<string>? onStatus = null,
            CancellationToken cancellationToken = default)
        {
            _logger.Info("Startup-Validierung gestartet.");
            onStatus?.Invoke("Lade Einstellungen …");

            using IServiceScope scope = _scopeFactory.CreateScope();
            IServiceProvider services = scope.ServiceProvider;

            ICachedNewReleaseDataService cacheService = services.GetRequiredService<ICachedNewReleaseDataService>();
            ICoverImageDataService coverImageService = services.GetRequiredService<ICoverImageDataService>();

            StartupContext context = await LoadContextAsync(
                services.GetRequiredService<IAppSettingsDataService>(),
                services.GetRequiredService<ISeriesDataService>(),
                cancellationToken);

            if (context.CacheCleared)
            {
                onStatus?.Invoke("Leere Cache …");
                await ClearCachesAsync(cacheService, coverImageService, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            (bool isOnlineAvailable, string? onlineHint) = await CheckOnlineAsync(context.Settings, onStatus, cancellationToken);
            (bool isLocalAvailable, string? localHint) = CheckLocal(context.Settings, onStatus);

            onStatus?.Invoke("Aktualisiere Serien …");
            await CleanCacheAsync(
                services.GetRequiredService<IWatchedTitleDataService>(), cacheService, context, cancellationToken);

            if (isOnlineAvailable && !context.Settings.OfflineMode)
            {
                onStatus?.Invoke("Überprüfe auf Neuerscheinungen …");
                await RefreshReleasesAsync(context, cancellationToken);
            }

            await LoadSeriesCoversAsync(context, isOnlineAvailable, onStatus, cancellationToken);
            await ResetCacheFlagAsync(context.CacheCleared, cancellationToken);

            onStatus?.Invoke("Bereite Dashboard vor …");
            IReadOnlyList<CachedNewRelease> cachedReleases = await cacheService.GetAllAsync(cancellationToken);

            _logger.Info(
                "Startup-Validierung abgeschlossen: Online={IsOnlineAvailable}, Lokal={IsLocalAvailable}, Cache={CacheCount} Einträge.",
                isOnlineAvailable, isLocalAvailable, cachedReleases.Count);

            await LogHealthSnapshotAsync(
                coverImageService, context, isOnlineAvailable, isLocalAvailable, cachedReleases.Count, cancellationToken);

            return new StartupResult
            {
                IsOnlineAvailable = isOnlineAvailable,
                IsLocalLibraryAvailable = isLocalAvailable,
                OnlineHintText = onlineHint,
                LocalLibraryHintText = localHint,
                SubscribedSeries = context.SubscribedSeries,
                CachedReleases = cachedReleases,
                Settings = context.Settings,
                NewReleaseCutoffDate = context.CutoffDate
            };
        }

        /// <summary>
        /// Was alle Schritte des Startlaufs brauchen: die Einstellungen, die abonnierten
        /// Serien, die Altersgrenze für Neuerscheinungen und die Frage, ob der
        /// Zwischenspeicher geleert werden soll.
        /// </summary>
        /// <param name="Settings">Die gespeicherten Einstellungen.</param>
        /// <param name="SubscribedSeries">Alle abonnierten Serien.</param>
        /// <param name="CutoffDate">Älteres gilt nicht mehr als Neuerscheinung.</param>
        /// <param name="CacheCleared">
        /// Ob der Zwischenspeicher in diesem Lauf geleert wird. Das Merkmal wird erst ganz
        /// am Ende zurückgesetzt — scheitert der Neuaufbau, versucht es der nächste Start
        /// erneut.
        /// </param>
        private sealed record StartupContext(
            AppSettings Settings,
            IReadOnlyList<Series> SubscribedSeries,
            DateTime CutoffDate,
            bool CacheCleared);

        /// <summary>Liest Einstellungen und Serien und leitet die Altersgrenze daraus ab.</summary>
        private async Task<StartupContext> LoadContextAsync(
            IAppSettingsDataService settingsService,
            ISeriesDataService seriesService,
            CancellationToken cancellationToken)
        {
            AppSettings settings = await settingsService.GetAsync(cancellationToken);
            IReadOnlyList<Series> subscribedSeries = await seriesService.GetSubscribedAsync(cancellationToken);

            DateTime cutoffDate = (settings.LastAppStart ?? _clock.UtcNow).AddDays(-settings.NewReleaseDays);

            return new StartupContext(settings, subscribedSeries, cutoffDate, settings.ClearCacheOnNextStart);
        }

        /// <summary>Leert Neuerscheinungen und Cover — der Neuaufbau läuft danach an.</summary>
        private async Task ClearCachesAsync(
            ICachedNewReleaseDataService cacheService,
            ICoverImageDataService coverImageService,
            CancellationToken cancellationToken)
        {
            await cacheService.ClearAllAsync(cancellationToken);
            _ = await coverImageService.ClearAllAsync(cancellationToken);

            _logger.Info("Cache geleert (Neuerscheinungen + Cover) – Neuaufbau läuft.");
        }

        /// <summary>
        /// Schritt 1: Erreichbarkeit prüfen. Ohne Anbieter oder im Offline-Modus entfällt
        /// die Prüfung — dann gilt „nicht online" ohne Hinweis, weil es kein Fehler ist.
        /// </summary>
        private async Task<(bool IsAvailable, string? Hint)> CheckOnlineAsync(
            AppSettings settings, Action<string>? onStatus, CancellationToken cancellationToken)
        {
            if (settings.OfflineMode)
            {
                return (false, null);
            }

            if (settings.ActiveProvider == ProviderType.None)
            {
                return (true, null);
            }

            onStatus?.Invoke("Prüfe Internetverbindung …");
            bool isAvailable = await CheckOnlineConnectivityAsync(cancellationToken);

            if (isAvailable)
            {
                return (true, null);
            }

            _logger.Warning("Online-Konnektivitätscheck fehlgeschlagen – Offline-Modus temporär aktiv.");
            return (false, "StartupOnlineUnavailableHint");
        }

        /// <summary>Schritt 2: Erreichbarkeit des lokalen Bibliotheksordners.</summary>
        private (bool IsAvailable, string? Hint) CheckLocal(AppSettings settings, Action<string>? onStatus)
        {
            if (!settings.LocalLibraryEnabled || string.IsNullOrWhiteSpace(settings.LocalLibraryRootPath))
            {
                return (true, null);
            }

            onStatus?.Invoke("Prüfe lokale Bibliothek …");

            if (CheckLocalLibraryAccess(settings.LocalLibraryRootPath))
            {
                return (true, null);
            }

            _logger.Warning("Lokales Verzeichnis nicht erreichbar: {RootPath}", settings.LocalLibraryRootPath);
            return (false, "StartupLocalLibraryUnavailableHint");
        }

        /// <summary>
        /// Schritte 3 und 4: Die Merkliste der überwachten Titel nachziehen, dann den
        /// Zwischenspeicher von nicht überwachten Serien und abgelaufenen Einträgen befreien.
        /// </summary>
        private async Task CleanCacheAsync(
            IWatchedTitleDataService watchedTitleService,
            ICachedNewReleaseDataService cacheService,
            StartupContext context,
            CancellationToken cancellationToken)
        {
            // Die Merkliste gibt neu eingelesenen Serien ihre Überwachung zurück, nachdem
            // die Mediathek geleert wurde.
            _ = await watchedTitleService.SyncFromWatchedSeriesAsync(cancellationToken);

            List<Guid> unwatchedSeriesIds = [.. context.SubscribedSeries.Where(s => !s.IsWatched).Select(s => s.Id)];

            if (unwatchedSeriesIds.Count > 0)
            {
                int cleaned = await cacheService.RemoveBySeriesIdsAsync(unwatchedSeriesIds, cancellationToken);
                if (cleaned > 0)
                {
                    _logger.Info("{Cleaned} Cache-Einträge für nicht-überwachte Serien entfernt.", cleaned);
                }
            }

            int expired = await cacheService.RemoveOlderThanAsync(context.CutoffDate, cancellationToken);
            if (expired > 0)
            {
                _logger.Debug(() => $"{expired} abgelaufene Cache-Einträge entfernt.");
            }
        }

        /// <summary>
        /// Schritt 5: Neuerscheinungen auffrischen. Eigener Bereich, weil der Hauptbereich
        /// nach den Stapel-Löschungen der Schritte 3 und 4 einen unstimmigen
        /// Änderungsverfolger haben kann.
        /// </summary>
        private async Task RefreshReleasesAsync(StartupContext context, CancellationToken cancellationToken)
        {
            using IServiceScope refreshScope = _scopeFactory.CreateScope();
            ICachedNewReleaseDataService refreshCacheService =
                refreshScope.ServiceProvider.GetRequiredService<ICachedNewReleaseDataService>();

            await RefreshNewReleaseCacheAsync(
                context.SubscribedSeries, context.CutoffDate, refreshCacheService,
                refreshScope.ServiceProvider, cancellationToken);
        }

        /// <summary>
        /// Schritt 6: Im Startbild werden ausschließlich fehlende Serien-Cover geholt.
        /// Folgen-Cover gehören nicht auf den Startpfad — die trägt der Hintergrunddienst
        /// nach, wenn das Fenster steht. Kein Folgen-Durchlauf, kein Lesen von
        /// Kennzeichnungen, kein Anbieter-Aufruf für Folgen.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Nachladen im Startbild: Fehler aus Datenbank, Kennzeichnungs-Bibliothek oder Dateisystem werden protokolliert, der Start fährt fort — sonst machte sich die Anwendung an einem einzelnen Bild unbrauchbar.")]
        private async Task LoadSeriesCoversAsync(
            StartupContext context, bool isOnlineAvailable, Action<string>? onStatus, CancellationToken cancellationToken)
        {
            try
            {
                onStatus?.Invoke(context.CacheCleared
                    ? "Lade Cover neu – das kann einen Moment dauern …"
                    : "Prüfe Cover …");

                bool canUseProvider = isOnlineAvailable && !context.Settings.OfflineMode;
                int coverCount = await _backgroundCoverService.RunSeriesCoversOnceAsync(canUseProvider, cancellationToken);

                if (coverCount > 0)
                {
                    _logger.Info("Cover-Check (Splash): {CoverCount} fehlende Serien-Cover nachgeladen.", coverCount);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("Cover-Check fehlgeschlagen: {Reason}", ex.Message);
            }
        }

        /// <summary>
        /// Schritt 7: Das Merkmal zurücksetzen — auch wenn der Neuaufbau nur teilweise
        /// gelang, damit der Zwischenspeicher nicht bei jedem Start erneut geleert wird.
        /// Eigener Bereich aus demselben Grund wie in Schritt 5.
        /// </summary>
        private async Task ResetCacheFlagAsync(bool cacheCleared, CancellationToken cancellationToken)
        {
            if (!cacheCleared)
            {
                return;
            }

            using IServiceScope resetScope = _scopeFactory.CreateScope();
            IAppSettingsDataService resetService =
                resetScope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();

            AppSettings current = await resetService.GetAsync(cancellationToken);
            current.ClearCacheOnNextStart = false;
            await resetService.SaveAsync(current, cancellationToken);

            _logger.Info("Cache-Clear-Flag zurückgesetzt – Neuaufbau abgeschlossen.");
        }

        /// <summary>
        /// Kompakter Zustandsbericht, der auch im Protokoll eines Supportfalls lesbar ist.
        /// Mehrzeilig, weil die Ablage die Zeilen trotzdem als einen Eintrag führt.
        /// </summary>
        private async Task LogHealthSnapshotAsync(
            ICoverImageDataService coverImageService,
            StartupContext context,
            bool isOnlineAvailable,
            bool isLocalAvailable,
            int cachedReleaseCount,
            CancellationToken cancellationToken)
        {
            int coverImageCount = await coverImageService.CountAsync(cancellationToken);
            AppSettings settings = context.Settings;

            _logger.Info(
                "Health-Check-Snapshot:\n" +
                $"  Online:             {isOnlineAvailable}\n" +
                $"  Lokal:              {isLocalAvailable}\n" +
                $"  AbonnierteSerien:   {context.SubscribedSeries.Count}\n" +
                $"  CoverCache:         {coverImageCount}\n" +
                $"  CachedReleases:     {cachedReleaseCount}\n" +
                $"  Provider:           {settings.ActiveProvider}\n" +
                $"  OfflineMode:        {settings.OfflineMode}\n" +
                $"  OnlineOnlyMode:     {settings.OnlineOnlyMode}\n" +
                $"  NewReleaseDays:     {settings.NewReleaseDays}\n" +
                $"  LogRetentionDays:   {settings.LogRetentionDays}\n" +
                $"  DbPurgeDays:        {settings.DbPurgeDays}");
        }

        /// <summary>
        /// Prüft die Online-Konnektivität per HTTP-HEAD-Request auf die iTunes-API.
        /// Leichtgewichtig: kein Body, nur Verbindungsaufbau und Antwort.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        private async Task<bool> CheckOnlineConnectivityAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Named „OnlineCheck"-Client hat ein kurzes Timeout (5s) und einen eigenen
                // User-Agent. IHttpClientFactory recycelt die Handler und verhindert das
                // Socket-Exhaustion-Risiko des alten „new HttpClient()"-Patterns.
                HttpClient client = _httpClientFactory.CreateClient("OnlineCheck");
                using HttpRequestMessage request = new(HttpMethod.Head, "https://itunes.apple.com/search?term=test&limit=1");
                using HttpResponseMessage response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                _logger.Debug(() => $"Online-Check fehlgeschlagen: {ex.Message}");
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
                _ = Directory.GetDirectories(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Debug(() => $"Lokales Verzeichnis nicht lesbar: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Aktualisiert den Neuerscheinungen-Cache gegen die iTunes-API.
        /// Identische Logik wie bisher in DashboardViewModel.RefreshNewReleaseCacheAsync,
        /// aber hier zentral im Startup ausgeführt.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Neuerscheinungen-Cache-Refresh pro Serie: HTTP-Fehler (Spotify/AppleMusic) oder DB-Fehler einer einzelnen Serie dürfen den Cache-Rebuild für die restlichen Serien nicht abbrechen.")]
        private async Task RefreshNewReleaseCacheAsync(
            IReadOnlyList<Series> subscribedSeries,
            DateTime cutoffDate,
            ICachedNewReleaseDataService cacheService,
            // Helper-Methode: Provider kommt aus dem aufrufenden Scope (kein Service-Locator im Konstruktor).
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            if (subscribedSeries.Count == 0)
            {
                return;
            }

            // Prüfen ob ein Update nötig ist (letzte Prüfung < 24h)
            DateTime? lastCheck = await cacheService.GetLatestCheckTimeAsync(cancellationToken);
            bool needsRefresh = lastCheck is null
                || _clock.UtcNow - lastCheck.Value > TimeSpan.FromHours(24);

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
                    await checker.CheckNewReleasesAsync(checkable, cutoffDate, cancellationToken);

                // Ergebnisse in Cache-Einträge umwandeln und speichern
                DateTime checkedAt = _clock.UtcNow;
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
                    await cacheService.UpsertRangeAsync(newEntries, cancellationToken);
                }

                _logger.Info("Neuerscheinungen-Cache aktualisiert: {NewEntries} Einträge aus {SeriesCount} Serien.", newEntries.Count, results.Count);
            }
            catch (Exception ex)
            {
                // Fehler beim Refresh dürfen den Startup nicht blockieren –
                // der Cache enthält dann weiterhin die alten (bereinigten) Daten.
                string innerMsg = ex.InnerException?.Message ?? "keine InnerException";
                _logger.Warning("Neuerscheinungen-Refresh fehlgeschlagen: {Reason} | Inner: {InnerReason}", ex.Message, innerMsg);
            }
        }
    }
}

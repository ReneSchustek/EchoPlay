using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Führt alle Startup-Validierungen während des Begrüßungsbildschirms durch: Erreichbarkeit
    /// (<see cref="StartupReachability"/>), Aufräumen und Auffrischen des Zwischenspeichers
    /// (<see cref="NewReleaseCacheMaintenance"/>), fehlende Serien-Cover. Das
    /// <see cref="StartupResult"/> trägt Erreichbarkeit und Hinweise an die Startseite weiter.
    /// </summary>
    public sealed class StartupValidator : IStartupValidator
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly BackgroundCoverService _backgroundCoverService;
        private readonly StartupReachability _reachability;
        private readonly NewReleaseCacheMaintenance _cacheMaintenance;
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
            _logger = loggerFactory.CreateLogger("StartupValidator");
            _clock = clock;
            _reachability = new StartupReachability(httpClientFactory, _logger);
            _cacheMaintenance = new NewReleaseCacheMaintenance(scopeFactory, clock, _logger);
        }

        /// <inheritdoc />
        public async Task<StartupResult> ValidateAsync(
            Action<string>? onStatus = null,
            CancellationToken cancellationToken = default)
        {
            _logger.Info("Startup-Validierung gestartet.");
            onStatus?.Invoke(SplashStatus.Text("SplashStatusLoadingSettings", "Lade Einstellungen …"));

            using IServiceScope scope = _scopeFactory.CreateScope();
            IServiceProvider services = scope.ServiceProvider;

            ICachedNewReleaseDataService cacheService = services.GetRequiredService<ICachedNewReleaseDataService>();
            ICoverImageDataService coverImageService = services.GetRequiredService<ICoverImageDataService>();

            StartupContext context = await LoadAndClearIfRequestedAsync(
                services, cacheService, coverImageService, onStatus, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            (bool isOnlineAvailable, string? onlineHint) = await _reachability.CheckOnlineAsync(context.Settings, onStatus, cancellationToken);
            (bool isLocalAvailable, string? localHint) = _reachability.CheckLocal(context.Settings, onStatus);

            await MaintainAsync(services, cacheService, context, isOnlineAvailable, onStatus, cancellationToken);

            onStatus?.Invoke(SplashStatus.Text("SplashStatusPreparingDashboard", "Bereite Dashboard vor …"));
            // Nur für Protokoll und Zustandsbericht: Die Startseite liest den Zwischenspeicher selbst.
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
                Settings = context.Settings
            };
        }

        /// <summary>
        /// Lädt Einstellungen und Serien; hat der Nutzer das Leeren des Zwischenspeichers
        /// verlangt, geschieht es hier, vor jeder weiteren Prüfung.
        /// </summary>
        private async Task<StartupContext> LoadAndClearIfRequestedAsync(
            IServiceProvider services,
            ICachedNewReleaseDataService cacheService,
            ICoverImageDataService coverImageService,
            Action<string>? onStatus,
            CancellationToken cancellationToken)
        {
            StartupContext context = await LoadContextAsync(
                services.GetRequiredService<IAppSettingsDataService>(),
                services.GetRequiredService<ISeriesDataService>(),
                cancellationToken);

            if (context.CacheCleared)
            {
                onStatus?.Invoke(SplashStatus.Text("SplashStatusClearingCache", "Leere Cache …"));
                await ClearCachesAsync(cacheService, coverImageService, cancellationToken);
            }

            return context;
        }

        /// <summary>
        /// Räumt den Neuerscheinungs-Zwischenspeicher auf, frischt ihn online auf, lädt die
        /// Serien-Cover und setzt den Merker für einen geleerten Zwischenspeicher zurück.
        /// </summary>
        private async Task MaintainAsync(
            IServiceProvider services,
            ICachedNewReleaseDataService cacheService,
            StartupContext context,
            bool isOnlineAvailable,
            Action<string>? onStatus,
            CancellationToken cancellationToken)
        {
            onStatus?.Invoke(SplashStatus.Text("SplashStatusUpdatingSeries", "Aktualisiere Serien …"));
            await _cacheMaintenance.CleanAsync(
                services.GetRequiredService<IWatchedTitleDataService>(), cacheService, context, cancellationToken);

            if (isOnlineAvailable && !context.Settings.OfflineMode)
            {
                onStatus?.Invoke(SplashStatus.Text("SplashStatusCheckingNewReleases", "Überprüfe auf Neuerscheinungen …"));
                await _cacheMaintenance.RefreshAsync(context, cancellationToken);
            }

            await LoadSeriesCoversAsync(context, isOnlineAvailable, onStatus, cancellationToken);
            await ResetCacheFlagAsync(context.CacheCleared, cancellationToken);
        }

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

        /// <summary>Leert Neuerscheinungen und Cover. Der Neuaufbau läuft danach an.</summary>
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
        /// Im Startbild werden ausschließlich fehlende Serien-Cover geholt.
        /// Folgen-Cover gehören nicht auf den Startpfad; die trägt der Hintergrunddienst
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
                    ? SplashStatus.Text("SplashStatusReloadingCovers", "Lade Cover neu – das kann einen Moment dauern …")
                    : SplashStatus.Text("SplashStatusCheckingCovers", "Prüfe Cover …"));

                bool canUseProvider = isOnlineAvailable && !context.Settings.OfflineMode;
                int coverCount = await _backgroundCoverService.RunSeriesCoversOnceAsync(canUseProvider, cancellationToken);

                if (coverCount > 0)
                {
                    _logger.Info("Cover-Check (Splash): {CoverCount} fehlende Serien-Cover nachgeladen.", coverCount);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ein Abbruch des Aufrufers ist kein Fehlschlag und gehört nach oben.
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning("Cover-Check fehlgeschlagen: {Reason}", ex.Message);
            }
        }

        /// <summary>
        /// Das Merkmal zurücksetzen, auch wenn der Neuaufbau nur teilweise gelang, damit der
        /// Zwischenspeicher nicht bei jedem Start erneut geleert wird. Eigener Bereich, weil der
        /// Hauptbereich nach den Stapel-Löschungen einen unstimmigen Änderungsverfolger haben kann.
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
    }
}

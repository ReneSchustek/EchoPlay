using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using EchoPlay.AppleMusic.DependencyInjection;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.TagManager.DependencyInjection;
using EchoPlay.Logger.Sinks;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.DependencyInjection;
using EchoPlay.LocalLibrary.DependencyInjection;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Extensions;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Auth;
using EchoPlay.Spotify.Clients;
using EchoPlay.Spotify.Configuration;
using EchoPlay.Spotify.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App
{
    /// <summary>
    /// Einstiegspunkt der EchoPlay-Anwendung.
    ///
    /// Die Klasse fungiert als Composition Root und ist
    /// verantwortlich für den Aufbau von Konfiguration
    /// und Dependency Injection.
    /// </summary>
    public partial class App : Application
    {
        private static IHost? _host;
        private static StartupResult? _startupResult;
        private Window? _window;
        private LoggerManager? _loggerManager;
        private EchoPlay.Logger.Abstractions.ILogger? _appLogger;

        /// <summary>
        /// Stellt den zentralen ServiceProvider der Anwendung bereit.
        /// Dieser Zugriff ist ausschließlich für UI-nahe Schichten gedacht.
        /// </summary>
        public static IServiceProvider Services =>
            _host?.Services ?? throw new InvalidOperationException("Host wurde noch nicht initialisiert.");

        /// <summary>
        /// Gibt das aktive Hauptfenster zurück.
        /// Wird für WinRT-Interop benötigt, z.B. für FolderPicker (InitializeWithWindow).
        /// </summary>
        public static Window? MainWindow { get; private set; }

        /// <summary>
        /// Ergebnis der Startup-Validierung, die während des Begrüßungsbildschirms ausgeführt wurde.
        /// Enthält vorgeladene Daten und Statusmeldungen für das Dashboard.
        /// </summary>
        public static StartupResult? StartupResultData => _startupResult;

        /// <summary>
        /// Initialisiert das Application-Objekt.
        /// Der eigentliche Aufbau erfolgt bewusst verzögert in OnLaunched,
        /// da dort der WinUI-Lebenszyklus garantiert bereit ist.
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Wird beim Start der Anwendung aufgerufen.
        /// Hier wird einmalig der Host erstellt, das Theme geladen und die UI gestartet.
        /// Das Theme wird vor dem ersten Rendern gesetzt, damit kein falsches Theme aufblitzt.
        /// </summary>
        /// <param name="args">Startparameter der Anwendung.</param>
        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Globaler Handler registrieren, bevor der Host gestartet wird.
            this.UnhandledException += OnUnhandledException;

            // Splash sofort zeigen, damit der Nutzer nicht auf einen leeren Bildschirm starrt.
            SplashWindow splash = new();
            splash.Activate();

            _host ??= CreateHost();

            // LoggerManager initialisieren – Cleanup läuft beim Dispose (App-Ende)
            _loggerManager = Services.GetRequiredService<LoggerManager>();
            _appLogger = _loggerManager.Factory.CreateLogger("App");

            _appLogger.Info("Anwendung gestartet");

            // Migrationen einmalig beim Start anwenden – kein Datenbankzugriff ohne aktuelles Schema.
            // Eigener Scope, weil DatabaseInitializer Scoped ist (DbContext-Lifetime).
            using (IServiceScope dbScope = Services.CreateScope())
            {
                DatabaseInitializer dbInit = dbScope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
                await dbInit.InitializeAsync();
            }

            // Log-Konfiguration und DbPurgeDays aus AppSettings laden.
            // AddEchoPlayLogger läuft vor dem DB-Zugriff, daher werden die gespeicherten Werte erst jetzt gesetzt.
            // LastAppStart wird bei jedem Start aktualisiert – dient als Referenz für den
            // Neuerscheinungen-Filter (nur Folgen der letzten 60 Tage ab LastAppStart).
            int dbPurgeDays;
            using (IServiceScope settingsScope = Services.CreateScope())
            {
                EchoPlay.Data.Services.Interfaces.IAppSettingsDataService settingsService =
                    settingsScope.ServiceProvider.GetRequiredService<EchoPlay.Data.Services.Interfaces.IAppSettingsDataService>();
                EchoPlay.Data.Entities.Settings.AppSettings appSettings = await settingsService.GetAsync();
                _loggerManager.UpdateRetentionDays(appSettings.LogRetentionDays);
                _loggerManager.UpdateMinimumLevel(appSettings.MinimumLogLevel);
                dbPurgeDays = appSettings.DbPurgeDays;

                appSettings.LastAppStart = DateTime.UtcNow;
                await settingsService.SaveAsync(appSettings);
            }

            // Datenbankpflege im Hintergrund – kein kritischer Pfad, darf den Start nicht verzögern.
            // Eigener Scope im Task stellt sicher, dass DbContext nicht vorzeitig freigegeben wird.
            _ = Task.Run(async () =>
            {
                try
                {
                    using IServiceScope purgeScope = Services.CreateScope();
                    IDatabaseMaintenanceService maintenance =
                        purgeScope.ServiceProvider.GetRequiredService<IDatabaseMaintenanceService>();
                    await maintenance.PurgeAsync(dbPurgeDays);
                }
                catch (Exception ex)
                {
                    // Purge-Fehler beeinflussen die App-Funktion nicht – wird beim nächsten Start erneut versucht
                    _appLogger?.Warning($"DB-Purge fehlgeschlagen: {ex.Message}");
                }
            });

            // Cover-Hintergrund-Service starten – lädt fehlende lokale Episoden-Cover in die DB
            Services.GetRequiredService<BackgroundCoverService>().Start();

            // Theme vor dem Fenster-Öffnen setzen, damit kein Flackern entsteht
            ThemeService themeService = Services.GetRequiredService<ThemeService>();
            await themeService.InitializeAsync();

            // Startup-Validierung: Online-Check, Lokal-Check, Cache-Bereinigung, Neuerscheinungen-Refresh.
            // Läuft komplett im Splash, damit das Dashboard sofort aktuelle Daten anzeigen kann.
            // Der Statustext wird direkt im Splash-Fenster angezeigt.
            IStartupValidator startupValidator = Services.GetRequiredService<IStartupValidator>();
            StartupResult startupResult = await startupValidator.ValidateAsync(
                status => splash.SetStatus(status));
            _startupResult = startupResult;

            _appLogger.Info($"Startup-Validierung abgeschlossen: Online={startupResult.IsOnlineAvailable}, Lokal={startupResult.IsLocalLibraryAvailable}");

            // Update-Check: prüft ob eine neuere Version auf GitHub verfügbar ist.
            // Läuft nach dem Splash, vor dem Hauptfenster – blockiert maximal 5 Sekunden.
            await CheckForUpdateAsync(splash);

            MainWindow = _window = new MainWindow();
            _window.Closed += OnWindowClosed;

            // RequestedTheme konnte in InitializeAsync() nicht gesetzt werden,
            // da das Fenster zu diesem Zeitpunkt noch nicht existierte.
            // Jetzt, wo Content verfügbar ist, den Wert nachliefern.
            themeService.SyncRequestedTheme();

            _window.Activate();

            splash.Close();
        }

        /// <summary>
        /// Wird beim Schließen des Hauptfensters aufgerufen.
        /// Führt Cleanup durch und gibt Ressourcen frei.
        /// </summary>
        /// <param name="sender">Das geschlossene Fenster.</param>
        /// <param name="args">Event-Argumente.</param>
        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            _appLogger?.Info("Anwendung wird beendet");

            // PRAGMA optimize vor dem Shutdown: SQLite aktualisiert interne Statistiken
            // basierend auf den Abfragen dieser Sitzung – verbessert den Query-Planer beim nächsten Start.
            try
            {
                using IServiceScope scope = _host!.Services.CreateScope();
                IDatabaseMaintenanceService maintenance = scope.ServiceProvider
                    .GetRequiredService<IDatabaseMaintenanceService>();
                // GetAwaiter().GetResult() ist hier bewusst gewählt:
                // OnWindowClosed kann nicht async sein, und beim Shutdown gibt es
                // keinen UI-SynchronizationContext der deadlocken könnte.
                maintenance.OptimizeAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Optimierung ist optional – darf das Beenden der App niemals blockieren
            }

            // Host zuerst disposen – gibt Singletons in umgekehrter Registrierungsreihenfolge frei.
            // Wichtig: PlayerService (Timer!) wird vor LoggerManager gestoppt,
            // weil PlayerService nach LoggerManager registriert wurde.
            // Ohne diesen Dispose würde der 500-ms-Timer nach dem Schließen des Fensters
            // weiter feuern und einen Crash im UI-Thread verursachen.
            _host?.Dispose();

            // Sicherheits-Dispose: greift, falls der LoggerManager nicht über den DI-Container
            // freigegeben wird (z.B. bei nicht-standard Registrierung).
            _loggerManager?.Dispose();
        }

        /// <summary>
        /// Behandelt alle nicht abgefangenen Exceptions aus dem UI-Thread.
        /// Verhindert das stille Beenden der Anwendung ohne sichtbaren Hinweis.
        /// </summary>
        /// <param name="sender">Quelle der Exception.</param>
        /// <param name="e">Enthält die nicht abgefangene Exception und ermöglicht optionales Unterdrücken des Absturzes.</param>
        private async void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // Absturz verhindern – die Exception wird als Dialog angezeigt
            e.Handled = true;

            if (_appLogger != null)
            {
                _appLogger.Fatal($"Nicht behandelte Exception: {e.Message}", e.Exception);
            }
            else
            {
                // Fallback falls Logger noch nicht initialisiert – Trace statt Debug,
                // da Debug.WriteLine im Release-Build entfernt wird
                System.Diagnostics.Trace.WriteLine($"[FATAL] Nicht behandelte Exception: {e.Exception}");
            }

            // Nur Dialog anzeigen wenn MainWindow bereits geöffnet ist.
            // Try-Catch: WinUI 3 erlaubt nur einen offenen ContentDialog pro XamlRoot.
            // Wenn die ursprüngliche Exception selbst von einem fehlgeschlagenen Dialog stammt,
            // würde ein zweiter ShowAsync-Aufruf die gleiche COMException werfen – endlose Kaskade.
            if (MainWindow is not null)
            {
                try
                {
                    EchoPlay.App.Services.ErrorDialogService errorDialog = Services.GetRequiredService<EchoPlay.App.Services.ErrorDialogService>();
                    await errorDialog.ShowAsync(
                        "Unerwarteter Fehler",
                        $"Die Anwendung hat einen Fehler festgestellt:\n\n{e.Message}");
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // Ein anderer ContentDialog ist bereits offen – Fehlerdialog verwerfen.
                    // Der Fehler wurde bereits geloggt, der Nutzer muss nicht zweimal informiert werden.
                }
            }
        }

        /// <summary>
        /// Prüft ob eine neuere Version auf GitHub verfügbar ist und zeigt ggf. einen Update-Dialog.
        /// Wird nach dem Splash aufgerufen, bevor das Hauptfenster geöffnet wird.
        /// </summary>
        /// <param name="splash">Das aktive Splash-Fenster für den XamlRoot des Dialogs.</param>
        private async Task CheckForUpdateAsync(SplashWindow splash)
        {
            try
            {
                UpdateCheckService updateCheckService = Services.GetRequiredService<UpdateCheckService>();
                EchoPlay.Core.Models.UpdateInfo? update = await updateCheckService.CheckForUpdateAsync();

                if (update is null)
                {
                    return;
                }

                _appLogger?.Info($"Neue Version verfügbar: {update.Version}");

                Windows.ApplicationModel.Resources.ResourceLoader resources =
                    Windows.ApplicationModel.Resources.ResourceLoader.GetForViewIndependentUse();

                // Drei-Optionen-Dialog: Jetzt aktualisieren / Später / Version überspringen
                ContentDialog dialog = new()
                {
                    Title = resources.GetString("UpdateAvailableTitle"),
                    Content = string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        resources.GetString("UpdateAvailableMessage"),
                        update.Version)
                        + (string.IsNullOrWhiteSpace(update.ReleaseNotes)
                            ? string.Empty
                            : "\n\n" + update.ReleaseNotes),
                    PrimaryButtonText = resources.GetString("UpdateNowButton"),
                    SecondaryButtonText = resources.GetString("UpdateLaterButton"),
                    CloseButtonText = resources.GetString("UpdateSkipButton"),
                    XamlRoot = splash.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Primary
                };

                ContentDialogResult result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    // Download und Installation starten
                    splash.SetStatus(resources.GetString("UpdateDownloadingMessage"));

                    UpdateDownloadService downloadService = Services.GetRequiredService<UpdateDownloadService>();
                    bool success = await downloadService.DownloadAndInstallAsync(
                        update.DownloadUrl,
                        update.Version);

                    if (success)
                    {
                        // Installer gestartet → App beenden
                        Exit();
                        return;
                    }

                    // Download fehlgeschlagen → Hinweis, dann normal weiter
                    IErrorDialogService errorDialog = Services.GetRequiredService<IErrorDialogService>();
                    await errorDialog.ShowAsync(
                        resources.GetString("UpdateDownloadFailedTitle"),
                        resources.GetString("UpdateDownloadFailedMessage"));
                }
                else if (result == ContentDialogResult.None)
                {
                    // "Version überspringen" → in DB merken
                    await updateCheckService.SkipVersionAsync(update.Version);
                    _appLogger?.Info($"Version {update.Version} übersprungen");
                }
            }
            catch (Exception ex)
            {
                // Update-Check darf den App-Start niemals blockieren
                _appLogger?.Warning($"Update-Check fehlgeschlagen: {ex.Message}");
            }
        }

        /// <summary>
        /// Erstellt und konfiguriert den generischen Host
        /// für die EchoPlay-Anwendung.
        /// </summary>
        /// <returns>Der konfigurierte Host.</returns>
        private static IHost CreateHost()
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();

            // Konfiguration wird bewusst im Host geladen,
            // da nur dieser Zugriff auf Dateisystem und Umgebungen hat.
            builder.Configuration
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true);

            // Logger registrieren (Cleanup läuft automatisch beim Start)
            builder.Services.AddEchoPlayLogger(options =>
            {
                options.LogDirectory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EchoPlay", "logs");
                options.MaxFileSizeMb = 10;
                options.RetentionDays = 30;
                options.MaxTotalSizeMb = 100;
                options.MinimumLevel = EchoPlay.Logger.Models.LogLevel.Debug;
                options.EnableDebugConsole = true;
                options.EnableFileLogging = true;
                options.EnableAutoCleanup = true;
                options.EnableMemorySink = true;
                options.MemorySinkCapacity = 100;
            });

            // Spotify-Konfiguration binden und als Singleton bereitstellen.
            SpotifyOptions spotifyOptions = builder.Configuration
                .GetSection("Spotify")
                .Get<SpotifyOptions>()
                ?? throw new InvalidOperationException("Spotify-Konfiguration fehlt.");

            builder.Services.AddSingleton(spotifyOptions);

            // HttpClient für Token-Anfragen (Client-Credentials-Flow).
            // Timeout kurz halten – ein hängender Token-Request blockiert jeden weiteren API-Call.
            builder.Services.AddHttpClient<SpotifyTokenClient>((services, client) =>
            {
                SpotifyOptions options = services.GetRequiredService<SpotifyOptions>();
                client.BaseAddress = new(options.AuthBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(10);
            });

            // Der AuthMessageHandler wird als Transient registriert,
            // damit jede HttpClient-Instanz ihren eigenen Handler erhält.
            builder.Services.AddTransient<SpotifyAuthMessageHandler>();

            // HttpClient für Spotify-Web-API mit automatischer Authentifizierung.
            builder.Services.AddHttpClient<SpotifyApiClient>((services, client) =>
            {
                SpotifyOptions options = services.GetRequiredService<SpotifyOptions>();
                client.BaseAddress = new(options.ApiBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddHttpMessageHandler<SpotifyAuthMessageHandler>();

            // ISpotifyApiClient auf den vom Factory verwalteten SpotifyApiClient abbilden.
            builder.Services.AddScoped<ISpotifyApiClient>(provider =>
                provider.GetRequiredService<SpotifyApiClient>());

            // Registrierung der Spotify-Use-Cases inkl. Keyed-Services für ImportService.
            builder.Services.AddSpotifyImport();

            // Registrierung der AppleMusic-Use-Cases inkl. Keyed-Services für ImportService.
            // Die iTunes Search API ist kostenfrei und benötigt keine Konfiguration oder Authentifizierung.
            builder.Services.AddAppleMusicImport();

            // Online-Folgenprüfung: vergleicht lokalen Stand mit iTunes-Katalog.
            // Checker als Scoped (nutzt DB + API), Cache-Ergebnisse liegen in der DB (CachedNewReleases).
            builder.Services.AddScoped<EchoPlay.Core.Abstractions.IOnlineEpisodeChecker,
                EchoPlay.App.Services.OnlineEpisodeChecker>();

            // Datenbankdienste für AppSettings und Mediathek-Verwaltung.
            builder.Services.AddEchoPlayData();

            // ThemeService als Singleton – der Zustand (aktives Theme) darf nur einmal existieren.
            builder.Services.AddSingleton<IThemeService, ThemeService>();
            builder.Services.AddSingleton<ThemeService>(provider =>
                (ThemeService)provider.GetRequiredService<IThemeService>());

            // ScanEventService als Singleton – überlebt Navigation, benachrichtigt neues ViewModel nach Rückkehr
            builder.Services.AddSingleton<IScanEventService, ScanEventService>();

            // NavigationService als Singleton – der ContentFrame existiert nur einmal pro App-Instanz.
            // MainWindow ruft Initialize(Frame) nach InitializeComponent() auf.
            builder.Services.AddSingleton<INavigationService, NavigationService>();
            builder.Services.AddSingleton<NavigationService>(provider =>
                (NavigationService)provider.GetRequiredService<INavigationService>());

            // WatchToggleService als Singleton – kapselt Watch-Toggle + NewRelease-Cache-Verwaltung.
            // Wird von beiden Mediathek-VMs genutzt, statt die Logik im Code-Behind zu duplizieren.
            builder.Services.AddSingleton<IWatchToggleService, WatchToggleService>();

            // LocalLibrary-Services für Scan, Metadaten und Cover.
            builder.Services.AddLocalLibrary();

            // PlayerService als Singleton – es darf immer nur eine Wiedergabeinstanz geben.
            builder.Services.AddSingleton<IPlayerService, PlayerService>();
            builder.Services.AddSingleton<PlayerService>(provider =>
                (PlayerService)provider.GetRequiredService<IPlayerService>());

            // SyncService als Singleton – nutzt eigenen DI-Scope intern via IServiceScopeFactory.
            builder.Services.AddSingleton<ISyncService, SyncService>();
            builder.Services.AddSingleton<SyncService>(provider =>
                (SyncService)provider.GetRequiredService<ISyncService>());

            // ImportService als Singleton – nutzt eigenen DI-Scope intern via IServiceScopeFactory.
            builder.Services.AddSingleton<ImportService>();

            // EpisodeCoverCacheService als Singleton – lädt fehlende Episoden-Cover im Hintergrund.
            // Eigener Service statt Teil von ImportService, damit die LocalLibrary-Assembly
            // nicht beim Laden des ImportService-Typs geladen wird (COM-Problem in Tests).
            builder.Services.AddSingleton<EpisodeCoverCacheService>();
            builder.Services.AddSingleton<CoverService>();
            builder.Services.AddSingleton<BackgroundCoverService>();

            // LocalizationService als Singleton – ResourceLoader-Instanz ist thread-sicher und teuer zu erzeugen.
            builder.Services.AddSingleton<ILocalizationService, LocalizationService>();

            // ErrorDialogService als Singleton – zeigt WinUI-3-ContentDialogs an.
            builder.Services.AddSingleton<IErrorDialogService, ErrorDialogService>();
            builder.Services.AddSingleton<ErrorDialogService>(provider =>
                (ErrorDialogService)provider.GetRequiredService<IErrorDialogService>());

            // ConfirmationDialogService als Singleton – zeigt Ja/Abbrechen-ContentDialogs an.
            builder.Services.AddSingleton<IConfirmationDialogService, ConfirmationDialogService>();

            // OnlineAccessGuard als Singleton – prüft Offline-Modus und schaltet StatusBar temporär auf Online.
            builder.Services.AddSingleton<IOnlineAccessGuard, OnlineAccessGuard>();

            // PageModeGuard als Singleton – kapselt den Offline-/Nur-Online-Check beim Betreten einer Page,
            // damit die ViewModels diesen Check nicht jeweils selbst implementieren müssen.
            builder.Services.AddSingleton<IPageModeGuard, PageModeGuard>();

            // FolderRestructureCoordinator als Singleton – kapselt AppSettings-Lookup, den
            // LocalLibrary-Restructure-Service und das Display-Mapping aus dem MediathekLokalViewModel.
            builder.Services.AddSingleton<IFolderRestructureCoordinator, FolderRestructureCoordinator>();

            // MissingEpisodesCoordinator als Singleton – kapselt Datei-System-Analyse,
            // Live-Online-Abgleich per iTunes und StatusBar-Aktualisierung für die
            // Fehlende-Folgen-Prüfung. Aus dem MediathekLokalViewModel ausgelagert.
            builder.Services.AddSingleton<IMissingEpisodesCoordinator, MissingEpisodesCoordinator>();

            // EpisodeCoverCoordinator als Singleton – kapselt Cover-Suche, Bestätigungs-
            // Dialog beim Überschreiben, HTTP-Download, CoverImages-Tabelle, optionales
            // Speichern als cover.jpg und das Card-Bitmap-Update.
            builder.Services.AddSingleton<IEpisodeCoverCoordinator, EpisodeCoverCoordinator>();

            // StartupValidator: führt alle Checks (Online, Lokal, Cache) im Splash durch.
            builder.Services.AddSingleton<IStartupValidator, StartupValidator>();

            // TaskbarProgressService als Singleton – steuert den Fortschrittsbalken im Taskleisten-Symbol.
            builder.Services.AddSingleton<TaskbarProgressService>();

            // Update-Services: prüft auf neue Versionen und lädt die Setup-Datei herunter.
            builder.Services.AddSingleton<UpdateCheckService>();
            builder.Services.AddSingleton<UpdateDownloadService>();

            // StatusBarViewModel als Singleton – Statistiken müssen App-weit konsistent sein.
            builder.Services.AddSingleton<StatusBarViewModel>();

            // MainWindowViewModel als Singleton – passend zum einzigen Hauptfenster der App.
            builder.Services.AddSingleton<MainWindowViewModel>();

            // ViewModels als Transient – jede Navigation erzeugt eine frische Instanz.
            builder.Services.AddTransient<DashboardViewModel>(provider => new DashboardViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<IConfirmationDialogService>(),
                provider.GetRequiredService<IPlayerService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>(),
                provider.GetRequiredService<CoverService>(),
                provider.GetRequiredService<ILocalizationService>()));
            builder.Services.AddTransient<MediathekOnlineViewModel>(provider => new MediathekOnlineViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IConfirmationDialogService>(),
                provider.GetRequiredService<ImportService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<IOnlineAccessGuard>(),
                provider.GetRequiredService<EpisodeCoverCacheService>(),
                provider.GetRequiredService<CoverService>(),
                provider.GetRequiredService<BackgroundCoverService>(),
                provider.GetRequiredService<IWatchToggleService>(),
                provider.GetRequiredService<IPageModeGuard>(),
                provider.GetRequiredService<EchoPlay.LocalLibrary.Cover.ICoverSearchService>(),
                provider.GetRequiredService<INavigationService>()));
            builder.Services.AddTransient<MediathekLokalViewModel>(provider => new MediathekLokalViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<ISyncService>(),
                provider.GetRequiredService<IPlayerService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<IConfirmationDialogService>(),
                provider.GetRequiredService<StatusBarViewModel>(),
                provider.GetRequiredService<EchoPlay.LocalLibrary.Cover.ILocalCoverLoader>(),
                provider.GetRequiredService<IScanEventService>(),
                provider.GetRequiredService<EchoPlay.LocalLibrary.Cover.ICoverSearchService>(),
                provider.GetRequiredService<IOnlineAccessGuard>(),
                provider.GetRequiredService<EchoPlay.Core.Abstractions.IOnlineEpisodeChecker>(),
                provider.GetRequiredService<CoverService>(),
                provider.GetRequiredService<IWatchToggleService>(),
                provider.GetRequiredService<IPageModeGuard>(),
                provider.GetRequiredService<IFolderRestructureCoordinator>(),
                provider.GetRequiredService<IMissingEpisodesCoordinator>(),
                provider.GetRequiredService<IEpisodeCoverCoordinator>()));
            builder.Services.AddTransient<SucheViewModel>(provider => new SucheViewModel(
                provider.GetRequiredService<ImportService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<INavigationService>(),
                provider.GetRequiredService<IPageModeGuard>()));
            builder.Services.AddTransient<PlayerViewModel>();
            builder.Services.AddTransient<SeriesDetailViewModel>();
            // App-Services für Settings: Verbindungstest und Log-Viewer sind nach Brief 211
            // als eigenständige Coordinators implementiert, damit das SettingsViewModel
            // stateless Logik nicht mehr selbst trägt.
            builder.Services.AddSingleton<IConnectionTestCoordinator, ConnectionTestCoordinator>();
            builder.Services.AddSingleton<ILogViewerCoordinator>(provider => new LogViewerCoordinator(
                provider.GetRequiredService<LoggerManager>(),
                provider.GetService<MemorySink>()));
            builder.Services.AddTransient<SettingsViewModel>(provider => new SettingsViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IThemeService>(),
                provider.GetRequiredService<ISyncService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<EchoPlay.LocalLibrary.Analysis.IEpisodePatternAnalyzer>(),
                provider.GetRequiredService<IConnectionTestCoordinator>(),
                provider.GetRequiredService<ILogViewerCoordinator>(),
                provider.GetRequiredService<LoggerManager>(),
                provider.GetRequiredService<StatusBarViewModel>()));
            builder.Services.AddTransient<MiniPlayerViewModel>();
            builder.Services.AddTransient<ImportViewModel>(provider => new ImportViewModel(
                provider.GetRequiredService<ImportService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<IOnlineAccessGuard>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<StatusBarViewModel>()));

            // TagManager-Dienste (ITagService, ITagLookupService mit MusicBrainz-HttpClient).
            builder.Services.AddTagManager();
            // App-Service, der den MusicBrainz-Lookup und die Query-/Match-Logik für den Tag-Manager kapselt.
            builder.Services.AddSingleton<ITagLookupCoordinator, TagLookupCoordinator>();
            builder.Services.AddTransient<TagManagerViewModel>();

            // ProtokollViewModel als Transient – jede Navigation erzeugt eine frische Instanz
            // und meldet sich sauber beim MemorySink ab.
            builder.Services.AddTransient<ProtokollViewModel>(provider => new ProtokollViewModel(
                provider.GetService<MemorySink>()));

            builder.Services.AddTransient<StatistikViewModel>();

            return builder.Build();
        }
    }
}
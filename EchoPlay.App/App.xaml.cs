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
using Microsoft.Extensions.Http.Resilience;
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
            SplashWindow? splash = null;
            try
            {
                // Globaler Handler registrieren, bevor der Host gestartet wird.
                this.UnhandledException += OnUnhandledException;

                // Splash sofort zeigen, damit der Nutzer nicht auf einen leeren Bildschirm starrt.
                splash = new SplashWindow();
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

                    IClock clock = Services.GetRequiredService<IClock>();
                    appSettings.LastAppStart = clock.UtcNow;
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

                // Provider-ID-Enrichment starten – ergänzt fehlende SpotifyAlbumId/AppleMusicAlbumId
                Services.GetRequiredService<BackgroundProviderIdService>().Start();

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
            catch (Exception ex)
            {
                await HandleStartupFailureAsync(splash, ex);
            }
        }

        /// <summary>
        /// Zeigt dem Nutzer einen Fehlerdialog, wenn der App-Start nicht abgeschlossen werden konnte,
        /// und beendet die Anwendung kontrolliert. Loggt in Trace und – falls verfügbar – über den
        /// App-Logger, damit der Fehler auch ohne sichtbares Hauptfenster nachvollziehbar ist.
        /// </summary>
        /// <param name="splash">Das Splash-Fenster, sofern bereits erzeugt.</param>
        /// <param name="exception">Die während <see cref="OnLaunched"/> geworfene Exception.</param>
        private async Task HandleStartupFailureAsync(SplashWindow? splash, Exception exception)
        {
            // Notfall-Logging in Trace (Logger eventuell noch nicht initialisiert)
            System.Diagnostics.Trace.WriteLine($"[FATAL OnLaunched] {exception}");
            try { _appLogger?.Fatal($"OnLaunched fehlgeschlagen: {exception.Message}", exception); }
            catch { /* Logger-Fehler dürfen den Fehlerdialog nicht verhindern */ }

            // Fallback-Dialog am Splash zeigen, damit der Nutzer eine Rückmeldung sieht.
            try
            {
                if (splash?.Content?.XamlRoot is not null)
                {
                    ContentDialog errorDialog = new()
                    {
                        Title = "EchoPlay konnte nicht starten",
                        Content = $"Beim Starten der Anwendung ist ein Fehler aufgetreten:\n\n{exception.Message}\n\nDie Anwendung wird beendet.",
                        CloseButtonText = "OK",
                        XamlRoot = splash.Content.XamlRoot
                    };
                    _ = await errorDialog.ShowAsync();
                }
            }
            catch
            {
                // Dialog konnte nicht angezeigt werden – Logging bleibt als Diagnose-Quelle
            }

            try { splash?.Close(); } catch { /* Schliessen darf das Exit nicht blockieren */ }

            Exit();
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
            _ = builder.Configuration
                .AddJsonFile("appsettings.json", optional: false);

            // Logger registrieren (Cleanup läuft automatisch beim Start)
            _ = builder.Services.AddEchoPlayLogger(options =>
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

            // Zentrale Named-HttpClients. Alle App-seitigen HTTP-Konsumenten holen sich
            // ihren Client ueber IHttpClientFactory, statt eigene statische Instanzen
            // zu halten. Damit greifen einheitliche Timeouts, User-Agent-Header und
            // bei Bedarf spaeter auch Polly-Resilience-Policies (siehe Brief 228).
            _ = builder.Services.AddHttpClient("CoverDownload", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-CoverDownload/1.0");
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            });
            _ = builder.Services.AddHttpClient("OnlineCheck", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-OnlineCheck/1.0");
            });
            _ = builder.Services.AddHttpClient("UpdateDownload", client =>
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-UpdateDownload/1.0");
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
            });
            _ = builder.Services.AddHttpClient("UpdateCheck", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-UpdateCheck/1.0");
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            });

            // Basis-URLs aus Konfiguration (keine Credentials — die kommen aus dem Credential-Store).
            SpotifyOptions baseSpotifyOptions = new()
            {
                ApiBaseUrl = builder.Configuration.GetValue<string>("Spotify:ApiBaseUrl") ?? "https://api.spotify.com/v1/",
                AuthBaseUrl = builder.Configuration.GetValue<string>("Spotify:AuthBaseUrl") ?? "https://accounts.spotify.com/"
            };

            // Credential-Store und Options-Provider registrieren.
            _ = builder.Services.AddSingleton<ISpotifyCredentialStore, SpotifyCredentialStore>();
            _ = builder.Services.AddSingleton<ISpotifyOptionsProvider>(provider =>
                new SpotifyOptionsProvider(baseSpotifyOptions, provider.GetRequiredService<ISpotifyCredentialStore>()));

            // Named HttpClient für Token-Anfragen (Client-Credentials-Flow).
            // Timeout kurz halten – ein hängender Token-Request blockiert jeden weiteren API-Call.
            _ = builder.Services.AddHttpClient("SpotifyToken", client =>
            {
                client.BaseAddress = new(baseSpotifyOptions.AuthBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(10);
            });

            // SpotifyTokenClient manuell registrieren, damit die Credentials zur Laufzeit
            // aus dem Credential-Store kommen statt aus einer statischen Konfiguration.
            _ = builder.Services.AddScoped<SpotifyTokenClient>(provider =>
            {
                IHttpClientFactory httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                HttpClient client = httpClientFactory.CreateClient("SpotifyToken");
                ISpotifyOptionsProvider optionsProvider = provider.GetRequiredService<ISpotifyOptionsProvider>();
                SpotifyOptions options = optionsProvider.GetAsync().GetAwaiter().GetResult() ?? baseSpotifyOptions;
                return new SpotifyTokenClient(client, options, provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());
            });

            // Der AuthMessageHandler wird als Transient registriert,
            // damit jede HttpClient-Instanz ihren eigenen Handler erhält.
            _ = builder.Services.AddTransient<SpotifyAuthMessageHandler>();

            // Named HttpClient für Spotify-Web-API mit automatischer Authentifizierung.
            _ = builder.Services.AddHttpClient("SpotifyApi", client =>
            {
                client.BaseAddress = new(baseSpotifyOptions.ApiBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddHttpMessageHandler<SpotifyAuthMessageHandler>();

            // SpotifyApiClient manuell registrieren, damit der Named HttpClient verwendet wird.
            _ = builder.Services.AddScoped<SpotifyApiClient>(provider =>
            {
                IHttpClientFactory httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                HttpClient client = httpClientFactory.CreateClient("SpotifyApi");
                return new SpotifyApiClient(client, provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());
            });

            // ISpotifyApiClient auf den manuell registrierten SpotifyApiClient abbilden.
            _ = builder.Services.AddScoped<ISpotifyApiClient>(provider =>
                provider.GetRequiredService<SpotifyApiClient>());

            // Registrierung der Spotify-Use-Cases inkl. Keyed-Services für ImportService.
            _ = builder.Services.AddSpotifyImport();

            // Registrierung der AppleMusic-Use-Cases inkl. Keyed-Services für ImportService.
            // Die iTunes Search API ist kostenfrei und benötigt keine Konfiguration oder Authentifizierung.
            _ = builder.Services.AddAppleMusicImport();

            // Online-Folgenprüfung: vergleicht lokalen Stand mit iTunes-Katalog.
            // Checker als Scoped (nutzt DB + API), Cache-Ergebnisse liegen in der DB (CachedNewReleases).
            _ = builder.Services.AddScoped<EchoPlay.Core.Abstractions.IOnlineEpisodeChecker,
                EchoPlay.App.Services.OnlineEpisodeChecker>();

            // Datenbankdienste für AppSettings und Mediathek-Verwaltung.
            _ = builder.Services.AddEchoPlayData();

            // ThemeService als Singleton – der Zustand (aktives Theme) darf nur einmal existieren.
            _ = builder.Services.AddSingleton<IThemeService, ThemeService>();
            _ = builder.Services.AddSingleton<ThemeService>(provider =>
                (ThemeService)provider.GetRequiredService<IThemeService>());

            // IClock als Singleton – einzige Stelle, an der DateTime.UtcNow in Produktion gelesen wird.
            // Tests injizieren einen FakeClock, damit zeitabhängige Logik reproduzierbar prüfbar wird.
            _ = builder.Services.AddSingleton<IClock, SystemClock>();
            _ = builder.Services.AddSingleton<IHostRateLimiter>(_ =>
                new SemaphoreHostRateLimiter(new Dictionary<string, TimeSpan>
                {
                    ["musicbrainz.org"]     = TimeSpan.FromSeconds(1),
                    ["coverartarchive.org"] = TimeSpan.FromSeconds(1),
                    ["itunes.apple.com"]    = TimeSpan.FromMilliseconds(1500),
                }));

            // ScanEventService als Singleton – überlebt Navigation, benachrichtigt neues ViewModel nach Rückkehr
            _ = builder.Services.AddSingleton<IScanEventService, ScanEventService>();

            // NavigationService als Singleton – der ContentFrame existiert nur einmal pro App-Instanz.
            // MainWindow ruft Initialize(Frame) nach InitializeComponent() auf.
            _ = builder.Services.AddSingleton<INavigationService, NavigationService>();
            _ = builder.Services.AddSingleton<NavigationService>(provider =>
                (NavigationService)provider.GetRequiredService<INavigationService>());

            // WatchToggleService als Singleton – kapselt Watch-Toggle + NewRelease-Cache-Verwaltung.
            // Wird von beiden Mediathek-VMs genutzt, statt die Logik im Code-Behind zu duplizieren.
            _ = builder.Services.AddSingleton<IWatchToggleService, WatchToggleService>();

            // LocalLibrary-Services für Scan, Metadaten und Cover.
            _ = builder.Services.AddLocalLibrary();

            // PlayerService als Singleton – es darf immer nur eine Wiedergabeinstanz geben.
            _ = builder.Services.AddSingleton<IPlayerService, PlayerService>();
            _ = builder.Services.AddSingleton<PlayerService>(provider =>
                (PlayerService)provider.GetRequiredService<IPlayerService>());

            // SyncService als Singleton – nutzt eigenen DI-Scope intern via IServiceScopeFactory.
            _ = builder.Services.AddSingleton<ISyncService, SyncService>();
            _ = builder.Services.AddSingleton<SyncService>(provider =>
                (SyncService)provider.GetRequiredService<ISyncService>());

            // ImportService als Singleton – nutzt eigenen DI-Scope intern via IServiceScopeFactory.
            _ = builder.Services.AddSingleton<ImportService>();

            // EpisodeCoverCacheService als Singleton – lädt fehlende Episoden-Cover im Hintergrund.
            // Eigener Service statt Teil von ImportService, damit die LocalLibrary-Assembly
            // nicht beim Laden des ImportService-Typs geladen wird (COM-Problem in Tests).
            _ = builder.Services.AddSingleton<EpisodeCoverCacheService>();
            _ = builder.Services.AddSingleton<CoverBrightnessAnalyzer>();
            _ = builder.Services.AddSingleton<CoverService>();
            _ = builder.Services.AddSingleton(new BackgroundCoverServiceOptions());
            _ = builder.Services.AddSingleton<BackgroundCoverService>();
            _ = builder.Services.AddSingleton<BackgroundProviderIdService>();

            // LocalizationService als Singleton – ResourceLoader-Instanz ist thread-sicher und teuer zu erzeugen.
            _ = builder.Services.AddSingleton<ILocalizationService, LocalizationService>();

            // ErrorDialogService als Singleton – zeigt WinUI-3-ContentDialogs an.
            _ = builder.Services.AddSingleton<IErrorDialogService, ErrorDialogService>();
            _ = builder.Services.AddSingleton<ErrorDialogService>(provider =>
                (ErrorDialogService)provider.GetRequiredService<IErrorDialogService>());

            // ConfirmationDialogService als Singleton – zeigt Ja/Abbrechen-ContentDialogs an.
            _ = builder.Services.AddSingleton<IConfirmationDialogService, ConfirmationDialogService>();

            // OnlineAccessGuard als Singleton – prüft Offline-Modus und schaltet StatusBar temporär auf Online.
            _ = builder.Services.AddSingleton<IOnlineAccessGuard, OnlineAccessGuard>();

            // PageModeGuard als Singleton – kapselt den Offline-/Nur-Online-Check beim Betreten einer Page,
            // damit die ViewModels diesen Check nicht jeweils selbst implementieren müssen.
            _ = builder.Services.AddSingleton<IPageModeGuard, PageModeGuard>();

            // FolderRestructureCoordinator als Singleton – kapselt AppSettings-Lookup, den
            // LocalLibrary-Restructure-Service und das Display-Mapping aus dem MediathekLokalViewModel.
            _ = builder.Services.AddSingleton<IFolderRestructureCoordinator, FolderRestructureCoordinator>();

            // MissingEpisodesCoordinator als Singleton – kapselt Datei-System-Analyse,
            // Live-Online-Abgleich per iTunes und StatusBar-Aktualisierung für die
            // Fehlende-Folgen-Prüfung. Aus dem MediathekLokalViewModel ausgelagert.
            _ = builder.Services.AddSingleton<IMissingEpisodesCoordinator, MissingEpisodesCoordinator>();

            // EpisodeCoverCoordinator als Singleton – kapselt Cover-Suche, Bestätigungs-
            // Dialog beim Überschreiben, HTTP-Download, CoverImages-Tabelle, optionales
            // Speichern als cover.jpg und das Card-Bitmap-Update.
            _ = builder.Services.AddSingleton<IEpisodeCoverCoordinator, EpisodeCoverCoordinator>();

            // StartupValidator: führt alle Checks (Online, Lokal, Cache) im Splash durch.
            _ = builder.Services.AddSingleton<IStartupValidator, StartupValidator>();

            // TaskbarProgressService als Singleton – steuert den Fortschrittsbalken im Taskleisten-Symbol.
            _ = builder.Services.AddSingleton<TaskbarProgressService>();

            // Update-Services: prüft auf neue Versionen und lädt die Setup-Datei herunter.
            _ = builder.Services.AddSingleton<UpdateCheckService>();
            _ = builder.Services.AddSingleton<UpdateDownloadService>();

            // StatusBarViewModel als Singleton – Statistiken müssen App-weit konsistent sein.
            _ = builder.Services.AddSingleton<StatusBarViewModel>();

            // MainWindowViewModel als Singleton – passend zum einzigen Hauptfenster der App.
            _ = builder.Services.AddSingleton<MainWindowViewModel>();

            // ViewModels als Transient – jede Navigation erzeugt eine frische Instanz.
            _ = builder.Services.AddTransient<DashboardViewModel>(provider => new DashboardViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<IConfirmationDialogService>(),
                provider.GetRequiredService<IPlayerService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>(),
                provider.GetRequiredService<CoverService>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<IClock>()));
            _ = builder.Services.AddTransient<MediathekOnlineViewModel>(provider => new MediathekOnlineViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IConfirmationDialogService>(),
                provider.GetRequiredService<ImportService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<IOnlineAccessGuard>(),
                provider.GetRequiredService<IHttpClientFactory>(),
                provider.GetRequiredService<CoverBrightnessAnalyzer>(),
                provider.GetRequiredService<EpisodeCoverCacheService>(),
                provider.GetRequiredService<CoverService>(),
                provider.GetRequiredService<BackgroundCoverService>(),
                provider.GetRequiredService<IWatchToggleService>(),
                provider.GetRequiredService<IHostRateLimiter>(),
                provider.GetRequiredService<IPageModeGuard>(),
                provider.GetRequiredService<EchoPlay.LocalLibrary.Cover.ICoverSearchService>(),
                provider.GetRequiredService<INavigationService>()));
            _ = builder.Services.AddTransient<MediathekLokalViewModel>(provider => new MediathekLokalViewModel(
                new MediathekLokalViewModelContext(
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
                    provider.GetRequiredService<IClock>(),
                    provider.GetRequiredService<CoverService>(),
                    provider.GetRequiredService<IWatchToggleService>(),
                    provider.GetRequiredService<IPageModeGuard>(),
                    provider.GetRequiredService<IFolderRestructureCoordinator>(),
                    provider.GetRequiredService<IMissingEpisodesCoordinator>(),
                    provider.GetRequiredService<IEpisodeCoverCoordinator>())));
            _ = builder.Services.AddTransient<SucheViewModel>(provider => new SucheViewModel(
                provider.GetRequiredService<ImportService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<INavigationService>(),
                provider.GetRequiredService<IPageModeGuard>(),
                provider.GetRequiredService<CoverBrightnessAnalyzer>()));
            _ = builder.Services.AddTransient<PlayerViewModel>();
            _ = builder.Services.AddTransient<SeriesDetailViewModel>();
            // App-Services für Settings: Verbindungstest und Log-Viewer sind nach Brief 211
            // als eigenständige Coordinators implementiert, damit das SettingsViewModel
            // stateless Logik nicht mehr selbst trägt.
            _ = builder.Services.AddSingleton<IConnectionTestCoordinator, ConnectionTestCoordinator>();
            _ = builder.Services.AddSingleton<ILogViewerCoordinator>(provider => new LogViewerCoordinator(
                provider.GetRequiredService<LoggerManager>(),
                provider.GetService<MemorySink>()));
            _ = builder.Services.AddTransient<SettingsViewModel>(provider => new SettingsViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IThemeService>(),
                provider.GetRequiredService<ISyncService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<IConfirmationDialogService>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<EchoPlay.LocalLibrary.Analysis.IEpisodePatternAnalyzer>(),
                provider.GetRequiredService<IConnectionTestCoordinator>(),
                provider.GetRequiredService<ISpotifyCredentialStore>(),
                provider.GetRequiredService<ISpotifyOptionsProvider>(),
                provider.GetRequiredService<ILogViewerCoordinator>(),
                provider.GetRequiredService<LoggerManager>(),
                provider.GetRequiredService<StatusBarViewModel>()));
            _ = builder.Services.AddTransient<MiniPlayerViewModel>();
            _ = builder.Services.AddTransient<ImportViewModel>(provider => new ImportViewModel(
                provider.GetRequiredService<ImportService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<IOnlineAccessGuard>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<StatusBarViewModel>()));

            // TagManager-Dienste (ITagService, ITagLookupService mit MusicBrainz-HttpClient).
            _ = builder.Services.AddTagManager();
            // App-Service, der den MusicBrainz-Lookup und die Query-/Match-Logik für den Tag-Manager kapselt.
            _ = builder.Services.AddSingleton<ITagLookupCoordinator, TagLookupCoordinator>();
            _ = builder.Services.AddTransient<TagManagerViewModel>();

            // ProtokollViewModel als Transient – jede Navigation erzeugt eine frische Instanz
            // und meldet sich sauber beim MemorySink ab.
            _ = builder.Services.AddTransient<ProtokollViewModel>(provider => new ProtokollViewModel(
                provider.GetService<MemorySink>()));

            _ = builder.Services.AddTransient<StatistikViewModel>();

            return builder.Build();
        }
    }
}
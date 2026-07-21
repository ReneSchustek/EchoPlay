using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Clients;
using EchoPlay.AppleMusic.DependencyInjection;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.TagManager.DependencyInjection;
using EchoPlay.Logger.Sinks;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.DependencyInjection;
using EchoPlay.LocalLibrary.DependencyInjection;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.DependencyInjection;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Auth;
using EchoPlay.Spotify.Clients;
using EchoPlay.Spotify.Configuration;
using EchoPlay.Spotify.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "WinUI-3 Entry-Point-Typ heißt per Konvention 'App' im 'EchoPlay.App'-Namespace.")]
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "WinUI-Entry-Point: Startup-Exception führt via HandleStartupFailureAsync zu Dialog + kontrolliertem App-Ende statt Crash.")]
        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            SplashWindow? splash = null;
            try
            {
                // Globaler Handler registrieren, bevor der Host gestartet wird.
                this.UnhandledException += OnUnhandledException;

                // Zusätzliche Fanglinien für Exceptions, die WinUIs UnhandledException nicht abfängt:
                // - AppDomain.UnhandledException: Fehler aus Nicht-UI-Threads (Task.Run ohne await, Threadpool).
                // - TaskScheduler.UnobservedTaskException: Tasks deren Exception nie per await konsumiert wurde.
                AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

                // Splash sofort zeigen, damit der Nutzer nicht auf einen leeren Bildschirm starrt.
                // Logo aus Embedded-Resource laden, bevor das Fenster aktiviert wird — sonst
                // flackert der Splash kurz ohne Logo (BitmapImage.SetSourceAsync ist asynchron).
                splash = new SplashWindow();
                await splash.LoadEmbeddedLogoAsync();
                splash.Activate();
                SplashLifetimeController splashLifetime = new();

                await InitializeHostAndDatabaseAsync();
                int dbPurgeDays = await RefreshAppSettingsAsync();
                SchedulePurgeInBackground(dbPurgeDays);

                // Hintergrunddienste vor dem Fenster anstoßen.
                ThemeService themeService = await StartBackgroundServicesAsync();

                // Startup-Validierung + Update-Check.
                _startupResult = await RunStartupValidationAsync(splash);

                MainWindow = _window = new MainWindow();
                _window.Closed += OnWindowClosed;

                // RequestedTheme konnte in InitializeAsync() nicht gesetzt werden,
                // da das Fenster zu diesem Zeitpunkt noch nicht existierte.
                // Jetzt, wo Content verfügbar ist, den Wert nachliefern.
                themeService.SyncRequestedTheme();

                _window.Activate();

                // Mindestanzeigedauer einhalten, damit der Splash bei warmem Cache nicht aufflackert.
                await splashLifetime.WaitForMinimumDurationAsync();
                splash.Close();

                // Cover-Hintergrund-Loop erst nach sichtbarem Hauptfenster starten.
                // Der Splash hat nur die Serien-Cover nachgeladen; Folgen-Cover, ID3-Parsing
                // und Provider-URL-Downloads für Episoden laufen danach progressiv im Hintergrund.
                Services.GetRequiredService<BackgroundCoverService>().Start();
            }
            catch (Exception ex)
            {
                await HandleStartupFailureAsync(splash, ex);
            }
        }

        /// <summary>
        /// Erstellt den Host (falls noch nicht vorhanden), initialisiert den Logger
        /// und applied DB-Migrationen mit dem VACUUM-INTO-Backup. Ausgelagerte Phase
        /// aus <see cref="OnLaunched"/>.
        /// </summary>
        private async Task InitializeHostAndDatabaseAsync()
        {
            _host ??= CreateHost();

            // LoggerManager initialisieren – Cleanup läuft beim Dispose (App-Ende)
            _loggerManager = Services.GetRequiredService<LoggerManager>();
            _appLogger = _loggerManager.Factory.CreateLogger("App");
            _appLogger.Info("Anwendung gestartet");

            // Migrationen einmalig beim Start anwenden – kein Datenbankzugriff ohne aktuelles Schema.
            // Eigener Scope, weil DatabaseInitializer Scoped ist (DbContext-Lifetime).
            using IServiceScope dbScope = Services.CreateScope();
            DatabaseInitializer dbInit = dbScope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
            await dbInit.InitializeAsync();
        }

        /// <summary>
        /// Liest die persistierten AppSettings, applied Logger-Retention und MinLevel,
        /// und schreibt LastAppStart zurück. Liefert die DbPurgeDays für den
        /// nachfolgenden Wartungs-Task. Ausgelagerte Phase aus <see cref="OnLaunched"/>.
        /// </summary>
        /// <returns>Die konfigurierte DB-Purge-Aufbewahrungsdauer in Tagen.</returns>
        private async Task<int> RefreshAppSettingsAsync()
        {
            using IServiceScope settingsScope = Services.CreateScope();
            EchoPlay.Data.Services.Interfaces.IAppSettingsDataService settingsService =
                settingsScope.ServiceProvider.GetRequiredService<EchoPlay.Data.Services.Interfaces.IAppSettingsDataService>();
            EchoPlay.Data.Entities.Settings.AppSettings appSettings = await settingsService.GetAsync();

            _loggerManager?.UpdateRetentionDays(appSettings.LogRetentionDays);
            _loggerManager?.UpdateMinimumLevel(appSettings.MinimumLogLevel);

            IClock clock = Services.GetRequiredService<IClock>();
            appSettings.LastAppStart = clock.UtcNow;
            await settingsService.SaveAsync(appSettings);

            return appSettings.DbPurgeDays;
        }

        /// <summary>
        /// Plant den DB-Wartungs-Task im Hintergrund. Kein kritischer Pfad — darf den
        /// Start nicht verzögern. Fehler werden geloggt, aber nicht durchgereicht.
        /// </summary>
        /// <param name="dbPurgeDays">Aufbewahrungsdauer für Soft-Delete-Einträge.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Hintergrund-Purge: SQLite-Locks oder IO-Fehler während VACUUM dürfen den App-Start nicht stören — wird beim nächsten Start erneut versucht.")]
        private void SchedulePurgeInBackground(int dbPurgeDays)
        {
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
                    _appLogger?.Warning("DB-Purge fehlgeschlagen: {Reason}", ex.Message);
                }
            });
        }

        /// <summary>
        /// Startet die Hintergrund-Services, die vor dem Hauptfenster laufen sollen,
        /// und initialisiert das Theme. Ausgelagerte Phase aus <see cref="OnLaunched"/>
        /// für bessere Stepdown-Lesbarkeit (ein Verantwortungsbereich pro Methode).
        /// </summary>
        /// <returns>Der initialisierte Theme-Service für späteres SyncRequestedTheme nach Window.Activate.</returns>
        private async Task<ThemeService> StartBackgroundServicesAsync()
        {
            // Provider-ID-Enrichment: ergänzt fehlende SpotifyAlbumId/AppleMusicAlbumId.
            // Kein Dateisystem-Scan, kein Cover-Download – läuft nebenläufig ohne den Splash zu verlangsamen.
            Services.GetRequiredService<BackgroundProviderIdService>().Start();

            // Theme vor dem Fenster-Öffnen setzen, damit kein Flackern entsteht.
            ThemeService themeService = Services.GetRequiredService<ThemeService>();
            await themeService.InitializeAsync();
            return themeService;
        }

        /// <summary>
        /// Führt die Startup-Validierung (Online/Lokal/Cache/Neuerscheinungen) und den
        /// Update-Check aus. Statustext läuft in den Splash. Ausgelagerte Phase aus
        /// <see cref="OnLaunched"/>.
        /// </summary>
        /// <param name="splash">Das aktive Splash-Fenster für Statustext-Updates.</param>
        /// <returns>Das Validation-Ergebnis für das Dashboard-VM.</returns>
        private async Task<StartupResult> RunStartupValidationAsync(SplashWindow splash)
        {
            // Startup-Validierung: Online-Check, Lokal-Check, Cache-Bereinigung, Neuerscheinungen-Refresh.
            // Läuft komplett im Splash, damit das Dashboard sofort aktuelle Daten anzeigen kann.
            IStartupValidator startupValidator = Services.GetRequiredService<IStartupValidator>();
            StartupResult result = await startupValidator.ValidateAsync(status => splash.SetStatus(status));

            _appLogger?.Info(
                "Startup-Validierung abgeschlossen: Online={IsOnlineAvailable}, Lokal={IsLocalLibraryAvailable}",
                result.IsOnlineAvailable, result.IsLocalLibraryAvailable);

            // Update-Check: prüft, ob eine neuere Version auf GitHub verfügbar ist.
            // Läuft nach der Validierung, blockiert maximal 5 Sekunden (RequestTimeout in UpdateCheckService).
            await CheckForUpdateAsync(splash);

            return result;
        }

        /// <summary>
        /// Zeigt dem Nutzer einen Fehlerdialog, wenn der App-Start nicht abgeschlossen werden konnte,
        /// und beendet die Anwendung kontrolliert. Loggt in Trace und – falls verfügbar – über den
        /// App-Logger, damit der Fehler auch ohne sichtbares Hauptfenster nachvollziehbar ist.
        /// </summary>
        /// <param name="splash">Das Splash-Fenster, sofern bereits erzeugt.</param>
        /// <param name="exception">Die während <see cref="OnLaunched"/> geworfene Exception.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Letzte Fehlerbehandlungsstufe beim App-Start: Logger-, Dialog- und Splash-Close-Fehler dürfen den kontrollierten Shutdown (Exit) nicht verhindern, unabhängig vom konkreten Exception-Typ.")]
        private async Task HandleStartupFailureAsync(SplashWindow? splash, Exception exception)
        {
            // Notfall-Logging via Trace-Wrapper (Logger eventuell noch nicht initialisiert)
            EchoPlay.Logger.Core.EmergencyTrace.Log($"[FATAL OnLaunched] {exception}");
            try { _appLogger?.Fatal($"OnLaunched fehlgeschlagen: {exception.Message}", exception); }
            catch { /* Logger-Fehler dürfen den Fehlerdialog nicht verhindern */ }

            // Fallback-Dialog am Splash zeigen, damit der Nutzer eine Rückmeldung sieht.
            try
            {
                if (splash?.Content?.XamlRoot is not null)
                {
                    string messageFormat = EchoPlay.App.Helpers.SafeResourceLoader.Get("AppStartFailedDialogMessage");
                    ContentDialog errorDialog = new()
                    {
                        Title = EchoPlay.App.Helpers.SafeResourceLoader.Get("AppStartFailedDialogTitle"),
                        Content = string.Format(System.Globalization.CultureInfo.CurrentCulture, messageFormat, exception.Message),
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

            try { splash?.Close(); } catch { /* Schließen darf das Exit nicht blockieren */ }

            Exit();
        }

        /// <summary>
        /// Wird beim Schließen des Hauptfensters aufgerufen.
        /// Führt Cleanup durch und gibt Ressourcen frei.
        /// </summary>
        /// <param name="sender">Das geschlossene Fenster.</param>
        /// <param name="args">Event-Argumente.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cleanup beim Fensterschließen: SQLite-Optimize und Background-Service-Stopps sind optional und dürfen den Shutdown nicht blockieren, unabhängig davon welche Exception aus DbContext, Maintenance-Service oder Background-Iteration geworfen wird.")]
        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            _appLogger?.Info("Anwendung wird beendet");

            // Globale Exception-Hooks wieder abmelden, damit nach Host-Dispose keine
            // Fatal-Logs mehr auf bereits entsorgte Sinks zugreifen.
            AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

            // Hintergrund-Services graceful stoppen, bevor der Host die Singletons disposed.
            // Jeder Service bekommt 5 Sekunden, um seine laufende Iteration zu beenden —
            // danach wird hart abgebrochen (StopAsync loggt die Überschreitung selbst).
            // Sync-over-async ist hier der einzige Weg, weil OnWindowClosed im Shutdown nicht
            // async sein kann und kein UI-SynchronizationContext mehr existiert (kein Deadlock).
            try
            {
                TimeSpan stopTimeout = TimeSpan.FromSeconds(5);
                _host!.Services.GetRequiredService<BackgroundCoverService>()
                    .StopAsync(stopTimeout).GetAwaiter().GetResult();
                _host!.Services.GetRequiredService<BackgroundProviderIdService>()
                    .StopAsync(stopTimeout).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _appLogger?.Warning("Stopp der Hintergrund-Services fehlgeschlagen: {Reason}", ex.Message);
            }

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
            // DisposeAsync läuft den Async-Dispose-Pfad: PlayerService.DisposeAsync sichert
            // die Abspielposition ohne Sync-over-Async-Knoten in der Service-Logik.
            // IHost selbst deklariert nur IDisposable — die konkrete Host-Implementierung implementiert IAsyncDisposable.
            if (_host is IAsyncDisposable asyncHost)
            {
                asyncHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            else
            {
                _host?.Dispose();
            }

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
                // Fallback falls Logger noch nicht initialisiert – EmergencyTrace kapselt
                // bewusst Trace.WriteLine, damit die Ausgabe auch im Release-Build erhalten bleibt.
                EchoPlay.Logger.Core.EmergencyTrace.Log($"[FATAL] Nicht behandelte Exception: {e.Exception}");
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
                    string messageFormat = EchoPlay.App.Helpers.SafeResourceLoader.Get("UnexpectedErrorMessage");
                    await errorDialog.ShowAsync(
                        EchoPlay.App.Helpers.SafeResourceLoader.Get("UnexpectedErrorTitle"),
                        string.Format(System.Globalization.CultureInfo.CurrentCulture, messageFormat, e.Message));
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // Ein anderer ContentDialog ist bereits offen – Fehlerdialog verwerfen.
                    // Der Fehler wurde bereits geloggt, der Nutzer muss nicht zweimal informiert werden.
                }
            }
        }

        /// <summary>
        /// Behandelt Exceptions, die von Nicht-UI-Threads ausgelöst werden (<c>Task.Run</c>,
        /// <c>Thread</c>, <c>ThreadPool</c>). WinUIs <see cref="OnUnhandledException"/> sieht diese nicht.
        /// </summary>
        /// <param name="sender">Quelle der Exception (typischerweise <see cref="AppDomain"/>).</param>
        /// <param name="e">Enthält die Exception und das <c>IsTerminating</c>-Flag.</param>
        private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
            => EchoPlay.App.Infrastructure.FatalExceptionHandler.HandleDomainException(_appLogger, e);

        /// <summary>
        /// Behandelt Exceptions aus <see cref="Task"/>s, deren Ergebnis nie per <c>await</c>
        /// konsumiert wurde (z. B. <c>_ = Task.Run(...)</c> ohne Fehlerbehandlung im Body).
        /// </summary>
        /// <param name="sender">Der <see cref="TaskScheduler"/>, der das Event meldet.</param>
        /// <param name="e">Enthält die Exception und ermöglicht <c>SetObserved()</c>, um den Crash zu verhindern.</param>
        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
            => EchoPlay.App.Infrastructure.FatalExceptionHandler.HandleUnobservedTaskException(_appLogger, e);

        /// <summary>
        /// Prüft ob eine neuere Version auf GitHub verfügbar ist und zeigt ggf. einen Update-Dialog.
        /// Wird nach dem Splash aufgerufen, bevor das Hauptfenster geöffnet wird.
        /// </summary>
        /// <param name="splash">Das aktive Splash-Fenster für den XamlRoot des Dialogs.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Update-Check ist nicht kritischer Pfad: GitHub-HTTP-Fehler, JSON-Parse-Fehler oder Dialog-Probleme dürfen den App-Start nicht blockieren.")]
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

                _appLogger?.Info("Neue Version verfügbar: {Version}", update.Version);

                // Lokalisierte Strings über SafeResourceLoader (Windows-App-SDK-Loader) — der
                // WinRT-ResourceLoader crasht im unpackaged-Betrieb.
                System.Text.CompositeFormat messageFormat = System.Text.CompositeFormat.Parse(
                    EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateAvailableMessage"));
                string dialogContent = string.Format(
                    System.Globalization.CultureInfo.CurrentCulture, messageFormat, update.Version)
                    + (string.IsNullOrWhiteSpace(update.ReleaseNotes)
                        ? string.Empty
                        : "\n\n" + update.ReleaseNotes);

                // Drei-Optionen-Dialog: Jetzt aktualisieren / Später / Version überspringen
                ContentDialog dialog = new()
                {
                    Title = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateAvailableTitle"),
                    Content = dialogContent,
                    PrimaryButtonText = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateNowButton"),
                    SecondaryButtonText = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateLaterButton"),
                    CloseButtonText = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateSkipButton"),
                    XamlRoot = splash.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Primary
                };

                ContentDialogResult result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    // Download und Installation starten
                    splash.SetStatus(EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateDownloadingMessage"));

                    UpdateDownloadService downloadService = Services.GetRequiredService<UpdateDownloadService>();
                    bool success = await downloadService.DownloadAndInstallAsync(
                        update.DownloadUrl,
                        update.Version,
                        update.FileSizeBytes,
                        update.ExpectedSha256);

                    if (success)
                    {
                        // Installer gestartet → App beenden
                        Exit();
                        return;
                    }

                    // Download fehlgeschlagen → Hinweis, dann normal weiter
                    IErrorDialogService errorDialog = Services.GetRequiredService<IErrorDialogService>();
                    await errorDialog.ShowAsync(
                        EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateDownloadFailedTitle"),
                        EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateDownloadFailedMessage"));
                }
                else if (result == ContentDialogResult.None)
                {
                    // "Version überspringen" → in DB merken
                    await updateCheckService.SkipVersionAsync(update.Version);
                    _appLogger?.Info("Version {Version} übersprungen", update.Version);
                }
            }
            catch (Exception ex)
            {
                // Update-Check darf den App-Start niemals blockieren
                _appLogger?.Warning("Update-Check fehlgeschlagen: {Reason}", ex.Message);
            }
        }

        /// <summary>
        /// Registriert <see cref="CoverService"/> als Singleton und mappt
        /// <see cref="ICoverService"/> auf DIESELBE konkrete Instanz.
        /// </summary>
        /// <remarks>
        /// Die Factory löst bewusst den KONKRETEN <see cref="CoverService"/> auf, nicht
        /// <see cref="ICoverService"/> selbst — eine Selbst-Auflösung würde endlos
        /// rekursieren (der DI-Container erkennt nur Konstruktor-, keine Factory-Zyklen)
        /// und den App-Start einfrieren. Bewusst ausgelagert, damit die Registrierung
        /// per Test aufgelöst und gegen diese Regression abgesichert werden kann.
        /// Gleiches Interface→Konkret-Muster wie ThemeService/NavigationService/PlayerService.
        /// </remarks>
        /// <param name="services">Die zu befüllende Service-Collection.</param>
        internal static void RegisterCoverService(IServiceCollection services)
        {
            _ = services.AddSingleton<CoverService>();
            _ = services.AddSingleton<ICoverService>(provider => provider.GetRequiredService<CoverService>());
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
            // Absoluter Pfad ab dem App-Verzeichnis — sonst löst der relative Dateiname
            // gegen das aktuelle Arbeitsverzeichnis auf und der Start scheitert mit
            // FileNotFoundException, sobald die App nicht aus ihrem Installationsordner
            // heraus gestartet wird (z. B. über die Kommandozeile).
            _ = builder.Configuration
                .AddJsonFile(System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false);

            // Microsoft-seitige HttpClient-/Resilience-Logs auf Warning dämpfen.
            // EchoPlay loggt jede HTTP-Anfrage und Antwort selbst über HttpRequestLoggingHandler;
            // die Default-Zeilen der LogicalHandler/ClientHandler und das Polly-"Handled: False"
            // würden sonst jede Request vierfach und ohne Mehrwert im Output-Fenster ausgeben.
            _ = builder.Logging
                .AddFilter("System.Net.Http.HttpClient", Microsoft.Extensions.Logging.LogLevel.Warning)
                .AddFilter("Microsoft.Extensions.Http", Microsoft.Extensions.Logging.LogLevel.Warning)
                .AddFilter("Microsoft.Extensions.Http.Resilience", Microsoft.Extensions.Logging.LogLevel.Warning)
                .AddFilter("Polly", Microsoft.Extensions.Logging.LogLevel.Warning);

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
            // ihren Client über IHttpClientFactory, statt eigene statische Instanzen
            // zu halten. Damit greifen einheitliche Timeouts, User-Agent-Header und
            // einheitliche Polly-Resilience-Policies an einer Stelle.
            Microsoft.Extensions.DependencyInjection.IHttpClientBuilder coverDownloadBuilder = builder.Services.AddHttpClient("CoverDownload", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-CoverDownload/1.0");
            });
            _ = coverDownloadBuilder.AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            });
            AttachRequestLogging(coverDownloadBuilder);

            Microsoft.Extensions.DependencyInjection.IHttpClientBuilder onlineCheckBuilder = builder.Services.AddHttpClient("OnlineCheck", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-OnlineCheck/1.0");
            });
            AttachRequestLogging(onlineCheckBuilder);

            Microsoft.Extensions.DependencyInjection.IHttpClientBuilder updateDownloadBuilder = builder.Services.AddHttpClient("UpdateDownload", client =>
            {
                // Großzügiges Timeout für den großen Setup-Download (self-contained, ~80 MB)
                // auch auf langsamen Verbindungen.
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-UpdateDownload/1.0");
            });
            // Bewusst KEIN StandardResilienceHandler: Dessen Per-Versuch-Timeout ist für kleine,
            // gepufferte API-Aufrufe gedacht und müsste einen großen Streaming-Download in ein
            // Attempt-Fenster zwängen. Zudem verlangt der eingebaute Circuit-Breaker
            // SamplingDuration >= 2×AttemptTimeout — eine hier übersehene Kopplung, die erst beim
            // ersten Benutzen des Clients als OptionsValidationException zuschlägt und den Download
            // komplett scheitern lässt (Update-Dialog erscheint, Installation bleibt aus). Ein
            // fehlgeschlagener Download wird beim nächsten Start erneut angeboten.
            AttachRequestLogging(updateDownloadBuilder);

            Microsoft.Extensions.DependencyInjection.IHttpClientBuilder updateCheckBuilder = builder.Services.AddHttpClient("UpdateCheck", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay-UpdateCheck/1.0");
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            });
            _ = updateCheckBuilder.AddStandardResilienceHandler(options =>
            {
                // GitHub-API kann auf Rate-Limits (429) oder kurzzeitige 5xx reagieren.
                // Retry mit Jitter deckt beides ab, ohne die App beim Start zu blockieren.
                options.Retry.MaxRetryAttempts = 2;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
            });
            AttachRequestLogging(updateCheckBuilder);

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
            _ = builder.Services.AddSingleton<ISpotifyClientCredentialsProvider, SpotifyClientCredentialsProvider>();

            // Named HttpClient für Token-Anfragen (Client-Credentials-Flow).
            // Timeout kurz halten – ein hängender Token-Request blockiert jeden weiteren API-Call.
            Microsoft.Extensions.DependencyInjection.IHttpClientBuilder spotifyTokenBuilder = builder.Services.AddHttpClient("SpotifyToken", client =>
            {
                client.BaseAddress = new(baseSpotifyOptions.AuthBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(10);
            });
            // Standard-Resilience (Retry + Exponential-Backoff + CircuitBreaker) wie bei allen
            // externen Clients – ein transienter Fehler beim Token-Holen darf den Import nicht sofort abbrechen.
            _ = spotifyTokenBuilder.AddStandardResilienceHandler();
            AttachRequestLogging(spotifyTokenBuilder);

            // SpotifyTokenClient als Singleton: Der Token-Cache lebt prozessweit, parallele
            // Anforderungen werden intern per SemaphoreSlim serialisiert. Credentials werden
            // erst beim ersten GetAccessTokenAsync-Call asynchron geholt — kein Sync-over-Async.
            _ = builder.Services.AddSingleton<SpotifyTokenClient>();

            // Der AuthMessageHandler wird als Transient registriert,
            // damit jede HttpClient-Instanz ihren eigenen Handler erhält.
            _ = builder.Services.AddTransient<SpotifyAuthMessageHandler>();

            // Named HttpClient für Spotify-Web-API mit automatischer Authentifizierung.
            Microsoft.Extensions.DependencyInjection.IHttpClientBuilder spotifyApiBuilder = builder.Services.AddHttpClient("SpotifyApi", client =>
            {
                client.BaseAddress = new(baseSpotifyOptions.ApiBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(15);
            });
            // Resilience als ÄUSSERER Handler (zuerst registriert), Auth-Handler INNEN: so bekommt jeder
            // Retry-Versuch über AttachBearerToken einen frischen Token. StandardResilienceHandler retryt
            // nur 408/429/5xx/Timeout – der 401-Refresh im AuthMessageHandler bleibt davon unberührt.
            _ = spotifyApiBuilder.AddStandardResilienceHandler();
            _ = spotifyApiBuilder.AddHttpMessageHandler<SpotifyAuthMessageHandler>();
            AttachRequestLogging(spotifyApiBuilder);

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
                    ["musicbrainz.org"] = TimeSpan.FromSeconds(1),
                    ["coverartarchive.org"] = TimeSpan.FromSeconds(1),
                    ["itunes.apple.com"] = TimeSpan.FromMilliseconds(1500),
                    ["api.discogs.com"] = TimeSpan.FromSeconds(1),
                }));

            // HTTP-Handler, der den IHostRateLimiter in die HttpClient-Pipeline einhängt.
            // Muss transient sein, damit IHttpClientFactory pro Client eine eigene Instanz erzeugt.
            _ = builder.Services.AddTransient<RateLimitMessageHandler>();

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
            RegisterCoverService(builder.Services);
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
            _ = builder.Services.AddSingleton<UpdateInteractionService>();

            // Zentrales Picker-Setup für FolderPicker/FileOpen/FileSave; entfernt Boilerplate
            // aus den Pages und kapselt die WinUI3-Window-Handle-Initialisierung.
            _ = builder.Services.AddSingleton<IFilePickerService, FilePickerService>();

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
                provider.GetRequiredService<ICoverService>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<BackgroundCoverService>()));
            _ = builder.Services.AddTransient<MediathekOnlineViewModel>(provider => new MediathekOnlineViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IConfirmationDialogService>(),
                provider.GetRequiredService<ImportService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<IOnlineAccessGuard>(),
                provider.GetRequiredService<IHttpClientFactory>(),
                provider.GetRequiredService<EpisodeCoverCacheService>(),
                provider.GetRequiredService<ICoverService>(),
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
                    provider.GetRequiredService<ICoverService>(),
                    provider.GetRequiredService<IWatchToggleService>(),
                    provider.GetRequiredService<IPageModeGuard>(),
                    provider.GetRequiredService<IFolderRestructureCoordinator>(),
                    provider.GetRequiredService<IMissingEpisodesCoordinator>(),
                    provider.GetRequiredService<IEpisodeCoverCoordinator>(),
                    provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>().CreateLogger("MediathekLokal"))));
            _ = builder.Services.AddTransient<SucheViewModel>(provider => new SucheViewModel(
                provider.GetRequiredService<ImportService>(),
                provider.GetRequiredService<IErrorDialogService>(),
                provider.GetRequiredService<ILocalizationService>(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<INavigationService>(),
                provider.GetRequiredService<IPageModeGuard>(),
                provider.GetRequiredService<BackgroundCoverService>()));
            _ = builder.Services.AddTransient<PlayerViewModel>();
            _ = builder.Services.AddTransient<SeriesDetailViewModel>();
            // App-Services für Settings: Verbindungstest und Log-Viewer sind als eigenständige
            // Coordinators implementiert, damit das SettingsViewModel stateless Logik nicht selbst trägt.
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

            // Resilience-Rollout: Standard-Policies (Retry + Timeout + CircuitBreaker)
            // an alle typed HttpClients aus LocalLibrary/AppleMusic/TagManager anhängen.
            // AddHttpClient<T>() ist additiv — BaseAddress/Timeout/UserAgent aus den Extensions bleiben erhalten.
            // Reihenfolge: ResilienceHandler zuerst (wrappt als äußerer Handler), RateLimitHandler danach
            // (innen). Damit wartet jeder Retry-Versuch erneut am IHostRateLimiter — wir verletzen
            // die API-Quota auch bei wiederholten Anfragen nicht.
            // MusicBrainzLookupService ist internal; der Named Client heißt hier per Konvention wie
            // das TClient-Interface ("ITagLookupService").
            AttachResilience(builder.Services.AddHttpClient<IAppleMusicSearchClient, AppleMusicSearchClient>(), withRateLimit: true);
            AttachResilience(builder.Services.AddHttpClient<EchoPlay.LocalLibrary.Cover.CoverService>(), withRateLimit: false);
            AttachResilience(builder.Services.AddHttpClient<EchoPlay.LocalLibrary.Cover.CoverArtArchiveSearchService>(), withRateLimit: true);
            AttachResilience(builder.Services.AddHttpClient<EchoPlay.LocalLibrary.Cover.ITunesCoverSearchService>(), withRateLimit: true);
            AttachResilience(builder.Services.AddHttpClient<EchoPlay.LocalLibrary.Cover.DeezerArtistCoverSearchService>(), withRateLimit: false);
            AttachResilience(builder.Services.AddHttpClient<EchoPlay.LocalLibrary.Cover.DeezerAlbumCoverSearchService>(), withRateLimit: false);
            AttachResilience(builder.Services.AddHttpClient<EchoPlay.LocalLibrary.Cover.DiscogsCoverSearchService>(), withRateLimit: true);
            AttachResilience(builder.Services.AddHttpClient("ITagLookupService"), withRateLimit: true);

            return builder.Build();

            static void AttachResilience(Microsoft.Extensions.DependencyInjection.IHttpClientBuilder clientBuilder, bool withRateLimit)
            {
                _ = clientBuilder.AddStandardResilienceHandler();
                if (withRateLimit)
                {
                    _ = clientBuilder.AddHttpMessageHandler<RateLimitMessageHandler>();
                }
                AttachRequestLogging(clientBuilder);
            }

            // Hängt den HttpRequestLoggingHandler als innersten Handler an den HttpClient.
            // Damit protokolliert jede einzelne Retry-Wiederholung eine eigene Request-/Response-Zeile
            // mit Methode, URL (redigiert), Status, Dauer und Client-Name.
            static void AttachRequestLogging(Microsoft.Extensions.DependencyInjection.IHttpClientBuilder clientBuilder)
            {
                string clientName = clientBuilder.Name;
                _ = clientBuilder.AddHttpMessageHandler(provider =>
                    new HttpRequestLoggingHandler(
                        provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>(),
                        clientName));
            }
        }
    }
}

using EchoPlay.App.Composition;
using EchoPlay.App.Services;
using EchoPlay.App.Startup;
using EchoPlay.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App
{
    /// <summary>
    /// Einstiegspunkt der EchoPlay-Anwendung.
    ///
    /// Die Klasse hält den Lebenszyklus zusammen: Sie ruft den Startlauf ab, öffnet das
    /// Hauptfenster, fängt Fehler ab, die sonst niemand mehr sieht, und stößt das
    /// geordnete Beenden an. Der Aufbau des Dienst-Containers steht daneben in
    /// <see cref="AppHostFactory"/>, die Abschnitte des Startlaufs in
    /// <see cref="StartupSequence"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "WinUI-3-Entry-Point-Typ heißt per Konvention 'App' im 'EchoPlay.App'-Namespace.")]
    public partial class App : Application
    {
        private static IHost? _host;
        private static StartupResult? _startupResult;
        private Window? _window;
        private StartupSequence? _startup;

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

        /// <summary>Der Protokollkanal der Anwendung, sobald der Startlauf ihn eingerichtet hat.</summary>
        private EchoPlay.Logger.Abstractions.ILogger? AppLogger => _startup?.Logger;

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
        /// Registriert den <see cref="CoverService"/> und bildet <see cref="ICoverService"/>
        /// auf dieselbe Instanz ab.
        /// </summary>
        /// <remarks>
        /// Bleibt als Einstieg für den Test erhalten, der genau diese Verdrahtung gegen
        /// eine Endlos-Rekursion absichert. Die Registrierung selbst steht bei den übrigen
        /// Cover-Diensten in <see cref="AppServiceRegistration"/>.
        /// </remarks>
        /// <param name="services">Die zu befüllende Dienstsammlung.</param>
        internal static void RegisterCoverService(IServiceCollection services)
            => AppServiceRegistration.RegisterCoverService(services);

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

                _host ??= AppHostFactory.Create();
                _startup = await StartupSequence.BeginAsync(StartupSequenceContext.From(Services));

                int dbPurgeDays = await _startup.ApplyAppSettingsAsync();
                _startup.SchedulePurgeInBackground(dbPurgeDays);

                // Hintergrunddienste vor dem Fenster anstoßen.
                ThemeService themeService = await _startup.StartBackgroundServicesAsync();

                // Startup-Validierung + Update-Check.
                _startupResult = await _startup.RunValidationAsync(splash);

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
            try { AppLogger?.Fatal($"OnLaunched fehlgeschlagen: {exception.Message}", exception); }
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
        /// Meldet die globalen Fanglinien ab und übergibt an die Beenden-Folge.
        /// </summary>
        /// <param name="sender">Das geschlossene Fenster.</param>
        /// <param name="args">Event-Argumente.</param>
        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            AppLogger?.Info("Anwendung wird beendet");

            // Globale Exception-Hooks wieder abmelden, damit nach Host-Dispose keine
            // Fatal-Logs mehr auf bereits entsorgte Sinks zugreifen.
            AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

            ShutdownSequence.Run(_host!, AppLogger);

            // Sicherheits-Dispose: greift, falls der LoggerManager nicht über den DI-Container
            // freigegeben wird (z.B. bei nicht-standard Registrierung).
            _startup?.LoggerManager.Dispose();
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

            if (AppLogger is not null)
            {
                AppLogger.Fatal($"Nicht behandelte Exception: {e.Message}", e.Exception);
            }
            else
            {
                // Fallback falls Logger noch nicht initialisiert – EmergencyTrace kapselt
                // bewusst Trace.WriteLine, damit die Ausgabe auch im Release-Build erhalten bleibt.
                EchoPlay.Logger.Core.EmergencyTrace.Log($"[FATAL] Nicht behandelte Exception: {e.Exception}");
            }

            await ShowUnhandledExceptionDialogAsync(e.Message);
        }

        /// <summary>
        /// Zeigt den Fehlerdialog zu einer nicht behandelten Exception — aber nur, wenn das
        /// Hauptfenster schon offen ist.
        /// </summary>
        /// <remarks>
        /// WinUI 3 erlaubt je Zeichenwurzel nur einen offenen Dialog. Stammt die
        /// ursprüngliche Exception selbst aus einem fehlgeschlagenen Dialog, würde ein
        /// zweiter Aufruf dieselbe COM-Exception werfen — eine endlose Kaskade.
        /// </remarks>
        /// <param name="message">Die Meldung der Exception.</param>
        private static async Task ShowUnhandledExceptionDialogAsync(string message)
        {
            if (MainWindow is null)
            {
                return;
            }

            try
            {
                ErrorDialogService errorDialog = Services.GetRequiredService<ErrorDialogService>();
                string messageFormat = EchoPlay.App.Helpers.SafeResourceLoader.Get("UnexpectedErrorMessage");
                await errorDialog.ShowAlwaysAsync(
                    EchoPlay.App.Helpers.SafeResourceLoader.Get("UnexpectedErrorTitle"),
                    string.Format(System.Globalization.CultureInfo.CurrentCulture, messageFormat, message));
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Ein anderer ContentDialog ist bereits offen – Fehlerdialog verwerfen.
                // Der Fehler wurde bereits geloggt, der Nutzer muss nicht zweimal informiert werden.
            }
        }

        /// <summary>
        /// Behandelt Exceptions, die von Nicht-UI-Threads ausgelöst werden (<c>Task.Run</c>,
        /// <c>Thread</c>, <c>ThreadPool</c>). WinUIs <see cref="OnUnhandledException"/> sieht diese nicht.
        /// </summary>
        /// <param name="sender">Quelle der Exception (typischerweise <see cref="AppDomain"/>).</param>
        /// <param name="e">Enthält die Exception und das <c>IsTerminating</c>-Flag.</param>
        private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
            => EchoPlay.App.Infrastructure.FatalExceptionHandler.HandleDomainException(AppLogger, e);

        /// <summary>
        /// Behandelt Exceptions aus <see cref="Task"/>s, deren Ergebnis nie per <c>await</c>
        /// konsumiert wurde (z. B. <c>_ = Task.Run(...)</c> ohne Fehlerbehandlung im Body).
        /// </summary>
        /// <param name="sender">Der <see cref="TaskScheduler"/>, der das Event meldet.</param>
        /// <param name="e">Enthält die Exception und ermöglicht <c>SetObserved()</c>, um den Crash zu verhindern.</param>
        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
            => EchoPlay.App.Infrastructure.FatalExceptionHandler.HandleUnobservedTaskException(AppLogger, e);
    }
}

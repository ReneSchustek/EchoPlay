using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Core;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace EchoPlay.App.Startup
{
    /// <summary>
    /// Der Startlauf der Anwendung in seinen Abschnitten: Protokollierung einrichten,
    /// Datenbankschema nachziehen, Einstellungen anwenden, Wartung einplanen,
    /// Hintergrunddienste anwerfen und die Prüfungen im Startbild durchlaufen.
    /// </summary>
    /// <remarks>
    /// Die Reihenfolge steckt im Aufrufer (<see cref="App.OnLaunched"/>), die Arbeit je
    /// Abschnitt hier. Damit bleibt der Einstiegspunkt eine lesbare Abfolge, und jeder
    /// Abschnitt hat genau eine Aufgabe.
    /// </remarks>
    internal sealed class StartupSequence
    {
        private readonly StartupSequenceContext _context;

        private StartupSequence(StartupSequenceContext context, ILogger logger)
        {
            _context = context;
            Logger = logger;
        }

        /// <summary>Der Protokoll-Verwalter der Anwendung; wird beim Beenden freigegeben.</summary>
        public LoggerManager LoggerManager => _context.LoggerManager;

        /// <summary>Der Protokollkanal der Anwendungsschicht.</summary>
        public ILogger Logger { get; }

        /// <summary>
        /// Richtet die Protokollierung ein und bringt das Datenbankschema auf Stand. Erst
        /// danach darf irgendetwas auf die Datenbank zugreifen.
        /// </summary>
        /// <param name="context">Die aufgelösten Dienste des Startlaufs.</param>
        /// <returns>Der begonnene Startlauf.</returns>
        public static async Task<StartupSequence> BeginAsync(StartupSequenceContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            ILogger logger = context.LoggerManager.Factory.CreateLogger("App");
            logger.Info("Anwendung gestartet");

            // Eigener Bereich, weil der Initialisierer scoped ist (Lebensdauer des
            // Datenbankkontexts).
            using IServiceScope dbScope = context.ScopeFactory.CreateScope();
            DatabaseInitializer initializer = dbScope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
            await initializer.InitializeAsync();

            return new StartupSequence(context, logger);
        }

        /// <summary>
        /// Liest die gespeicherten Einstellungen, wendet Sprache, Aufbewahrung und
        /// Protokollschwelle an und schreibt den Startzeitpunkt zurück.
        /// </summary>
        /// <returns>Die eingestellte Aufbewahrungsdauer für gelöschte Datensätze in Tagen.</returns>
        public async Task<int> ApplyAppSettingsAsync()
        {
            using IServiceScope scope = _context.ScopeFactory.CreateScope();
            IAppSettingsDataService settingsService =
                scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            AppSettings appSettings = await settingsService.GetAsync();

            // Die Sprachvorgabe wird bei jedem Start neu gesetzt. Ohne Paket-Identität
            // überlebt sie den Prozess nicht — ohne diese Zeile startet die Anwendung
            // trotz gewählter Sprache wieder in der Systemsprache, während die Auswahl
            // unbenutzt in der Datenbank liegt.
            _ = _context.LanguageSwitchService.ApplyOverride(appSettings.ActiveLanguage);

            // Die Lautstärke gilt ab dem ersten Ton — sie hier zu setzen ist früh genug,
            // weil vor dem Hauptfenster nichts abgespielt wird.
            _context.PlayerService.Volume = appSettings.Volume;
            _context.PlayerService.IsMuted = appSettings.IsMuted;

            LoggerManager.UpdateRetentionDays(appSettings.LogRetentionDays);
            LoggerManager.UpdateMinimumLevel(appSettings.MinimumLogLevel);

            appSettings.LastAppStart = _context.Clock.UtcNow;
            await settingsService.SaveAsync(appSettings);

            return appSettings.DbPurgeDays;
        }

        /// <summary>
        /// Plant die Datenbank-Wartung im Hintergrund. Kein kritischer Pfad — sie darf den
        /// Start nicht verzögern. Fehler werden protokolliert, aber nicht durchgereicht.
        /// </summary>
        /// <param name="dbPurgeDays">Aufbewahrungsdauer für gelöschte Datensätze in Tagen.</param>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Hintergrund-Purge: SQLite-Locks oder IO-Fehler während VACUUM dürfen den App-Start nicht stören — wird beim nächsten Start erneut versucht.")]
        public void SchedulePurgeInBackground(int dbPurgeDays)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using IServiceScope scope = _context.ScopeFactory.CreateScope();
                    IDatabaseMaintenanceService maintenance =
                        scope.ServiceProvider.GetRequiredService<IDatabaseMaintenanceService>();
                    await maintenance.PurgeAsync(dbPurgeDays);

                    // Der Hörstatus wird quellenübergreifend abgeglichen. Das läuft hier
                    // mit, weil es dieselbe Bedingung erfüllt: Wartungsarbeit, die den
                    // Start nicht aufhalten darf. Der Aufruf ist idempotent — nach dem
                    // ersten Lauf findet er nichts mehr und kostet eine Abfrage.
                    IPlaybackStateDataService playbackStates =
                        scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();
                    _ = await playbackStates.SynchronizeCompletionAcrossSourcesAsync();
                }
                catch (Exception ex)
                {
                    Logger.Warning("DB-Purge fehlgeschlagen: {Reason}", ex.Message);
                }
            });
        }

        /// <summary>
        /// Startet die Hintergrunddienste, die vor dem Hauptfenster laufen sollen, und
        /// richtet das Farbschema ein.
        /// </summary>
        /// <returns>
        /// Der eingerichtete Farbschema-Dienst. Der Aufrufer gleicht damit nach dem Öffnen
        /// des Fensters das angeforderte Schema nach — vorher gibt es kein Fenster, dem es
        /// zugewiesen werden könnte.
        /// </returns>
        public async Task<ThemeService> StartBackgroundServicesAsync()
        {
            // Ergänzt fehlende Anbieter-Kennungen. Kein Dateisystem-Durchlauf, kein
            // Cover-Download — läuft nebenher, ohne das Startbild aufzuhalten.
            _context.ProviderIdService.Start();

            // Das Farbschema wird vor dem Öffnen des Fensters gesetzt, damit nichts flackert.
            await _context.ThemeService.InitializeAsync();
            return _context.ThemeService;
        }

        /// <summary>
        /// Durchläuft die Prüfungen im Startbild (Erreichbarkeit, lokale Mediathek,
        /// Zwischenspeicher, Neuerscheinungen) und bietet danach eine gefundene
        /// Aktualisierung an.
        /// </summary>
        /// <param name="splash">Das Startbild; nimmt die Statuszeile und die Dialoge auf.</param>
        /// <returns>Das Ergebnis der Prüfungen für die Startseite.</returns>
        public async Task<StartupResult> RunValidationAsync(SplashWindow splash)
        {
            ArgumentNullException.ThrowIfNull(splash);

            // Läuft vollständig im Startbild, damit die Startseite gleich aktuelle Zahlen
            // zeigen kann.
            StartupResult result = await _context.Validator.ValidateAsync(status => splash.SetStatus(status));

            Logger.Info(
                "Startup-Validierung abgeschlossen: Online={IsOnlineAvailable}, Lokal={IsLocalLibraryAvailable}",
                result.IsOnlineAvailable, result.IsLocalLibraryAvailable);

            // Danach die Prüfung auf eine neuere Version. Sie blockiert höchstens so lange,
            // wie die Zeitgrenze des Aktualisierungs-Clients erlaubt.
            await _context.UpdateInteraction.PromptOnStartupAsync(splash.Content.XamlRoot, splash.SetStatus);

            return result;
        }
    }
}

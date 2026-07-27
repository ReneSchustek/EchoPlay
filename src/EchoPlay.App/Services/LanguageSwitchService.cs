using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standard-Implementierung von <see cref="ILanguageSwitchService"/>.
    /// </summary>
    /// <remarks>
    /// Beide WinRT-Wege der MSIX-Zeit funktionieren unpackaged nicht:
    /// <c>Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride</c> und
    /// <c>Microsoft.Windows.AppLifecycle.AppInstance.Restart</c> setzen eine Paket-Identität
    /// voraus und werfen sonst <see cref="InvalidOperationException"/>. Ersetzt durch die
    /// App-SDK-Variante <c>Microsoft.Windows.Globalization.ApplicationLanguages</c> (ausdrücklich
    /// für Apps ohne Paket-Identität) und einen Neustart über den eigenen Prozess.
    /// </remarks>
    public sealed class LanguageSwitchService : ILanguageSwitchService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IProcessLauncher _processLauncher;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Dienst.
        /// </summary>
        /// <param name="scopeFactory">Für den scoped Zugriff auf die Einstellungen.</param>
        /// <param name="processLauncher">Startet den Neustart-Prozess; in Tests ein Fake.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        public LanguageSwitchService(
            IServiceScopeFactory scopeFactory,
            IProcessLauncher processLauncher,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _processLauncher = processLauncher;
            _logger = loggerFactory.CreateLogger("LanguageSwitchService");
        }

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sprachpräferenz ist Komfort, kein Muss: schlägt der Plattformaufruf fehl (fehlende Paket-Identität, alte Runtime), startet die App in Systemsprache weiter statt gar nicht.")]
        public bool ApplyOverride(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return false;
            }

            try
            {
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageCode;
                _logger.Info("Sprachpräferenz gesetzt: {LanguageCode}.", languageCode);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning("Sprachpräferenz '{LanguageCode}' konnte nicht gesetzt werden: {Reason}", languageCode, ex.Message);
                return false;
            }
        }

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Der Neustart darf die App nicht abstürzen lassen: scheitert er (fehlende Prozesspfad-Info, blockierender Shutdown), bleibt die App offen und der Nutzer startet manuell neu – die Sprache ist bereits persistiert.")]
        public async Task<bool> ChangeLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return false;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();

            AppSettings settings = await settingsService.GetAsync(cancellationToken);
            settings.ActiveLanguage = languageCode;
            await settingsService.SaveAsync(settings, cancellationToken);

            // Reihenfolge zählt: erst persistieren, dann die Präferenz setzen. Scheitert der
            // Plattformaufruf, greift die Sprache trotzdem beim nächsten Start (Startpfad liest
            // ActiveLanguage und setzt die Präferenz erneut).
            _ = ApplyOverride(languageCode);

            string? executable = _processLauncher.CurrentExecutablePath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                _logger.Warning("Neustart nicht möglich – Prozesspfad unbekannt. Sprache greift beim nächsten manuellen Start.");
                return false;
            }

            // Eigener Prozess statt AppInstance.Restart: letzteres braucht Paket-Identität
            // und wirft im unpackaged-Betrieb, bevor der Neustart überhaupt beginnt.
            // Der Launcher verweigert den Start, wenn der Prozess nicht die App ist.
            if (!_processLauncher.Start(executable))
            {
                return false;
            }

            _logger.Info("Neustart für Sprachwechsel angestoßen: {LanguageCode}.", languageCode);

            try
            {
                Microsoft.UI.Xaml.Application.Current.Exit();
                return true;
            }
            catch (Exception ex)
            {
                // Der neue Prozess läuft bereits – das Beenden des alten darf nicht hart scheitern.
                _logger.Warning("Beenden nach Sprachwechsel fehlgeschlagen: {Reason}", ex.Message);
                return true;
            }
        }
    }
}

using EchoPlay.App.Helpers;
using EchoPlay.Logger.Abstractions;
using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Einzige Stelle im Produktivcode, die einen Prozess startet, der die eigene Anwendung
    /// sein kann. Der Namensabgleich verhindert, dass versehentlich ein fremder Host
    /// (etwa ein Testrunner) sich selbst startet.
    /// </summary>
    public sealed class ProcessLauncher : IProcessLauncher
    {
        // Nur dieser Prozessname darf sich selbst neu starten. Läuft der Code in einem
        // anderen Host, ist der Aufruf ein Fehler und wird verweigert statt ausgeführt.
        private const string AllowedProcessName = "EchoPlay.App";

        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Launcher.
        /// </summary>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        public ProcessLauncher(ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger("ProcessLauncher");
        }

        /// <inheritdoc/>
        public string? CurrentExecutablePath => Environment.ProcessPath;

        /// <inheritdoc/>
        public bool Start(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            string current = System.IO.Path.GetFileNameWithoutExtension(executablePath);
            if (!string.Equals(current, AllowedProcessName, StringComparison.OrdinalIgnoreCase))
            {
                // Schutzriegel gegen Selbstvermehrung: in einem Testhost oder fremden Prozess
                // wird nichts gestartet, egal was der Aufrufer übergibt.
                _logger.Warning(
                    "Prozessstart verweigert: '{Executable}' ist nicht '{Allowed}'.",
                    current, AllowedProcessName);
                return false;
            }

            if (ShellLauncher.TryStart(executablePath, out string? failureReason))
            {
                _logger.Info("Prozess gestartet: {Executable}.", current);
                return true;
            }

            _logger.Warning("Prozessstart fehlgeschlagen: {Reason}", failureReason);
            return false;
        }
    }
}

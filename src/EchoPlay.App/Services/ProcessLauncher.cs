using EchoPlay.Logger.Abstractions;
using System;
using System.Diagnostics;

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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Prozessstart ist Komfort (Neustart nach Sprachwechsel): Win32Exception, fehlende Rechte oder ein gesperrter Pfad dürfen die laufende App nicht beenden — false meldet dem Aufrufer, dass er den Nutzer um einen manuellen Neustart bitten muss.")]
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

            try
            {
                _ = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
                _logger.Info("Prozess gestartet: {Executable}.", current);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning("Prozessstart fehlgeschlagen: {Reason}", ex.Message);
                return false;
            }
        }
    }
}

using System;
using EchoPlay.App.Helpers;
using EchoPlay.Logger.Abstractions;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Startet die Setup-Datei eines Updates über die Shell. Einzige Stelle im Produktivcode,
    /// die einen Installer-Prozess erzeugt.
    /// </summary>
    public sealed class InstallerLauncher : IInstallerLauncher
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Launcher.
        /// </summary>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        public InstallerLauncher(ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(nameof(InstallerLauncher));
        }

        /// <inheritdoc/>
        public bool Start(string setupPath)
        {
            if (string.IsNullOrWhiteSpace(setupPath))
            {
                return false;
            }

            if (ShellLauncher.TryStart(setupPath, out string? failureReason))
            {
                return true;
            }

            _logger.Warning("Installer-Start fehlgeschlagen: {Reason}", failureReason);
            return false;
        }
    }
}

using System;
using System.Diagnostics;
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Installer-Start ist Best-Effort: Win32Exception (kein gültiges Image), fehlende Rechte oder eine vom Virenscanner gesperrte Datei dürfen die laufende App nicht beenden — false meldet dem Aufrufer, dass die Installation nicht angelaufen ist.")]
        public bool Start(string setupPath)
        {
            if (string.IsNullOrWhiteSpace(setupPath))
            {
                return false;
            }

            try
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = setupPath,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning("Installer-Start fehlgeschlagen: {Reason}", ex.Message);
                return false;
            }
        }
    }
}

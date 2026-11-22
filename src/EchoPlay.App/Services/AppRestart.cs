using System.Runtime.InteropServices;
using EchoPlay.Logger.Abstractions;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Startet die Anwendung neu: Nachfolger starten, diesen Prozess beenden.
    /// </summary>
    /// <remarks>
    /// Eigener Prozess statt <c>AppInstance.Restart</c>: Letzteres braucht eine Paket-Identität und
    /// wirft im Betrieb ohne Paket, bevor der Neustart überhaupt beginnt. Der Nachfolger startet,
    /// während dieser Prozess noch läuft. Hielte er den Einzelinstanz-Anspruch noch, gäbe der
    /// Nachfolger an ihn ab und endete sofort, und mit dem Beenden hier liefe gar keine Instanz mehr.
    /// Deshalb gibt dieser Prozess den Anspruch vorher frei und holt ihn zurück, wenn der Start
    /// scheitert.
    /// </remarks>
    internal static class AppRestart
    {
        /// <summary>
        /// Versucht den Neustart.
        /// </summary>
        /// <param name="launcher">Startet den Nachfolger.</param>
        /// <param name="registration">Der Einzelinstanz-Anspruch dieses Prozesses.</param>
        /// <param name="logger">Protokolliert Erfolg und Scheitern.</param>
        /// <param name="reason">Wofür neu gestartet wird, für das Protokoll.</param>
        /// <returns>
        /// <see langword="true"/>, wenn der Nachfolger läuft; <see langword="false"/>, wenn dieser Prozess
        /// offen und die laufende Instanz bleibt.
        /// </returns>
        public static bool TryRestart(IProcessLauncher launcher, IAppInstanceRegistration registration, ILogger logger, string reason)
        {
            ArgumentNullException.ThrowIfNull(launcher);
            ArgumentNullException.ThrowIfNull(registration);
            ArgumentNullException.ThrowIfNull(logger);

            string? executable = launcher.CurrentExecutablePath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                logger.Warning("Neustart für {Reason} nicht möglich – Prozesspfad unbekannt. Es greift beim nächsten manuellen Start.", reason);
                return false;
            }

            registration.Release();

            // Der Launcher verweigert den Start, wenn der Prozess nicht die App ist.
            if (!launcher.Start(executable))
            {
                registration.Reclaim();
                return false;
            }

            logger.Info("Neustart für {Reason} angestoßen.", reason);

            try
            {
                Microsoft.UI.Xaml.Application.Current.Exit();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                // Der neue Prozess läuft bereits, das Beenden des alten darf nicht hart scheitern.
                logger.Warning("Beenden nach {Reason} fehlgeschlagen: {Message}", reason, ex.Message);
            }

            return true;
        }
    }
}

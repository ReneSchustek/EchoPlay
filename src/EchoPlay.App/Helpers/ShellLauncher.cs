using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Übergibt ein Ziel an die Windows-Shell, die es mit dem zuständigen Programm öffnet: eine
    /// Adresse im Browser, ein Protokoll in seiner App, eine ausführbare Datei als Prozess.
    /// </summary>
    /// <remarks>
    /// Die Stelle, die weiß, woran ein Start scheitern kann. Ein Aufrufer prüft vorher, was
    /// gestartet werden darf (<see cref="SafeUrlLauncher"/>, Launcher für Installer und Neustart);
    /// hier geht es nur darum, dass ein fehlender Handler die Anwendung nicht beendet.
    /// </remarks>
    internal static class ShellLauncher
    {
        /// <summary>
        /// Startet <paramref name="target"/> über die Shell.
        /// </summary>
        /// <param name="target">Datei, Programm oder Adresse.</param>
        /// <param name="failureReason">Bei Misserfolg die Meldung des Systems, sonst <see langword="null"/>.</param>
        /// <returns><see langword="true"/>, wenn die Shell das Ziel angenommen hat.</returns>
        public static bool TryStart(string target, [NotNullWhen(false)] out string? failureReason)
        {
            try
            {
                // Das Process-Objekt ist nur ein Griff auf den gestarteten Prozess; ihn freizugeben
                // beendet nichts, gibt aber das Handle sofort zurück.
                using Process? started = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                failureReason = null;
                return true;
            }
            catch (Win32Exception ex)
            {
                // Kein zuständiges Programm, Datei fehlt, Zugriff verweigert oder vom Virenscanner
                // gesperrt; die Shell meldet all das auf diesem Weg.
                failureReason = ex.Message;
                return false;
            }
            catch (InvalidOperationException ex)
            {
                // Leeres Ziel.
                failureReason = ex.Message;
                return false;
            }
        }
    }
}

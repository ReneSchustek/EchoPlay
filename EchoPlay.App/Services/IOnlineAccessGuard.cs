using System;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Schützt Online-Aktionen im Offline-Modus.
    /// Zeigt einen Bestätigungsdialog und schaltet die StatusBar temporär auf "Online",
    /// solange die Aktion läuft. Im Online-Modus wird die Aktion ohne Dialog durchgelassen.
    /// </summary>

    public interface IOnlineAccessGuard
    {
        /// <summary>
        /// Prüft, ob der Offline-Modus aktiv ist. Falls ja, wird ein Bestätigungsdialog angezeigt.
        /// Bei Bestätigung wechselt die StatusBar temporär auf "Online".
        /// Das zurückgegebene <see cref="IDisposable"/> setzt den Status beim Dispose zurück.
        /// </summary>
        /// <returns>
        /// Ein <see cref="IDisposable"/>, das den temporären Online-Status beim Dispose beendet.
        /// <see langword="null"/>, wenn der Nutzer den Dialog abgelehnt hat – die Aktion soll abgebrochen werden.
        /// Im Online-Modus wird ein No-Op-Disposable zurückgegeben (kein Dialog).
        /// </returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IDisposable?> RequestOnlineAccessAsync(CancellationToken cancellationToken = default);
    }
}

using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Prüft beim Betreten einer Page, ob der aktuelle App-Modus die Anzeige
    /// erlaubt. Im Sperrfall zeigt der Guard einen lokalisierten Hinweisdialog
    /// und navigiert über den <see cref="INavigationService"/> zurück.
    /// Vermeidet duplizierten Offline-/Nur-Online-Check in den Mediathek- und Suche-ViewModels.
    /// </summary>

    public interface IPageModeGuard
    {
        /// <summary>
        /// Stellt sicher, dass die aufrufende Page Online-Funktionen nutzen darf.
        /// Blockiert, wenn der Offline-Modus in den AppSettings aktiv ist.
        /// </summary>
        /// <returns>
        /// <see langword="true"/>, wenn die Page geladen werden darf.
        /// <see langword="false"/>, wenn der Offline-Modus aktiv ist – in diesem Fall hat
        /// der Guard bereits einen Hinweisdialog gezeigt und die Rücknavigation ausgelöst.
        /// </returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<bool> EnsureOnlineAccessAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stellt sicher, dass die aufrufende Page lokale Inhalte anzeigen darf.
        /// Blockiert, wenn der Nur-Online-Modus in den AppSettings aktiv ist.
        /// </summary>
        /// <returns>
        /// <see langword="true"/>, wenn die Page geladen werden darf.
        /// <see langword="false"/>, wenn der Nur-Online-Modus aktiv ist – in diesem Fall hat
        /// der Guard bereits einen Hinweisdialog gezeigt und die Rücknavigation ausgelöst.
        /// </returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<bool> EnsureLocalAccessAsync(CancellationToken cancellationToken = default);
    }
}

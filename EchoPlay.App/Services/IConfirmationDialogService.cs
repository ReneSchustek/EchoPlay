using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Definiert den Vertrag für modale Bestätigungs-Dialoge.
    /// Ermöglicht ViewModels, eine Ja/Abbrechen-Frage zu stellen, ohne direkt von WinUI abhängig zu sein.
    /// </summary>

    public interface IConfirmationDialogService
    {
        /// <summary>
        /// Zeigt einen modalen Bestätigungs-Dialog mit Ja/Abbrechen-Schaltflächen.
        /// </summary>
        /// <param name="title">Titel des Dialogs.</param>
        /// <param name="message">Die Frage oder Erklärung für den Benutzer.</param>
        /// <returns><c>true</c>, wenn der Benutzer „Ja" gewählt hat; sonst <c>false</c>.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default);
    }
}

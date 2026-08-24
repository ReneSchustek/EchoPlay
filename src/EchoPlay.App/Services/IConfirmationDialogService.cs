using EchoPlay.Core.Models;
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
        /// Hat der Nutzer diese Rückfrage dauerhaft ausgeblendet, erscheint kein Dialog und
        /// die Antwort ist <c>true</c>.
        /// </summary>
        /// <param name="title">Titel des Dialogs.</param>
        /// <param name="message">Die Frage oder Erklärung für den Benutzer.</param>
        /// <param name="key">
        /// Kennung der Rückfrage. Pflichtangabe, damit keine Aufrufstelle das Ausblenden
        /// stillschweigend auslässt — der Übersetzer führt die Liste.
        /// </param>
        /// <returns><c>true</c>, wenn der Benutzer „Ja" gewählt hat; sonst <c>false</c>.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<bool> ConfirmAsync(string title, string message, DialogKey key, CancellationToken cancellationToken = default);
    }
}

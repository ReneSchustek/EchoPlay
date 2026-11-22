using EchoPlay.Core.Models;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Modale Ja/Abbrechen-Frage für ViewModels, ohne dass sie WinUI kennen müssen.
    /// </summary>
    public interface IConfirmationDialogService
    {
        /// <summary>
        /// Zeigt einen modalen Bestätigungs-Dialog mit Ja/Abbrechen-Schaltflächen.
        /// Hat der Nutzer diese Rückfrage dauerhaft ausgeblendet, erscheint kein Dialog und
        /// die Antwort ist <see langword="true"/>.
        /// </summary>
        /// <param name="title">Titel des Dialogs.</param>
        /// <param name="message">Die Frage oder Erklärung für den Benutzer.</param>
        /// <param name="key">
        /// Kennung der Rückfrage. Pflichtangabe, damit keine Aufrufstelle das Ausblenden
        /// stillschweigend auslässt; der Compiler erzwingt die Angabe.
        /// </param>
        /// <returns><see langword="true"/>, wenn der Benutzer „Ja" gewählt hat; sonst <see langword="false"/>.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<bool> ConfirmAsync(string title, string message, DialogKey key, CancellationToken cancellationToken = default);
    }
}

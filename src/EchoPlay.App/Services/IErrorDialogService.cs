using EchoPlay.Core.Models;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Definiert den Vertrag für die Anzeige von Fehler- und Hinweis-Dialogen.
    /// Ermöglicht ViewModels, Meldungen anzuzeigen, ohne direkt von WinUI abhängig zu sein.
    /// </summary>
    public interface IErrorDialogService
    {
        /// <summary>
        /// Zeigt einen modalen Hinweis mit Schließen-Schaltfläche und dem Häkchen
        /// „nicht wieder anzeigen". Hat der Nutzer diesen Hinweis dauerhaft ausgeblendet,
        /// erscheint nichts.
        /// </summary>
        /// <param name="title">Titel des Dialogs.</param>
        /// <param name="message">Die Meldung für den Benutzer.</param>
        /// <param name="key">
        /// Kennung des Hinweises. Pflichtangabe, damit keine Aufrufstelle das Ausblenden
        /// stillschweigend auslässt — der Übersetzer führt die Liste.
        /// </param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task ShowAsync(string title, string message, DialogKey key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Zeigt eine Meldung, die sich nicht ausblenden lässt.
        /// </summary>
        /// <remarks>
        /// Für die letzte Verteidigungslinie: unbehandelte Fehler und der fehlgeschlagene
        /// Start. Ein einmal gesetztes Häkchen würde dort jede künftige Störung verschlucken.
        /// </remarks>
        /// <param name="title">Titel des Dialogs.</param>
        /// <param name="message">Die Meldung für den Benutzer.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task ShowAlwaysAsync(string title, string message, CancellationToken cancellationToken = default);
    }
}

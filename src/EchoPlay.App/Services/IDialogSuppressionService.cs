using EchoPlay.App.Models;
using EchoPlay.Core.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Beantwortet für jeden Dialog, ob der Nutzer ihn dauerhaft ausgeblendet hat, und
    /// nimmt das Ausblenden und Zurückholen entgegen.
    /// </summary>
    /// <remarks>
    /// Die Abfrage läuft bei jeder Rückfrage und bei jeder Navigation über den
    /// <see cref="IPageModeGuard"/>; die Antwort kommt deshalb aus dem Speicher und nicht
    /// jedes Mal aus der Datenbank.
    /// </remarks>
    public interface IDialogSuppressionService
    {
        /// <summary>
        /// Gibt an, ob der Dialog dauerhaft ausgeblendet ist.
        /// </summary>
        /// <param name="key">Der zu prüfende Dialog.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns><see langword="true"/>, wenn der Dialog nicht mehr gezeigt werden soll.</returns>
        Task<bool> IsSuppressedAsync(DialogKey key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Blendet den Dialog dauerhaft aus.
        /// </summary>
        /// <param name="key">Der auszublendende Dialog.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task SuppressAsync(DialogKey key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Holt einen einzelnen Dialog zurück in die Anzeige.
        /// </summary>
        /// <param name="key">Der zurückzuholende Dialog.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task RestoreAsync(DialogKey key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Holt alle ausgeblendeten Dialoge zurück in die Anzeige.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task RestoreAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert alle derzeit ausgeblendeten Dialoge samt Zeitpunkt, jüngste zuerst.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die Liste für die Einstellungsseite.</returns>
        Task<IReadOnlyList<SuppressedDialogInfo>> GetSuppressedAsync(CancellationToken cancellationToken = default);
    }
}

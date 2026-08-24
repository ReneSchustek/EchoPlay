using EchoPlay.Data.Entities.Settings;

namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Verwaltet die dauerhaft ausgeblendeten Dialoge.
    /// </summary>
    /// <remarks>
    /// Eigene Schnittstelle statt Anbau an <see cref="IAppSettingsDataService"/>: Die Liste ist
    /// eine Menge von Zeilen, keine feste Anzahl Felder — sie wächst mit jedem neuen Dialog,
    /// ohne dass die Einstellungszeile breiter wird.
    /// </remarks>
    public interface IDialogSuppressionDataService
    {
        /// <summary>
        /// Liefert alle aktuell ausgeblendeten Dialoge, jüngste zuerst.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die aktiven Zeilen mit Schlüssel und Zeitpunkt des Ausblendens.</returns>
        Task<IReadOnlyList<DialogSuppression>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Blendet einen Dialog dauerhaft aus. Ist er bereits ausgeblendet, passiert nichts.
        /// </summary>
        /// <param name="key">Name des <c>DialogKey</c>-Werts.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task SuppressAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Holt einen einzelnen Dialog zurück in die Anzeige.
        /// </summary>
        /// <param name="key">Name des <c>DialogKey</c>-Werts.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task RestoreAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Holt alle ausgeblendeten Dialoge zurück in die Anzeige.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Anzahl der zurückgeholten Dialoge.</returns>
        Task<int> RestoreAllAsync(CancellationToken cancellationToken = default);
    }
}

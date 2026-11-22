using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Contracts;

namespace EchoPlay.Data.Entities.Settings
{
    /// <summary>
    /// Merkt, dass der Nutzer einen Dialog dauerhaft ausgeblendet hat.
    /// Eine Zeile je ausgeblendetem Dialog; <see cref="BaseEntity.CreatedAt"/> ist der
    /// Zeitpunkt des Ausblendens und wird in den Einstellungen angezeigt.
    /// </summary>
    /// <remarks>
    /// Eigene Tabelle statt weiterer Spalten in <c>AppSettings</c>: Die Anwendung kennt rund
    /// fünfzig unterdrückbare Dialoge, und jeder weitere bekäme sonst eine eigene Spalte samt
    /// Migration.
    /// <para>
    /// <see cref="Key"/> ist Text und nicht die Aufzählung <c>DialogKey</c>. Fällt ein
    /// Dialog irgendwann weg, steht seine Zeile weiter in der Datenbank. Sie soll dann
    /// übergangen werden und nicht beim Lesen die ganze Abfrage reißen.
    /// </para>
    /// </remarks>
    public sealed class DialogSuppression : BaseEntity
    {
        /// <summary>Name des <c>DialogKey</c>-Werts, fachlicher Schlüssel der Zeile.</summary>
        [DataScope(DataScope.Shared)]
        public string Key { get; set; } = string.Empty;
    }
}

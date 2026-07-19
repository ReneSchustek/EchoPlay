namespace EchoPlay.App.Models
{
    /// <summary>
    /// UI-Display-Modell für einen Eintrag in der Umbenennungs-Vorschau des Tag-Managers.
    /// Reine Anzeigerepräsentation, damit der Views-Ordner keinen Kontakt zum Modul-Typ
    /// <c>EchoPlay.TagManager.Models.RenamePreviewItem</c> braucht.
    /// </summary>
    /// <param name="OldName">Aktueller Dateiname inklusive Extension, ohne Verzeichnispfad.</param>
    /// <param name="NewName">Neuer Dateiname nach Anwendung des Musters, inklusive Extension.</param>
    public sealed record RenamePreviewDisplay(string OldName, string NewName);
}

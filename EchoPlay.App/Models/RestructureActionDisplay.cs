namespace EchoPlay.App.Models
{
    /// <summary>
    /// App-Display-Modell für eine einzelne Verschiebe-Aktion des Ordnerstruktur-Assistenten.
    /// Enthält nur die Felder, die der Vorschau-Dialog tatsächlich anzeigt –
    /// das LocalLibrary-Pendant <see cref="EchoPlay.LocalLibrary.Models.RestructureAction"/>
    /// bleibt damit aus der UI-Schicht verborgen.
    /// </summary>
    /// <param name="FileName">Dateiname (ohne Pfad) für die Vorschau-Anzeige.</param>
    /// <param name="TargetFolderName">Name des Zielordners (ohne Pfad) für die Vorschau-Anzeige.</param>
    public sealed record RestructureActionDisplay(string FileName, string TargetFolderName);
}

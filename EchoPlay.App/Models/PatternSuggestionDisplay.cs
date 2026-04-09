namespace EchoPlay.App.Models
{
    /// <summary>
    /// UI-Display-Modell für einen Mustererkennungs-Vorschlag in den Einstellungen.
    /// Eine reine Anzeige-Repräsentation, damit der Views-Ordner keinen Kontakt zum
    /// Modul-Typ <c>EchoPlay.LocalLibrary.Analysis.PatternSuggestion</c> braucht.
    /// </summary>
    /// <param name="Pattern">Das Ordner-/Dateinamens-Muster (z. B. <c>{number:000} - {title}</c>).</param>
    /// <param name="MatchCount">Anzahl der Ordner- bzw. Dateinamen, die mit dem Muster übereinstimmen.</param>
    /// <param name="MatchPercentage">Trefferquote von 0.0 bis 1.0.</param>
    /// <param name="IsFlatStructure">
    /// Gibt an, ob das Muster aus Dateinamen einer flachen Struktur stammt (MP3s direkt im Serienordner).
    /// </param>
    public sealed record PatternSuggestionDisplay(
        string Pattern,
        int MatchCount,
        double MatchPercentage,
        bool IsFlatStructure);
}

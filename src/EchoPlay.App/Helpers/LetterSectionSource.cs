namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Woher die Buchstaben-Abschnitte ihre Kacheln, deren Titel, die Auswahl und die verfügbare
    /// Breite lesen. Alles wird bei jedem Neuaufbau frisch abgefragt.
    /// </summary>
    /// <typeparam name="T">Typ der Kachel-ViewModels.</typeparam>
    /// <param name="GetItems">Liefert die aktuelle Kachelliste aus dem ViewModel.</param>
    /// <param name="TitleOf">Liefert den Titel einer Kachel; er bestimmt den Buchstaben.</param>
    /// <param name="GetSelectedIndex">Liefert den gewählten Index; -1, wenn nichts gewählt ist.</param>
    /// <param name="GetAvailableWidth">Liefert die verfügbare Breite in Pixel.</param>
    public sealed record LetterSectionSource<T>(
        Func<IReadOnlyList<T>> GetItems,
        Func<T, string?> TitleOf,
        Func<int> GetSelectedIndex,
        Func<double> GetAvailableWidth)
        where T : class;
}

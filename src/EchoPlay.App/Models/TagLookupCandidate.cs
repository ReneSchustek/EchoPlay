namespace EchoPlay.App.Models
{
    /// <summary>
    /// UI-Display-Modell für einen einzelnen Online-Lookup-Kandidaten im Tag-Manager.
    /// Eine reine Anzeigerepräsentation, damit der Views-Ordner keinen Kontakt zu
    /// Domänen- oder Modul-Typen aus <c>EchoPlay.TagManager</c> braucht.
    /// </summary>
    /// <param name="Index">Position in der ursprünglichen Ergebnisliste – wird beim Zurückübertragen ans ViewModel benötigt.</param>
    /// <param name="Title">Titel des Releases.</param>
    /// <param name="Artist">Interpret.</param>
    /// <param name="Album">Albumname.</param>
    /// <param name="Year">Erscheinungsjahr, falls bekannt.</param>
    /// <param name="TrackCount">Anzahl Tracks im Release, falls bekannt.</param>
    /// <param name="Genre">Genre, falls bekannt.</param>
    /// <param name="Source">Name der Lookup-Quelle (z. B. „MusicBrainz").</param>
    public sealed record TagLookupCandidate(
        int Index,
        string? Title,
        string? Artist,
        string? Album,
        uint? Year,
        uint? TrackCount,
        string? Genre,
        string Source);
}

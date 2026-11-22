namespace EchoPlay.App.Models
{
    /// <summary>
    /// UI-Display-Modell für einen einzelnen Online-Lookup-Kandidaten im Tag-Manager.
    /// Eine reine Anzeigerepräsentation, damit der Views-Ordner keinen Kontakt zu
    /// Domänen- oder Modul-Typen aus <c>EchoPlay.TagManager</c> braucht.
    /// </summary>
    public sealed record TagLookupCandidate
    {
        /// <summary>Position in der ursprünglichen Ergebnisliste, wird beim Zurückübertragen ans ViewModel benötigt.</summary>
        public required int Index { get; init; }

        /// <summary>Titel des Releases.</summary>
        public string? Title { get; init; }

        /// <summary>Interpret.</summary>
        public string? Artist { get; init; }

        /// <summary>Albumname.</summary>
        public string? Album { get; init; }

        /// <summary>Erscheinungsjahr, falls bekannt.</summary>
        public uint? Year { get; init; }

        /// <summary>Anzahl Tracks im Release, falls bekannt.</summary>
        public uint? TrackCount { get; init; }

        /// <summary>Genre, falls bekannt.</summary>
        public string? Genre { get; init; }

        /// <summary>Name der Lookup-Quelle (z. B. „MusicBrainz").</summary>
        public required string Source { get; init; }
    }
}

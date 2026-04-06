namespace EchoPlay.TagManager.Models
{
    /// <summary>
    /// Ergebnis eines Online-Metadaten-Lookups, z.B. von MusicBrainz.
    /// Wird im Tag-Editor als Vorlage angeboten und kann vom Nutzer übernommen werden.
    /// Alle Felder sind unveränderlich – das Ergebnis wird nur gelesen, nie verändert.
    /// </summary>
    public sealed class TagLookupResult
    {
        /// <summary>Titel des Releases laut Lookup-Quelle.</summary>
        public string? Title { get; init; }

        /// <summary>Interpret laut Lookup-Quelle.</summary>
        public string? Artist { get; init; }

        /// <summary>Albumname laut Lookup-Quelle.</summary>
        public string? Album { get; init; }

        /// <summary>Erscheinungsjahr laut Lookup-Quelle.</summary>
        public uint? Year { get; init; }

        /// <summary>Anzahl der Tracks im Release laut Lookup-Quelle.</summary>
        public uint? TrackCount { get; init; }

        /// <summary>Genre laut Lookup-Quelle, falls vorhanden.</summary>
        public string? Genre { get; init; }

        /// <summary>
        /// Name der Datenquelle, aus der das Ergebnis stammt, z.B. <c>"MusicBrainz"</c>.
        /// Wird im UI als Herkunftshinweis angezeigt.
        /// </summary>
        public string Source { get; init; } = "MusicBrainz";
    }
}

using System.Text.Json.Serialization;

namespace EchoPlay.AppleMusic.Dtos
{
    /// <summary>
    /// Repräsentiert einen Track in der iTunes Lookup API.
    /// Wird bei Track-Lookups zurückgegeben, wobei in gemischten Antworten
    /// nur Einträge mit WrapperType "track" relevant sind.
    /// </summary>
    public sealed class ITunesTrackDto
    {
        /// <summary>
        /// Typ des Eintrags. Bei Tracks "track", bei Alben "collection".
        /// Dient zur Filterung in gemischten Lookup-Antworten.
        /// </summary>
        [JsonPropertyName("wrapperType")]
        public string WrapperType { get; init; } = string.Empty;

        /// <summary>
        /// Eindeutige iTunes-ID des Tracks.
        /// </summary>
        [JsonPropertyName("trackId")]
        public long TrackId { get; init; }

        /// <summary>
        /// Titel des Tracks.
        /// </summary>
        [JsonPropertyName("trackName")]
        public string TrackName { get; init; } = string.Empty;

        /// <summary>
        /// Dauer des Tracks in Millisekunden.
        /// </summary>
        [JsonPropertyName("trackTimeMillis")]
        public int? TrackTimeMillis { get; init; }

        /// <summary>
        /// Positionsnummer des Tracks innerhalb des Albums.
        /// </summary>
        [JsonPropertyName("trackNumber")]
        public int TrackNumber { get; init; }

        /// <summary>
        /// Veröffentlichungsdatum im ISO-8601-Format.
        /// </summary>
        [JsonPropertyName("releaseDate")]
        public string? ReleaseDate { get; init; }

        /// <summary>
        /// iTunes-ID des zugehörigen Albums.
        /// </summary>
        [JsonPropertyName("collectionId")]
        public long CollectionId { get; init; }

        /// <summary>
        /// Anzeigename des zugehörigen Albums.
        /// </summary>
        [JsonPropertyName("collectionName")]
        public string CollectionName { get; init; } = string.Empty;
    }
}

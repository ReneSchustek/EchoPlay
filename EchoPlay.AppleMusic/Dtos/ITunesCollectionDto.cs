using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace EchoPlay.AppleMusic.Dtos
{
    /// <summary>
    /// Repräsentiert ein Album (Collection) in der iTunes Search/Lookup API.
    /// Wird bei Album-Lookups zurückgegeben, wobei in gemischten Antworten
    /// nur Einträge mit WrapperType "collection" relevant sind.
    /// </summary>
    public sealed class ITunesCollectionDto
    {
        /// <summary>
        /// Typ des Eintrags. Bei Alben "collection", bei Künstlern "artist".
        /// Dient zur Filterung in gemischten Lookup-Antworten.
        /// </summary>
        [JsonPropertyName("wrapperType")]
        public string WrapperType { get; init; } = string.Empty;

        /// <summary>
        /// Eindeutige iTunes-ID des Albums.
        /// </summary>
        [JsonPropertyName("collectionId")]
        public long CollectionId { get; init; }

        /// <summary>
        /// Anzeigename des Albums.
        /// </summary>
        [JsonPropertyName("collectionName")]
        public string CollectionName { get; init; } = string.Empty;

        /// <summary>
        /// iTunes-ID des zugehörigen Künstlers.
        /// </summary>
        [JsonPropertyName("artistId")]
        public long ArtistId { get; init; }

        /// <summary>
        /// Anzeigename des zugehörigen Künstlers.
        /// </summary>
        [JsonPropertyName("artistName")]
        public string ArtistName { get; init; } = string.Empty;

        /// <summary>
        /// Anzahl der Tracks im Album.
        /// </summary>
        [JsonPropertyName("trackCount")]
        public int TrackCount { get; init; }

        /// <summary>
        /// URL zum Album-Artwork in 100x100 Pixel Auflösung.
        /// </summary>
        [JsonPropertyName("artworkUrl100")]
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "DTO spiegelt externes iTunes-JSON-Feld 'artworkUrl100'; Uri-Umwandlung würde die Serialisierung doppelt durchlaufen.")]
        public string? ArtworkUrl100 { get; init; }

        /// <summary>
        /// Veröffentlichungsdatum im ISO-8601-Format.
        /// </summary>
        [JsonPropertyName("releaseDate")]
        public string? ReleaseDate { get; init; }

        /// <summary>
        /// URL zur Album-Seite in Apple Music.
        /// Kann verwendet werden, um das Album direkt in der Apple-Music-App zu öffnen.
        /// </summary>
        [JsonPropertyName("collectionViewUrl")]
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "DTO spiegelt externes iTunes-JSON-Feld 'collectionViewUrl'; Uri-Umwandlung würde die Serialisierung doppelt durchlaufen.")]
        public string? CollectionViewUrl { get; init; }

        /// <summary>
        /// Primäres Genre des Albums (z.B. "Hörspiele").
        /// </summary>
        [JsonPropertyName("primaryGenreName")]
        public string? PrimaryGenreName { get; init; }
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace EchoPlay.AppleMusic.Dtos
{
    /// <summary>
    /// Repräsentiert einen Künstler in der iTunes Search API.
    /// Wird bei der Suche mit entity=musicArtist zurückgegeben.
    /// </summary>
    public sealed class ITunesArtistDto
    {
        /// <summary>
        /// Typ des Eintrags. Bei Künstlern immer "artist".
        /// Dient zur Unterscheidung in gemischten Lookup-Antworten.
        /// </summary>
        [JsonPropertyName("wrapperType")]
        public string WrapperType { get; init; } = string.Empty;

        /// <summary>
        /// Eindeutige iTunes-ID des Künstlers.
        /// </summary>
        [JsonPropertyName("artistId")]
        public long ArtistId { get; init; }

        /// <summary>
        /// Anzeigename des Künstlers.
        /// </summary>
        [JsonPropertyName("artistName")]
        public string ArtistName { get; init; } = string.Empty;

        /// <summary>
        /// URL zur Apple-Music-Profilseite des Künstlers.
        /// </summary>
        [JsonPropertyName("artistLinkUrl")]
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "DTO spiegelt externes iTunes-JSON-Feld 'artistLinkUrl'; Uri-Umwandlung würde die Serialisierung doppelt durchlaufen.")]
        public string? ArtistLinkUrl { get; init; }

        /// <summary>
        /// Primäres Genre des Künstlers (z.B. "Hörspiele", "Kinder und Jugend").
        /// Steht im Gegensatz zur Apple Music Developer API bei iTunes Search zur Verfügung
        /// und ist ein wertvoller Indikator für die Hörspiel-Erkennung.
        /// </summary>
        [JsonPropertyName("primaryGenreName")]
        public string? PrimaryGenreName { get; init; }
    }
}

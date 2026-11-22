using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Spotify.Dtos
{
    /// <summary>
    /// Repräsentiert einen Künstler aus der Spotify-Web-API.
    /// Die Klasse bildet ausschließlich die Felder ab, die für die Hörspiel-Erkennung und spätere Serien-Zuordnung relevant sind.
    /// </summary>
    public sealed class SpotifyArtistDto
    {
        /// <summary>
        /// Eindeutige Spotify-ID des Künstlers.
        /// </summary>
        public required string SpotifyArtistId { get; init; }

        /// <summary>
        /// Anzeigename des Künstlers.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Von Spotify zugeordnete Genres. Diese Information wird unter anderem
        /// zur Ausschlusslogik verwendet.
        /// </summary>
        public IReadOnlyList<string> Genres { get; init; } = [];

        /// <summary>
        /// URL zum primären Künstlerbild, sofern von Spotify geliefert.
        /// </summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "Feld der Anbieter-Antwort. Als Uri ließe ein einziges unlesbares Feld die Deserialisierung der ganzen Antwort scheitern; geprüft wird die Adresse beim Übergang ins Import-Modell (WebAddress).")]
        public string? ImageUrl { get; init; }
    }
}

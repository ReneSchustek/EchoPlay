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
        public string? ImageUrl { get; init; }
    }
}
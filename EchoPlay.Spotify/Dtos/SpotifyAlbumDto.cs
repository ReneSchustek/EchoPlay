namespace EchoPlay.Spotify.Dtos
{
    /// <summary>
    /// Repräsentiert ein Album eines Künstlers bei Spotify.
    /// Für EchoPlay sind Alben primär Container für Episoden (Tracks).
    /// </summary>
    public sealed class SpotifyAlbumDto
    {
        /// <summary>
        /// Eindeutige Spotify-ID des Albums.
        /// </summary>
        public required string SpotifyAlbumId { get; init; }

        /// <summary>
        /// Anzeigename des Albums.
        /// </summary>
        public required string Title { get; init; }

        /// <summary>
        /// Veröffentlichungsdatum des Albums, sofern von Spotify bereitgestellt.
        /// </summary>
        public DateTime? ReleaseDate { get; init; }

        /// <summary>
        /// Gesamtanzahl der Tracks im Album.
        /// Wird unter anderem zur Plausibilisierung von Hörspiel-Alben verwendet.
        /// </summary>
        public int TotalTracks { get; init; }

        /// <summary>
        /// URL zum Cover-Bild des Albums. Null wenn kein Cover vorhanden.
        /// </summary>
        public string? ImageUrl { get; init; }

        /// <summary>
        /// Name des Künstlers. Wird bei der Album-Suche befüllt,
        /// damit das Suchergebnis den Seriennamen anzeigen kann.
        /// </summary>
        public string? ArtistName { get; init; }
    }
}
namespace EchoPlay.Spotify.Dtos
{
    /// <summary>
    /// Repräsentiert einen einzelnen Track eines Spotify-Albums.
    /// Tracks bilden in EchoPlay die Grundlage für Episoden-Erkennung und -Sortierung.
    /// </summary>
    public sealed class SpotifyTrackDto
    {
        /// <summary>
        /// Eindeutige Spotify-ID des Tracks.
        /// </summary>
        public required string SpotifyTrackId { get; init; }

        /// <summary>
        /// Titel des Tracks.
        /// </summary>
        public required string Title { get; init; }

        /// <summary>
        /// Dauer des Tracks.
        /// </summary>
        public TimeSpan Duration { get; init; }

        /// <summary>
        /// Position des Tracks innerhalb des Albums, wie von Spotify geliefert.
        /// </summary>
        public int TrackNumber { get; init; }
    }
}

using EchoPlay.Spotify.Dtos;

namespace EchoPlay.Spotify.Abstractions
{
    /// <summary>
    /// Abstraktion für den technischen Zugriff auf die Spotify-Web-API.
    /// Das Interface definiert ausschließlich rohe Leseoperationen und stellt sicher, dass fachliche Logik und Tests nicht vom
    /// konkreten HTTP-Zugriff abhängig sind.
    /// </summary>
    public interface ISpotifyApiClient
    {
        /// <summary>
        /// Sucht Künstler anhand eines Suchbegriffs.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="limit">Maximale Anzahl der Ergebnisse.</param>
        /// <returns>Eine Liste roher Spotify-Künstlerdaten.</returns>
        Task<IReadOnlyList<SpotifyArtistDto>> SearchArtistsAsync(string query, int limit);

        /// <summary>
        /// Sucht Alben (Folgen) anhand eines Suchbegriffs.
        /// Ermöglicht die Suche nach einzelnen Hörspielepisoden statt nur nach Serien.
        /// </summary>
        /// <param name="query">Der Suchtext (z.B. "Kapatenhund").</param>
        /// <param name="limit">Maximale Anzahl der Ergebnisse.</param>
        /// <returns>Eine Liste roher Spotify-Albumdaten.</returns>
        Task<IReadOnlyList<SpotifyAlbumDto>> SearchAlbumsAsync(string query, int limit);

        /// <summary>
        /// Lädt alle Alben eines Spotify-Künstlers.
        /// </summary>
        /// <param name="artistId">Die Spotify-Artist-ID.</param>
        /// <param name="limit">Maximale Anzahl der Alben.</param>
        /// <returns>Eine Liste roher Spotify-Albumdaten.</returns>
        Task<IReadOnlyList<SpotifyAlbumDto>> GetArtistAlbumsAsync(string artistId, int limit);

        /// <summary>
        /// Lädt alle Tracks eines Spotify-Albums.
        /// </summary>
        /// <param name="albumId">Die Spotify-Album-ID.</param>
        /// <returns>Eine Liste roher Spotify-Trackdaten.</returns>
        Task<IReadOnlyList<SpotifyTrackDto>> GetAlbumTracksAsync(string albumId);
    }
}
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;

namespace EchoPlay.Spotify.Tests.Fakes
{
    /// <summary>
    /// Konfigurierbarer Fake des Spotify-API-Clients für Scoring-Tests.
    /// Erlaubt die gezielte Zuordnung von Alben zu Künstlern und Tracks zu Alben,
    /// um alle Stufen des Scorings deterministisch testen zu können.
    /// </summary>
    internal sealed class ConfigurableSpotifyApiClient : ISpotifyApiClient
    {
        private readonly Dictionary<string, IReadOnlyList<SpotifyAlbumDto>> _albumsByArtist = new();
        private readonly Dictionary<string, IReadOnlyList<SpotifyTrackDto>> _tracksByAlbum = new();

        /// <summary>
        /// Registriert Alben für einen bestimmten Künstler.
        /// </summary>
        /// <param name="artistId">Die Spotify-Artist-ID.</param>
        /// <param name="albums">Die zugeordneten Alben.</param>
        /// <returns>Diese Instanz für Fluent-Konfiguration.</returns>
        public ConfigurableSpotifyApiClient WithAlbums(string artistId, IReadOnlyList<SpotifyAlbumDto> albums)
        {
            _albumsByArtist[artistId] = albums;
            return this;
        }

        /// <summary>
        /// Registriert Tracks für ein bestimmtes Album.
        /// </summary>
        /// <param name="albumId">Die Spotify-Album-ID.</param>
        /// <param name="tracks">Die zugeordneten Tracks.</param>
        /// <returns>Diese Instanz für Fluent-Konfiguration.</returns>
        public ConfigurableSpotifyApiClient WithTracks(string albumId, IReadOnlyList<SpotifyTrackDto> tracks)
        {
            _tracksByAlbum[albumId] = tracks;
            return this;
        }

        /// <summary>
        /// Wird von Scoring-Tests nicht benötigt, liefert leere Liste.
        /// </summary>
        public Task<IReadOnlyList<SpotifyArtistDto>> SearchArtistsAsync(string query, int limit)
        {
            return Task.FromResult<IReadOnlyList<SpotifyArtistDto>>([]);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<SpotifyAlbumDto>> SearchAlbumsAsync(string query, int limit)
        {
            return Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>([]);
        }

        /// <summary>
        /// Liefert die konfigurierten Alben für den angegebenen Künstler.
        /// </summary>
        public Task<IReadOnlyList<SpotifyAlbumDto>> GetArtistAlbumsAsync(string artistId, int limit)
        {
            if (_albumsByArtist.TryGetValue(artistId, out IReadOnlyList<SpotifyAlbumDto>? albums))
            {
                IReadOnlyList<SpotifyAlbumDto> limited = [.. albums.Take(limit)];
                return Task.FromResult(limited);
            }

            return Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>([]);
        }

        /// <summary>
        /// Liefert die konfigurierten Tracks für das angegebene Album.
        /// </summary>
        public Task<IReadOnlyList<SpotifyTrackDto>> GetAlbumTracksAsync(string albumId)
        {
            if (_tracksByAlbum.TryGetValue(albumId, out IReadOnlyList<SpotifyTrackDto>? tracks))
            {
                return Task.FromResult(tracks);
            }

            return Task.FromResult<IReadOnlyList<SpotifyTrackDto>>([]);
        }
    }
}

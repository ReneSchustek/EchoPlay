using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;

namespace EchoPlay.Spotify.Tests.Fakes
{
    /// <summary>
    /// Szenario-basierter Fake für den Spotify-API-Client.
    /// Dieser Fake bildet gezielt eine typische Hörspielstruktur ab,
    /// um den Episodenimport fachlich vollständig testen zu können.
    /// </summary>
    internal sealed class ScenarioSpotifyApiClient : ISpotifyApiClient
    {
        /// <summary>
        /// Liefert einen festen Hörspiel-Künstler.
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
        /// Liefert zwei Alben in bewusst unsortierter Reihenfolge,
        /// um die fachliche Sortierung nach ReleaseDate zu erzwingen.
        /// </summary>
        public Task<IReadOnlyList<SpotifyAlbumDto>> GetArtistAlbumsAsync(string artistId, int limit)
        {
            IReadOnlyList<SpotifyAlbumDto> albums =
            [
                new SpotifyAlbumDto
                {
                    SpotifyAlbumId = "album-002",
                    Title = "Späteres Album",
                    ReleaseDate = new DateTime(1985, 1, 1),
                    TotalTracks = 1
                },
                new SpotifyAlbumDto
                {
                    SpotifyAlbumId = "album-001",
                    Title = "Früheres Album",
                    ReleaseDate = new DateTime(1979, 1, 1),
                    TotalTracks = 1
                }
            ];

            return Task.FromResult(albums);
        }

        /// <summary>
        /// Liefert genau einen Track pro Album.
        /// Die TrackNumber ist bewusst identisch,
        /// damit nur die Albumreihenfolge relevant ist.
        /// </summary>
        public Task<IReadOnlyList<SpotifyTrackDto>> GetAlbumTracksAsync(string albumId)
        {
            IReadOnlyList<SpotifyTrackDto> tracks =
            [
                new SpotifyTrackDto
                {
                    SpotifyTrackId = $"track-{albumId}",
                    Title = $"Episode aus {albumId}",
                    Duration = TimeSpan.FromMinutes(45),
                    TrackNumber = 1
                }
            ];

            return Task.FromResult(tracks);
        }
    }
}
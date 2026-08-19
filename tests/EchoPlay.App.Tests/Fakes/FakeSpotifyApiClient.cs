using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ISpotifyApiClient"/> ohne Netzzugriff.
    /// Liefert vorgegebene Alben und hält die Suchbegriffe fest.
    /// </summary>
    internal sealed class FakeSpotifyApiClient : ISpotifyApiClient
    {
        /// <summary>Treffer, die <see cref="SearchAlbumsAsync"/> liefert.</summary>
        public List<SpotifyAlbumDto> Albums { get; } = [];

        /// <summary>Alle Suchbegriffe der Album-Suche in ihrer Reihenfolge.</summary>
        public List<(string Query, int Limit)> AlbumSearches { get; } = [];

        /// <inheritdoc/>
        public Task<IReadOnlyList<SpotifyArtistDto>> SearchArtistsAsync(
            string query, int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SpotifyArtistDto>>([]);

        /// <inheritdoc/>
        public Task<IReadOnlyList<SpotifyAlbumDto>> SearchAlbumsAsync(
            string query, int limit, CancellationToken cancellationToken = default)
        {
            AlbumSearches.Add((query, limit));
            return Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>(Albums);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<SpotifyAlbumDto>> GetArtistAlbumsAsync(
            string artistId, int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>([]);

        /// <inheritdoc/>
        public Task<IReadOnlyList<SpotifyTrackDto>> GetAlbumTracksAsync(
            string albumId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SpotifyTrackDto>>([]);
    }
}

using EchoPlay.App.Services;
using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests fuer <see cref="ConnectionTestCoordinator"/>. Verwendet inline definierte
    /// Stub-API-Clients fuer Spotify und Apple Music, damit kein Netzwerk noetig ist.
    /// </summary>
    public sealed class ConnectionTestCoordinatorTests
    {
        [Fact]
        public async Task TestAsync_None_ReturnsFailureWithoutCallingProvider()
        {
            ServiceCollection services = new();
            ServiceProvider provider = services.BuildServiceProvider();
            ConnectionTestCoordinator coordinator = new(provider.GetRequiredService<IServiceScopeFactory>());

            ConnectionTestResult result = await coordinator.TestAsync(ProviderType.None, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("No provider configured", result.ErrorDetail);
        }

        [Fact]
        public async Task TestAsync_Spotify_NetworkError_ReturnsFailureWithMessage()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISpotifyApiClient>(_ => new ThrowingSpotifyClient(new HttpRequestException("offline")));
            ServiceProvider provider = services.BuildServiceProvider();
            ConnectionTestCoordinator coordinator = new(provider.GetRequiredService<IServiceScopeFactory>());

            ConnectionTestResult result = await coordinator.TestAsync(ProviderType.Spotify, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("offline", result.ErrorDetail);
        }

        [Fact]
        public async Task TestAsync_Spotify_HappyPath_ReturnsSuccess()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISpotifyApiClient>(_ => new EmptyResultSpotifyClient());
            ServiceProvider provider = services.BuildServiceProvider();
            ConnectionTestCoordinator coordinator = new(provider.GetRequiredService<IServiceScopeFactory>());

            ConnectionTestResult result = await coordinator.TestAsync(ProviderType.Spotify, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Null(result.ErrorDetail);
        }

        // ── Test-Stubs ─────────────────────────────────────────────────────────

        private sealed class ThrowingSpotifyClient : ISpotifyApiClient
        {
            private readonly Exception _ex;
            public ThrowingSpotifyClient(Exception ex) => _ex = ex;
            public Task<IReadOnlyList<SpotifyArtistDto>> SearchArtistsAsync(string query, int limit, CancellationToken cancellationToken = default) => throw _ex;
            public Task<IReadOnlyList<SpotifyAlbumDto>> SearchAlbumsAsync(string query, int limit, CancellationToken cancellationToken = default) => throw _ex;
            public Task<IReadOnlyList<SpotifyAlbumDto>> GetArtistAlbumsAsync(string artistId, int limit, CancellationToken cancellationToken = default) => throw _ex;
            public Task<IReadOnlyList<SpotifyTrackDto>> GetAlbumTracksAsync(string albumId, CancellationToken cancellationToken = default) => throw _ex;
        }

        private sealed class EmptyResultSpotifyClient : ISpotifyApiClient
        {
            public Task<IReadOnlyList<SpotifyArtistDto>> SearchArtistsAsync(string query, int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyArtistDto>>([]);
            public Task<IReadOnlyList<SpotifyAlbumDto>> SearchAlbumsAsync(string query, int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>([]);
            public Task<IReadOnlyList<SpotifyAlbumDto>> GetArtistAlbumsAsync(string artistId, int limit, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyAlbumDto>>([]);
            public Task<IReadOnlyList<SpotifyTrackDto>> GetAlbumTracksAsync(string albumId, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SpotifyTrackDto>>([]);
        }
    }
}

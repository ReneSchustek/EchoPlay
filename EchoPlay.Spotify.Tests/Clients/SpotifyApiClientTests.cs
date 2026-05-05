using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using EchoPlay.Spotify.Clients;
using EchoPlay.Spotify.Dtos;

namespace EchoPlay.Spotify.Tests.Clients
{
    /// <summary>
    /// Verifiziert den Spotify-Web-API-Client (technische Schicht ohne Fachlogik).
    /// </summary>
    public sealed class SpotifyApiClientTests
    {
        [Fact]
        public async Task SearchArtistsAsync_HappyPath_ReturnsArtists()
        {
            const string responseJson = """
            {
                "artists": {
                    "items": [
                        { "id": "abc", "name": "Die drei ???", "popularity": 80, "genres": ["audio drama"], "images": [] }
                    ]
                }
            }
            """;
            SpotifyApiClient client = BuildClient(responseJson);

            IReadOnlyList<SpotifyArtistDto> result = await client.SearchArtistsAsync("Die drei", limit: 10);

            _ = Assert.Single(result);
            Assert.Equal("abc", result[0].SpotifyArtistId);
        }

        [Fact]
        public async Task SearchArtistsAsync_EmptyResponse_ReturnsEmptyList()
        {
            const string responseJson = """{"artists":{"items":[]}}""";
            SpotifyApiClient client = BuildClient(responseJson);

            IReadOnlyList<SpotifyArtistDto> result = await client.SearchArtistsAsync("nichts", limit: 10);

            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchAlbumsAsync_HttpError_ThrowsHttpRequestException()
        {
            SpotifyApiClient client = BuildClient(string.Empty, HttpStatusCode.InternalServerError);

            _ = await Assert.ThrowsAsync<HttpRequestException>(
                async () => await client.SearchAlbumsAsync("test", limit: 10));
        }

        [Fact]
        public async Task GetArtistAlbumsAsync_EmptyResponse_ReturnsEmptyList()
        {
            // Volle JSON-Felder fuer Album-Mapping waeren fragil — wir testen den Empty-Pfad
            // gegen die echte Spotify-Response-Form (items + next).
            const string responseJson = """{"items":[],"next":null}""";
            SpotifyApiClient client = BuildClient(responseJson);

            IReadOnlyList<SpotifyAlbumDto> result = await client.GetArtistAlbumsAsync("artistX", limit: 10);

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetAlbumTracksAsync_EmptyResponse_ReturnsEmptyList()
        {
            const string responseJson = """{"items":[],"next":null}""";
            SpotifyApiClient client = BuildClient(responseJson);

            IReadOnlyList<SpotifyTrackDto> result = await client.GetAlbumTracksAsync("alb1");

            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchArtistsAsync_CanceledToken_ThrowsTaskCanceled()
        {
            SpotifyApiClient client = BuildClient("""{"artists":{"items":[]}}""");
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            _ = await Assert.ThrowsAsync<TaskCanceledException>(
                async () => await client.SearchArtistsAsync("x", limit: 10, cts.Token));
        }

        // ── Test-Helfer ──────────────────────────────────────────────────────────

        private static SpotifyApiClient BuildClient(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            StubHandler handler = new(statusCode, responseJson);
            HttpClient http = new(handler) { BaseAddress = new Uri("https://api.spotify.com/v1/") };
            return new SpotifyApiClient(http, NullLoggerFactory.Instance);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _responseJson;

            public StubHandler(HttpStatusCode statusCode, string responseJson)
            {
                _statusCode = statusCode;
                _responseJson = responseJson;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HttpResponseMessage response = new(_statusCode);
                if (!string.IsNullOrEmpty(_responseJson))
                {
                    response.Content = new StringContent(_responseJson, Encoding.UTF8, "application/json");
                }
                return Task.FromResult(response);
            }
        }

        private static class NullLoggerFactory
        {
            public static readonly LoggerFactory Instance = new([], new LoggerOptions());
        }
    }
}

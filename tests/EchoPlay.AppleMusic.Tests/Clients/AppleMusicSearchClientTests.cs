using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.AppleMusic.Clients;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;

namespace EchoPlay.AppleMusic.Tests.Clients
{
    /// <summary>
    /// Verifiziert den iTunes-Search-API-Adapter.
    /// </summary>
    public sealed class AppleMusicSearchClientTests
    {
        [Fact]
        public async Task LookupAlbumsAsync_HappyPath_ReturnsParsedAlbums()
        {
            const string responseJson = """
            {
                "resultCount": 2,
                "results": [
                    { "wrapperType": "artist", "artistId": 100 },
                    { "wrapperType": "collection", "collectionId": 1, "collectionName": "Folge 1", "artistId": 100 },
                    { "wrapperType": "collection", "collectionId": 2, "collectionName": "Folge 2", "artistId": 100 }
                ]
            }
            """;
            AppleMusicSearchClient client = BuildClient(responseJson);

            ITunesResponseDto<ITunesCollectionDto> result = await client.LookupAlbumsAsync(artistId: 100, ct: TestContext.Current.CancellationToken);

            Assert.NotNull(result.Results);
            // ResultCount entspricht dem JSON-Header "resultCount", nicht der tatsaechlichen Listenlaenge.
            Assert.Equal(2, result.ResultCount);
        }

        [Fact]
        public async Task LookupAlbumsAsync_EmptyResponse_ReturnsEmptyList()
        {
            const string responseJson = """{"resultCount":0,"results":[]}""";
            AppleMusicSearchClient client = BuildClient(responseJson);

            ITunesResponseDto<ITunesCollectionDto> result = await client.LookupAlbumsAsync(artistId: 999, ct: TestContext.Current.CancellationToken);

            Assert.Equal(0, result.ResultCount);
            Assert.Empty(result.Results);
        }

        [Fact]
        public async Task LookupAlbumsAsync_HttpError_ThrowsHttpRequestException()
        {
            AppleMusicSearchClient client = BuildClient(string.Empty, HttpStatusCode.InternalServerError);

            _ = await Assert.ThrowsAsync<HttpRequestException>(
                async () => await client.LookupAlbumsAsync(artistId: 100, ct: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task LookupAlbumsAsync_CanceledToken_ThrowsOperationCanceled()
        {
            AppleMusicSearchClient client = BuildClient("""{"resultCount":0,"results":[]}""");
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            _ = await Assert.ThrowsAsync<TaskCanceledException>(
                async () => await client.LookupAlbumsAsync(artistId: 100, ct: cts.Token));
        }

        [Fact]
        public async Task SearchArtistsAsync_HappyPath_ReturnsArtists()
        {
            const string responseJson = """
            {
                "resultCount": 1,
                "results": [
                    { "wrapperType": "artist", "artistId": 42, "artistName": "Die drei ???" }
                ]
            }
            """;
            AppleMusicSearchClient client = BuildClient(responseJson);

            ITunesResponseDto<ITunesArtistDto> result = await client.SearchArtistsAsync("Die drei", ct: TestContext.Current.CancellationToken);

            Assert.Equal(1, result.ResultCount);
        }

        [Fact]
        public async Task SearchAlbumsAsync_LimitParameterTransmitted_ReturnsCollections()
        {
            const string responseJson = """{"resultCount":0,"results":[]}""";
            RecordingHandler handler = new(HttpStatusCode.OK, responseJson);
            using HttpClient http = new(handler) { BaseAddress = new Uri("https://itunes.apple.com/") };
            AppleMusicSearchClient client = new(http, NullLoggerFactory.Instance);

            _ = await client.SearchAlbumsAsync("test", limit: 10, ct: TestContext.Current.CancellationToken);

            _ = Assert.Single(handler.RequestUris);
            Assert.Contains("limit=10", handler.RequestUris[0].Query, StringComparison.Ordinal);
        }

        // ── Test-Helfer ──────────────────────────────────────────────────────────

        private static AppleMusicSearchClient BuildClient(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            RecordingHandler handler = new(statusCode, responseJson);
            HttpClient http = new(handler) { BaseAddress = new Uri("https://itunes.apple.com/") };
            return new AppleMusicSearchClient(http, NullLoggerFactory.Instance);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _responseJson;
            public System.Collections.Generic.List<Uri> RequestUris { get; } = [];

            public RecordingHandler(HttpStatusCode statusCode, string responseJson)
            {
                _statusCode = statusCode;
                _responseJson = responseJson;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (request.RequestUri is not null)
                {
                    RequestUris.Add(request.RequestUri);
                }

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

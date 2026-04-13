using EchoPlay.Spotify.Auth;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace EchoPlay.Spotify.Tests.Auth
{
    /// <summary>
    /// Tests für das 401-reaktive Refresh-Verhalten von <see cref="SpotifyAuthMessageHandler"/>:
    /// einmaliger Retry mit frischem Token bei 401, kein Retry bei 2xx, kein Infinite-Loop.
    /// </summary>
    public sealed class SpotifyAuthMessageHandlerTests
    {
        [Fact]
        public async Task SendAsync_AttachesBearerToken_OnSuccessfulResponse()
        {
            RecordingInnerHandler api = new();
            api.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK));
            (HttpClient client, TokenServer tokens) = BuildClient(api);
            tokens.EnqueueToken("token-A");

            using HttpResponseMessage response = await client.GetAsync(new Uri("v1/me", UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            _ = Assert.Single(api.AuthorizationHeaders);
            Assert.Equal("Bearer token-A", api.AuthorizationHeaders[0]);
            Assert.Equal(1, tokens.CallCount);
        }

        [Fact]
        public async Task SendAsync_Refreshes_And_RetriesOnce_On401()
        {
            RecordingInnerHandler api = new();
            api.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            api.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK));
            (HttpClient client, TokenServer tokens) = BuildClient(api);
            tokens.EnqueueToken("token-stale");
            tokens.EnqueueToken("token-fresh");

            using HttpResponseMessage response = await client.GetAsync(new Uri("v1/me", UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, api.CallCount);
            Assert.Equal("Bearer token-stale", api.AuthorizationHeaders[0]);
            Assert.Equal("Bearer token-fresh", api.AuthorizationHeaders[1]);
            Assert.Equal(2, tokens.CallCount);
        }

        [Fact]
        public async Task SendAsync_Propagates401_WhenRetryAlsoFails()
        {
            RecordingInnerHandler api = new();
            api.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            api.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            (HttpClient client, TokenServer tokens) = BuildClient(api);
            tokens.EnqueueToken("token-1");
            tokens.EnqueueToken("token-2");

            using HttpResponseMessage response = await client.GetAsync(new Uri("v1/me", UriKind.Relative));

            // Genau zwei Versuche — kein dritter Refresh, kein Infinite-Loop.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(2, api.CallCount);
        }

        private static (HttpClient client, TokenServer tokens) BuildClient(RecordingInnerHandler api)
        {
            TokenServer tokenServer = new();
            HttpClient tokenHttpClient = new(tokenServer) { BaseAddress = new Uri("https://accounts.spotify.test/") };

            SpotifyTokenClient tokenClient = new(
                new SingleHttpClientFactory(tokenHttpClient),
                new StaticCredentialsProvider("id", "secret"),
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));

            SpotifyAuthMessageHandler authHandler = new(tokenClient) { InnerHandler = api };
            HttpClient apiClient = new(authHandler) { BaseAddress = new Uri("https://api.spotify.test/") };
            return (apiClient, tokenServer);
        }

        private sealed class SingleHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpClient _httpClient;
            public SingleHttpClientFactory(HttpClient httpClient) => _httpClient = httpClient;
            public HttpClient CreateClient(string name) => _httpClient;
        }

        private sealed class StaticCredentialsProvider : ISpotifyClientCredentialsProvider
        {
            private readonly SpotifyClientCredentials _credentials;
            public StaticCredentialsProvider(string id, string secret)
                => _credentials = new SpotifyClientCredentials(id, secret);
            public Task<SpotifyClientCredentials?> GetAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<SpotifyClientCredentials?>(_credentials);
        }

        /// <summary>Zählt API-Aufrufe und speichert den Authorization-Header pro Aufruf.</summary>
        private sealed class RecordingInnerHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses = new();
            private readonly List<string> _authHeaders = new();

            public int CallCount { get; private set; }

            public List<string> AuthorizationHeaders => _authHeaders;

            public void EnqueueResponse(HttpResponseMessage response) => _responses.Enqueue(response);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                AuthenticationHeaderValue? auth = request.Headers.Authorization;
                _authHeaders.Add(auth is null ? string.Empty : $"{auth.Scheme} {auth.Parameter}");

                return Task.FromResult(_responses.TryDequeue(out HttpResponseMessage? response)
                    ? response
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }
        }

        /// <summary>Liefert Token-Responses aus einer Queue.</summary>
        private sealed class TokenServer : HttpMessageHandler
        {
            private readonly Queue<string> _tokens = new();

            public int CallCount { get; private set; }

            public void EnqueueToken(string token) => _tokens.Enqueue(token);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                if (!_tokens.TryDequeue(out string? token))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                }

                string json = $$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":3600}""";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}

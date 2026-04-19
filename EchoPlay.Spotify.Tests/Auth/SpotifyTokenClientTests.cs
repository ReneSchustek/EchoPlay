using EchoPlay.Spotify.Auth;
using EchoPlay.Spotify.Tests.Fakes;
using EchoPlay.Spotify.Tests.Http;
using System.Net;
using System.Text;

namespace EchoPlay.Spotify.Tests.Auth
{
    /// <summary>
    /// Tests für das Singleton-Lebenszyklus-Verhalten von <see cref="SpotifyTokenClient"/>:
    /// Cache-Hit/Miss, Parallel-Access (genau ein HTTP-Roundtrip), Invalidate, fehlende Credentials.
    /// </summary>
    public sealed class SpotifyTokenClientTests
    {
        [Fact]
        public async Task GetAccessTokenAsync_ReturnsCachedToken_OnSecondCall()
        {
            RecordingHandler handler = new();
            handler.EnqueueToken("token-1", expiresInSeconds: 3600);
            SpotifyTokenClient tokenClient = CreateTokenClient(handler);

            string first = await tokenClient.GetAccessTokenAsync();
            string second = await tokenClient.GetAccessTokenAsync();

            Assert.Equal("token-1", first);
            Assert.Equal("token-1", second);
            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task GetAccessTokenAsync_FetchesNewToken_AfterInvalidate()
        {
            RecordingHandler handler = new();
            handler.EnqueueToken("token-1", expiresInSeconds: 3600);
            handler.EnqueueToken("token-2", expiresInSeconds: 3600);
            SpotifyTokenClient tokenClient = CreateTokenClient(handler);

            string first = await tokenClient.GetAccessTokenAsync();
            await tokenClient.InvalidateAsync();
            string second = await tokenClient.GetAccessTokenAsync();

            Assert.Equal("token-1", first);
            Assert.Equal("token-2", second);
            Assert.Equal(2, handler.CallCount);
        }

        [Fact]
        public async Task GetAccessTokenAsync_SerializesParallelRequests_ToSingleRoundtrip()
        {
            RecordingHandler handler = new() { ResponseDelay = TimeSpan.FromMilliseconds(50) };
            handler.EnqueueToken("token-shared", expiresInSeconds: 3600);
            SpotifyTokenClient tokenClient = CreateTokenClient(handler);

            Task<string>[] tasks = Enumerable.Range(0, 10)
                .Select(_ => tokenClient.GetAccessTokenAsync())
                .ToArray();
            string[] tokens = await Task.WhenAll(tasks);

            Assert.All(tokens, t => Assert.Equal("token-shared", t));
            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task GetAccessTokenAsync_Throws_WhenCancellationRequested()
        {
            RecordingHandler handler = new() { ResponseDelay = TimeSpan.FromSeconds(5) };
            handler.EnqueueToken("token-never-delivered", expiresInSeconds: 3600);
            SpotifyTokenClient tokenClient = CreateTokenClient(handler);

            using CancellationTokenSource cts = new();
            Task<string> pending = tokenClient.GetAccessTokenAsync(cts.Token);
            await cts.CancelAsync();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }

        [Fact]
        public async Task GetAccessTokenAsync_Throws_WhenCredentialsMissing()
        {
            RecordingHandler handler = new();
            // Test-Fixture – Ausnahme von der IHttpClientFactory-Pflicht: der RecordingHandler ist ein
            // Test-Double, das genau diesem Client ungeteilt zugeordnet sein muss, damit CallCount stimmt.
            SpotifyTokenClient tokenClient = new(
                new SingleHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://accounts.spotify.test/") }),
                new NullCredentialsProvider(),
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()),
                new FakeClock());

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => tokenClient.GetAccessTokenAsync());
            Assert.Equal(0, handler.CallCount);
        }

        private static SpotifyTokenClient CreateTokenClient(HttpMessageHandler handler)
        {
            // Test-Fixture – Ausnahme von der IHttpClientFactory-Pflicht: jeder Test liefert seinen eigenen
            // RecordingHandler als Test-Double; der muss ungeteilt in diesen Client gehängt werden.
            HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://accounts.spotify.test/") };
            return new SpotifyTokenClient(
                new SingleHttpClientFactory(httpClient),
                new StaticCredentialsProvider("id", "secret"),
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()),
                new FakeClock());
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

        private sealed class NullCredentialsProvider : ISpotifyClientCredentialsProvider
        {
            public Task<SpotifyClientCredentials?> GetAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<SpotifyClientCredentials?>(null);
        }

        /// <summary>
        /// HTTP-Handler, der Token-Responses aus einer Queue liefert und Aufrufe zählt.
        /// </summary>
        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Queue<(string Token, int ExpiresIn)> _responses = new();
            private int _callCount;

            public int CallCount => _callCount;

            public TimeSpan ResponseDelay { get; set; } = TimeSpan.Zero;

            public void EnqueueToken(string token, int expiresInSeconds)
                => _responses.Enqueue((token, expiresInSeconds));

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                _ = Interlocked.Increment(ref _callCount);

                if (ResponseDelay > TimeSpan.Zero)
                {
                    // bewusst: simulierte Server-Latenz für Parallel-Cache-Test
                    await Task.Delay(ResponseDelay, cancellationToken).ConfigureAwait(false);
                }

                if (!_responses.TryDequeue(out (string Token, int ExpiresIn) entry))
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                string json = $$"""{"access_token":"{{entry.Token}}","token_type":"Bearer","expires_in":{{entry.ExpiresIn}}}""";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
        }
    }
}

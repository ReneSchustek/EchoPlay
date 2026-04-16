using System.Net;
using EchoPlay.App.Services;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="RateLimitMessageHandler"/>.
    /// Prüfen, dass der Handler vor jedem Request den <see cref="IHostRateLimiter"/>
    /// konsultiert, das Abbruch-Token respektiert und die Antwort unverändert durchreicht.
    /// </summary>
    public sealed class RateLimitMessageHandlerTests
    {
        [Fact]
        public async Task SendAsync_CallsRateLimiter_WithRequestHost()
        {
            RecordingRateLimiter limiter = new();
            RecordingInnerHandler inner = new(HttpStatusCode.OK);
            RateLimitMessageHandler sut = new(limiter) { InnerHandler = inner };
            using HttpClient client = new(sut);

            using HttpResponseMessage response = await client.GetAsync(new Uri("https://api.discogs.com/search"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            _ = Assert.Single(limiter.Hosts);
            Assert.Equal("api.discogs.com", limiter.Hosts[0]);
            Assert.Equal(1, inner.CallCount);
        }

        [Fact]
        public async Task SendAsync_SkipsRateLimiter_WhenRequestUriIsNull()
        {
            RecordingRateLimiter limiter = new();
            RecordingInnerHandler inner = new(HttpStatusCode.OK);
            RateLimitMessageHandler sut = new(limiter) { InnerHandler = inner };

            using HttpRequestMessage request = new();
            using HttpResponseMessage response = await InvokeSendAsync(sut, request, CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(limiter.Hosts);
        }

        [Fact]
        public async Task SendAsync_PropagatesCancellation_BeforeInnerHandlerIsCalled()
        {
            RecordingRateLimiter limiter = new() { ThrowOnWait = new OperationCanceledException() };
            RecordingInnerHandler inner = new(HttpStatusCode.OK);
            RateLimitMessageHandler sut = new(limiter) { InnerHandler = inner };
            using HttpClient client = new(sut);

            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.GetAsync(new Uri("https://musicbrainz.org/ws/2/release"), cts.Token));

            Assert.Equal(0, inner.CallCount);
        }

        [Fact]
        public async Task SendAsync_ForwardsResponseUnchanged()
        {
            RecordingRateLimiter limiter = new();
            RecordingInnerHandler inner = new(HttpStatusCode.ServiceUnavailable);
            RateLimitMessageHandler sut = new(limiter) { InnerHandler = inner };
            using HttpClient client = new(sut);

            using HttpResponseMessage response = await client.GetAsync(new Uri("https://itunes.apple.com/search"));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("itunes.apple.com", limiter.Hosts[0]);
        }

        [Fact]
        public async Task SendAsync_WaitsPerRequest_EvenWhenRepeated()
        {
            // Bei einem Retry in der Resilience-Pipeline ruft der innere RateLimit-Handler
            // erneut WaitAsync auf — die API-Quota wird auch bei Wiederholungen eingehalten.
            RecordingRateLimiter limiter = new();
            RecordingInnerHandler inner = new(HttpStatusCode.OK);
            RateLimitMessageHandler sut = new(limiter) { InnerHandler = inner };
            using HttpClient client = new(sut);

            using HttpResponseMessage r1 = await client.GetAsync(new Uri("https://musicbrainz.org/a"));
            using HttpResponseMessage r2 = await client.GetAsync(new Uri("https://musicbrainz.org/b"));
            using HttpResponseMessage r3 = await client.GetAsync(new Uri("https://musicbrainz.org/c"));

            Assert.Equal(3, limiter.Hosts.Count);
            Assert.All(limiter.Hosts, host => Assert.Equal("musicbrainz.org", host));
            Assert.Equal(3, inner.CallCount);
        }

        private static Task<HttpResponseMessage> InvokeSendAsync(
            RateLimitMessageHandler handler,
            HttpRequestMessage request,
            CancellationToken ct)
        {
            System.Reflection.MethodInfo method = typeof(HttpMessageHandler).GetMethod(
                "SendAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            return (Task<HttpResponseMessage>)method.Invoke(handler, new object[] { request, ct })!;
        }

        private sealed class RecordingRateLimiter : IHostRateLimiter
        {
            public List<string> Hosts { get; } = [];
            public Exception? ThrowOnWait { get; set; }

            public Task WaitAsync(string host, CancellationToken ct = default)
            {
                if (ThrowOnWait is not null)
                {
                    throw ThrowOnWait;
                }
                Hosts.Add(host);
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }

        private sealed class RecordingInnerHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            public int CallCount { get; private set; }

            public RecordingInnerHandler(HttpStatusCode status)
            {
                _status = status;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromResult(new HttpResponseMessage(_status));
            }
        }
    }
}

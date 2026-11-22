using System.Collections.Concurrent;
using System.Net;
using EchoPlay.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Integrationstests für die HttpClient-Resilience-Pipeline.
    /// Prüft das Zusammenspiel von <see cref="AddStandardResilienceHandler"/>
    /// und <see cref="RateLimitMessageHandler"/> mit einem stubbed Primary-Handler.
    /// </summary>
    public sealed class ResiliencePipelineIntegrationTests
    {
        /// <summary>
        /// Testfreundliche Werte ohne Backoff, damit der Test nicht sekundenlang läuft: zwei
        /// Wiederholungen ohne Wartezeit.
        /// </summary>
        private static void UseTwoImmediateRetries(Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions options)
        {
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.BackoffType = Polly.DelayBackoffType.Constant;
            options.Retry.UseJitter = false;
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
        }

        [Fact]
        public async Task Pipeline_RetriesOn503_AndCallsRateLimiterForEachAttempt()
        {
            CountingRateLimiter limiter = new();
            ServiceCollection services = new();
            _ = services.AddSingleton<IHostRateLimiter>(limiter);
            _ = services.AddTransient<RateLimitMessageHandler>();

            QueuedPrimaryHandler primary = new(
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                new HttpResponseMessage(HttpStatusCode.OK));

            IHttpClientBuilder clientBuilder = services.AddHttpClient("Test");
            _ = clientBuilder.AddStandardResilienceHandler(UseTwoImmediateRetries);
            _ = clientBuilder.AddHttpMessageHandler<RateLimitMessageHandler>()
                .ConfigurePrimaryHttpMessageHandler(() => primary);

            ServiceProvider provider = services.BuildServiceProvider();
            IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();
            HttpClient client = factory.CreateClient("Test");

            using HttpResponseMessage response = await client.GetAsync(
                new Uri("https://musicbrainz.org/ws/2/release"), cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(3, primary.CallCount);         // 1 Original + 2 Retries
            Assert.Equal(3, limiter.WaitCount);         // RateLimit greift bei jedem Attempt
            Assert.All(limiter.Hosts, host => Assert.Equal("musicbrainz.org", host));
        }

        /// <summary>
        /// Zählt Wartebitten und Hosts threadsicher.
        /// </summary>
        private sealed class CountingRateLimiter : IHostRateLimiter
        {
            private int _waitCount;
            public int WaitCount => _waitCount;
            public ConcurrentBag<string> Hosts { get; } = [];

            public Task WaitAsync(string host, CancellationToken ct = default)
            {
                _ = Interlocked.Increment(ref _waitCount);
                Hosts.Add(host);
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Gibt die vorgegebenen Antworten der Reihe nach aus und zählt die Aufrufe.
        /// </summary>
        private sealed class QueuedPrimaryHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;
            private int _callCount;
            public int CallCount => _callCount;

            public QueuedPrimaryHandler(params HttpResponseMessage[] responses)
            {
                _responses = new Queue<HttpResponseMessage>(responses);
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                _ = Interlocked.Increment(ref _callCount);
                return Task.FromResult(_responses.Dequeue());
            }
        }
    }
}

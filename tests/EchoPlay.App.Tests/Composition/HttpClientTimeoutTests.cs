using EchoPlay.App.Composition;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Polly.Timeout;
using System.Net;
using System.Text;

namespace EchoPlay.App.Tests.Composition
{
    /// <summary>
    /// Prüft, nach welcher Zeit eine hängende Anfrage tatsächlich aufgibt, und zwar über den
    /// echten Container: Client aus <see cref="IHttpClientFactory"/>, Wiederholungsregel wie
    /// in der Anwendung, nur die Gegenstelle ist ein Handler, der nie antwortet.
    /// </summary>
    /// <remarks>
    /// Der Anlass: Die Wiederholungsregel stellt <see cref="HttpClient.Timeout"/> auf
    /// unendlich, wenn sie nach der Client-Konfiguration angehängt wird. Eine dort gesetzte
    /// Grenze sah aus, als gelte sie, und galt nicht. Ein Test, der nur die Optionen liest,
    /// hätte das nicht bemerkt; dieser misst die Wirkung.
    ///
    /// Die Pipeline liest ihre Uhr aus dem Container. Mit einer künstlichen Uhr laufen die
    /// dreißig Sekunden in Bruchteilen einer Sekunde ab. Die Grenze des
    /// <see cref="HttpClient"/> selbst läuft dagegen auf der echten Uhr; griffe sie, käme
    /// keine <see cref="TimeoutRejectedException"/>, und der Test schlüge fehl.
    /// </remarks>
    public sealed class HttpClientTimeoutTests
    {
        [Fact]
        public async Task CoverDownloadClient_HangingServer_GivesUpAfterThirtySeconds()
        {
            TimeoutOutcome outcome = await MeasureAsync(
                CoverDownloader.HttpClientName,
                new Uri("https://is1-ssl.mzstatic.com/image/cover.jpg"));

            AssertGaveUpAt(TimeSpan.FromSeconds(30), outcome);
        }

        [Fact]
        public async Task UpdateCheckClient_HangingServer_GivesUpAfterUpdateCheckLimit()
        {
            TimeoutOutcome outcome = await MeasureAsync(
                "UpdateCheck",
                new Uri("https://api.github.com/repos/owner/repo/releases/latest"));

            AssertGaveUpAt(HttpClientRegistration.UpdateCheckTotalTimeout, outcome);
        }

        [Fact]
        public async Task SpotifyTokenClient_HangingServer_GivesUpAfterTokenLimit()
        {
            TimeoutOutcome outcome = await MeasureAsync(
                "SpotifyToken",
                new Uri("https://accounts.spotify.com/api/token"));

            AssertGaveUpAt(ProviderRegistration.TokenTotalTimeout, outcome);
        }

        [Fact]
        public async Task SpotifyApiClient_HangingServer_GivesUpAfterProviderLimit()
        {
            TimeoutOutcome outcome = await MeasureAsync(
                "SpotifyApi",
                new Uri("https://api.spotify.com/v1/search?q=test"));

            AssertGaveUpAt(HttpClientRegistration.ProviderTotalTimeout, outcome);
        }

        [Fact]
        public async Task AppleMusicSearchClient_HangingServer_GivesUpAfterProviderLimit()
        {
            // Steht für alle typisierten Clients der fremden Schnittstellen; sie bekommen
            // ihre Regel über denselben Weg.
            TimeoutOutcome outcome = await MeasureAsync(
                "IAppleMusicSearchClient",
                new Uri("https://itunes.apple.com/search?term=test"));

            AssertGaveUpAt(HttpClientRegistration.ProviderTotalTimeout, outcome);
        }

        [Fact]
        public void ProviderLimits_LeaveRoomForAtLeastTwoAttempts()
        {
            // Eine Gesamtgrenze, die nur einen Versuch zulässt, machte die Wiederholungsregel
            // wirkungslos, ohne dass es jemand merkt.
            Assert.True(HttpClientRegistration.ProviderTotalTimeout >= 2 * HttpClientRegistration.ProviderAttemptTimeout);
            Assert.True(ProviderRegistration.TokenTotalTimeout >= 2 * ProviderRegistration.TokenAttemptTimeout);
            Assert.True(HttpClientRegistration.UpdateCheckTotalTimeout >= 2 * HttpClientRegistration.UpdateCheckAttemptTimeout);
        }

        private static void AssertGaveUpAt(TimeSpan expectedLimit, TimeoutOutcome outcome)
        {
            _ = Assert.IsType<TimeoutRejectedException>(outcome.Error);
            Assert.InRange(outcome.Elapsed, expectedLimit, expectedLimit + ClockDriver.Tolerance);

            // Mindestens ein zweiter Versuch: Die Grenze je Versuch ist kürzer als die
            // Gesamtgrenze, sonst wäre die Wiederholungsregel ohne Wirkung.
            Assert.True(outcome.Attempts >= 2, $"Nur {outcome.Attempts} Versuch(e) bis zur Grenze.");
        }

        /// <summary>
        /// Baut den Container wie die Anwendung, lässt die Gegenstelle des genannten Clients
        /// hängen und dreht die künstliche Uhr, bis die Anfrage aufgibt.
        /// </summary>
        private static async Task<TimeoutOutcome> MeasureAsync(string clientName, Uri requestUri)
        {
            ObservedClock time = ClockDriver.CreateClock();
            HangingHttpHandler hanging = new();

            using ServiceProvider provider = BuildProvider(time, clientName, hanging);
            HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

            Task<HttpResponseMessage> send = client.GetAsync(requestUri, TestContext.Current.CancellationToken);
            TimeSpan elapsed = await ClockDriver.RunUntilCompletedAsync(time, send, hanging);
            Exception error = await Assert.ThrowsAnyAsync<Exception>(() => send);
            return new TimeoutOutcome(elapsed, error, hanging.Attempts);
        }

        private static ServiceProvider BuildProvider(FakeTimeProvider time, string hangingClient, HangingHttpHandler hanging)
        {
            ServiceCollection services = new();
            _ = services.AddSingleton<TimeProvider>(time);
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddSingleton<IHostRateLimiter>(new PassThroughRateLimiter());
            _ = services.AddTransient<RateLimitMessageHandler>();

            _ = services.AddEchoPlayHttpClients();
            _ = services.AddEchoPlayProviders(new ConfigurationBuilder().Build());
            _ = services.AddEchoPlayTypedHttpClients();

            // Zugangsdaten nicht aus dem verschlüsselten Speicher des Nutzers, und der
            // Token kommt sofort, damit beim Schnittstellen-Client nur die Anfrage selbst hängt.
            _ = services.AddSingleton<ISpotifyClientCredentialsProvider>(new FixedCredentialsProvider());
            if (hangingClient != "SpotifyToken")
            {
                _ = services.AddHttpClient("SpotifyToken").ConfigurePrimaryHttpMessageHandler(() => new TokenResponder());
            }

            _ = services.AddHttpClient(hangingClient).ConfigurePrimaryHttpMessageHandler(() => hanging);
            return services.BuildServiceProvider();
        }

        /// <summary>Gemessene Zeit bis zum Aufgeben, der Fehler und die Zahl der Versuche.</summary>
        private sealed record TimeoutOutcome(TimeSpan Elapsed, Exception Error, int Attempts);

        /// <summary>Antwortet auf jede Token-Anfrage sofort mit einem gültigen Token.</summary>
        private sealed class TokenResponder : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpResponseMessage response = new(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"access_token\":\"test-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}",
                        Encoding.UTF8,
                        "application/json")
                };
                return Task.FromResult(response);
            }
        }

        /// <summary>Feste Zugangsdaten, damit der Token-Client nicht auf den Nutzerspeicher greift.</summary>
        private sealed class FixedCredentialsProvider : ISpotifyClientCredentialsProvider
        {
            public Task<SpotifyClientCredentials?> GetAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<SpotifyClientCredentials?>(new SpotifyClientCredentials("client-id", "client-secret"));
        }

        /// <summary>Ratenbremse ohne Wartezeit; die Bremse selbst ist nicht Gegenstand dieser Tests.</summary>
        private sealed class PassThroughRateLimiter : IHostRateLimiter
        {
            public Task WaitAsync(string host, CancellationToken ct = default) => Task.CompletedTask;

            public void Dispose()
            {
            }
        }
    }
}

using System.Diagnostics.CodeAnalysis;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Auth;
using EchoPlay.Spotify.Clients;
using EchoPlay.Spotify.Configuration;
using EchoPlay.Spotify.Tests.Fakes;
using Microsoft.Extensions.Configuration;

namespace EchoPlay.Spotify.Tests.Live
{
    /// <summary>
    /// Gemeinsame Test-Infrastruktur für alle Spotify-Live-Smoke-Tests.
    ///
    /// Die Fixture erstellt manuell eine vollstaendige HTTP-Pipeline
    /// mit Token-Handling und stellt den API-Client allen Tests bereit.
    ///
    /// Der bewusste Verzicht auf IHttpClientFactory vermeidet
    /// Connection-Pool-Probleme, die in Test-Kontexten ohne Host auftreten.
    /// </summary>
    [SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "xUnit-Test-Infrastruktur muss public sein für den Test-Runner (IClassFixture<T>).")]
    public sealed class SpotifyLiveFixture : IDisposable
    {
        private readonly HttpClient _tokenHttpClient;
        private readonly HttpClient _apiHttpClient;

        /// <summary>
        /// Der vorkonfigurierte Spotify-API-Client mit Authentifizierung.
        /// </summary>
        public ISpotifyApiClient ApiClient { get; }

        /// <summary>
        /// Erstellt die Fixture mit manuell verdrahteter HTTP-Pipeline.
        /// </summary>
        public SpotifyLiveFixture()
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddUserSecrets<SpotifyLiveFixture>()
                .Build();

            SpotifyOptions spotifyOptions = configuration
                .GetSection("Spotify")
                .Get<SpotifyOptions>()
                ?? throw new InvalidOperationException("Spotify-Konfiguration fehlt.");

            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory =
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

            // HttpClient für Token-Anfragen (Client-Credentials-Flow)
            _tokenHttpClient = new HttpClient
            {
                BaseAddress = new Uri(spotifyOptions.AuthBaseUrl)
            };

            SpotifyTokenClient tokenClient = new(
                new SingleClientHttpClientFactory(_tokenHttpClient),
                new StaticCredentialsProvider(spotifyOptions.ClientId, spotifyOptions.ClientSecret),
                loggerFactory,
                new FakeClock());

            // Auth-Handler mit eigenem InnerHandler.
            // PooledConnectionLifetime = Zero verhindert, dass abgelaufene
            // Verbindungen wiederverwendet werden – Spotify schließt
            // Idle-Connections aggressiv, was sonst zu SocketExceptions führt.
            SpotifyAuthMessageHandler authHandler = new(tokenClient)
            {
                InnerHandler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.Zero
                }
            };

            // Test-Fixture – Ausnahme von der IHttpClientFactory-Pflicht: die Factory bietet keinen Zugang
            // zum SocketsHttpHandler-Setup, das wegen Spotifys Idle-Drops erforderlich ist (siehe oben).
            _apiHttpClient = new HttpClient(authHandler, disposeHandler: true)
            {
                BaseAddress = new Uri(spotifyOptions.ApiBaseUrl)
            };

            ApiClient = new SpotifyApiClient(_apiHttpClient, loggerFactory);
        }

        /// <summary>
        /// Gibt alle HTTP-Ressourcen frei.
        /// </summary>
        public void Dispose()
        {
            _apiHttpClient.Dispose();
            _tokenHttpClient.Dispose();
        }

        private sealed class SingleClientHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpClient _httpClient;

            public SingleClientHttpClientFactory(HttpClient httpClient) => _httpClient = httpClient;

            public HttpClient CreateClient(string name) => _httpClient;
        }

        private sealed class StaticCredentialsProvider : ISpotifyClientCredentialsProvider
        {
            private readonly SpotifyClientCredentials _credentials;

            public StaticCredentialsProvider(string clientId, string clientSecret)
                => _credentials = new SpotifyClientCredentials(clientId, clientSecret);

            public Task<SpotifyClientCredentials?> GetAsync(System.Threading.CancellationToken cancellationToken = default)
                => Task.FromResult<SpotifyClientCredentials?>(_credentials);
        }
    }
}

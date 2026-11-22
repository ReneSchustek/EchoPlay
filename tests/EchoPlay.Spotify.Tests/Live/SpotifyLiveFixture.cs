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
    /// Die Fixture erstellt manuell eine vollständige HTTP-Pipeline
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

        /// <summary>Liest die Spotify-Einstellungen aus appsettings.json und den Benutzergeheimnissen.</summary>
        private static SpotifyOptions LoadOptions()
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddUserSecrets<SpotifyLiveFixture>()
                .Build();

            return configuration
                .GetSection("Spotify")
                .Get<SpotifyOptions>()
                ?? throw new InvalidOperationException("Spotify-Konfiguration fehlt.");
        }

        /// <summary>
        /// Auth-Handler mit eigenem InnerHandler. PooledConnectionLifetime = Zero verhindert, dass
        /// abgelaufene Verbindungen wiederverwendet werden, denn Spotify schließt
        /// Idle-Connections aggressiv, was sonst zu SocketExceptions führt.
        /// </summary>
        private static SpotifyAuthMessageHandler CreateAuthHandler(SpotifyTokenClient tokenClient)
            => new(tokenClient)
            {
                InnerHandler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.Zero
                }
            };

        /// <summary>
        /// Erstellt die Fixture mit manuell verdrahteter HTTP-Pipeline.
        /// </summary>
        public SpotifyLiveFixture()
        {
            SpotifyOptions spotifyOptions = LoadOptions();

            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory =
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

            // HttpClient für Token-Anfragen (Client-Credentials-Flow)
            _tokenHttpClient = new HttpClient
            {
                BaseAddress = spotifyOptions.AuthBaseUrl
            };

            SpotifyTokenClient tokenClient = new(
                new SingleClientHttpClientFactory(_tokenHttpClient),
                new StaticCredentialsProvider(spotifyOptions.ClientId, spotifyOptions.ClientSecret),
                loggerFactory,
                new FakeClock());

            // Test-Fixture als Ausnahme von der IHttpClientFactory-Pflicht: Die Factory bietet keinen Zugang
            // zum SocketsHttpHandler-Setup, das wegen Spotifys Idle-Drops erforderlich ist (siehe CreateAuthHandler).
            _apiHttpClient = new HttpClient(CreateAuthHandler(tokenClient), disposeHandler: true)
            {
                BaseAddress = spotifyOptions.ApiBaseUrl
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

        /// <summary>
        /// Gibt für jeden Namen denselben Client aus.
        /// </summary>
        private sealed class SingleClientHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpClient _httpClient;

            public SingleClientHttpClientFactory(HttpClient httpClient) => _httpClient = httpClient;

            public HttpClient CreateClient(string name) => _httpClient;
        }

        /// <summary>
        /// Liefert die Zugangsdaten aus den Optionen des Live-Laufs.
        /// </summary>
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

using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Auth;
using EchoPlay.Spotify.Clients;
using EchoPlay.Spotify.Configuration;
using Microsoft.Extensions.Configuration;

namespace EchoPlay.Spotify.Tests.Live
{
    /// <summary>
    /// Gemeinsame Test-Infrastruktur fuer alle Spotify-Live-Smoke-Tests.
    ///
    /// Die Fixture erstellt manuell eine vollstaendige HTTP-Pipeline
    /// mit Token-Handling und stellt den API-Client allen Tests bereit.
    ///
    /// Der bewusste Verzicht auf IHttpClientFactory vermeidet
    /// Connection-Pool-Probleme, die in Test-Kontexten ohne Host auftreten.
    /// </summary>
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

            // HttpClient fuer Token-Anfragen (Client-Credentials-Flow)
            _tokenHttpClient = new HttpClient
            {
                BaseAddress = new Uri(spotifyOptions.AuthBaseUrl)
            };

            SpotifyTokenClient tokenClient = new(_tokenHttpClient, spotifyOptions, loggerFactory);

            // Auth-Handler mit eigenem InnerHandler.
            // PooledConnectionLifetime = Zero verhindert, dass abgelaufene
            // Verbindungen wiederverwendet werden – Spotify schliesst
            // Idle-Connections aggressiv, was sonst zu SocketExceptions fuehrt.
            SpotifyAuthMessageHandler authHandler = new(tokenClient)
            {
                InnerHandler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.Zero
                }
            };

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
    }
}

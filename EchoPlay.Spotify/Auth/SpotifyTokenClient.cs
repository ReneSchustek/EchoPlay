using EchoPlay.Spotify.Configuration;
using EchoPlay.Spotify.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EchoPlay.Spotify.Auth
{
    /// <summary>
    /// Kapselt den Client-Credentials-Flow zur Authentifizierung gegen die Spotify-Web-API.
    ///
    /// Die Klasse ist bewusst zustandsbehaftet, um Tokens effizient zwischenzuverwenden.
    /// </summary>
    /// <remarks>
    /// Initialisiert den Token-Client mit Spotify-App-Zugangsdaten.
    /// </remarks>
    /// <param name="httpClient">HttpClient für Token-Anfragen.</param>
    /// <param name="options">Spotify-Konfiguration mit ClientId und ClientSecret.</param>
    /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
    public sealed class SpotifyTokenClient(
        HttpClient httpClient,
        SpotifyOptions options,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory)
    {
        private readonly HttpClient _httpClient = httpClient;
        private readonly string _clientId = options.ClientId;
        private readonly string _clientSecret = options.ClientSecret;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger = loggerFactory.CreateLogger("SpotifyTokenClient");

        private SpotifyAccessToken? _currentToken;

        /// <summary>
        /// Liefert ein gültiges Zugriffstoken. Holt bei Bedarf automatisch ein neues.
        /// </summary>
        /// <returns>Ein gültiger Spotify-Zugriffstoken.</returns>
        /// <exception cref="HttpRequestException">Wenn die Spotify-Token-API nicht erreichbar ist oder die Zugangsdaten ungültig sind.</exception>
        /// <exception cref="JsonException">Wenn die Token-Antwort nicht verarbeitet werden kann.</exception>
        public async Task<string> GetAccessTokenAsync()
        {
            if (_currentToken != null && !_currentToken.IsExpired)
            {
                _logger.Debug("Gültiges Token aus Cache verwendet.");
                return _currentToken.AccessToken;
            }

            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("API:Spotify:GetAccessToken");

            _logger.Debug("Kein gültiges Token vorhanden – neues Token wird angefordert.");

            // Spotify erwartet ClientId und Secret Base64-kodiert im Authorization-Header.
            string credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));

            try
            {
                // Pro Retry-Versuch muss ein neues HttpRequestMessage erzeugt werden,
                // da es nach dem Senden nicht wiederverwendet werden kann.
                using HttpResponseMessage response = await SpotifyHttpRetry.SendWithRetryAsync(async () =>
                {
                    using HttpRequestMessage req = new(HttpMethod.Post, "api/token");
                    req.Headers.Authorization = new("Basic", credentials);
                    req.Content = new FormUrlEncodedContent(
                    [
                        new KeyValuePair<string, string>("grant_type", "client_credentials")
                    ]);
                    return await _httpClient.SendAsync(req).ConfigureAwait(false);
                }).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                using Stream contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using JsonDocument document = await JsonDocument.ParseAsync(contentStream).ConfigureAwait(false);

                // GetString()! – Null-Forgiving: Spotify garantiert "access_token" im Response,
                // wenn EnsureSuccessStatusCode() nicht geworfen hat.
                string accessToken = document.RootElement.GetProperty("access_token").GetString()!;
                int expiresInSeconds = document.RootElement.GetProperty("expires_in").GetInt32();

                // Sicherheitsmarge: Token läuft bewusst etwas früher ab, um Grenzfälle während Requests zu vermeiden.
                _currentToken = new()
                {
                    AccessToken = accessToken,
                    ExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds - 60)
                };

                _logger.Info("Spotify-Token erfolgreich erneuert.");

                return _currentToken.AccessToken;
            }
            catch (HttpRequestException ex)
            {
                _logger.Error(
                    "Spotify-Authentifizierung fehlgeschlagen. Die API ist möglicherweise nicht erreichbar oder die Zugangsdaten sind ungültig.",
                    ex);
                throw;
            }
            catch (JsonException ex)
            {
                _logger.Error(
                    "Spotify-Token-Antwort konnte nicht verarbeitet werden. Das API-Format hat sich möglicherweise geändert.",
                    ex);
                throw;
            }
        }
    }
}

using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Spotify.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EchoPlay.Spotify.Auth
{
    /// <summary>
    /// Kapselt den Client-Credentials-Flow zur Authentifizierung gegen die Spotify-Web-API.
    ///
    /// Ist als Singleton ausgelegt — der Token-Cache wird prozessweit gehalten, damit
    /// mehrere DI-Scopes denselben Access-Token nutzen können. Parallele Token-Requests
    /// werden durch einen Semaphor serialisiert, sodass bei gleichzeitiger Anforderung
    /// nur ein einziger HTTP-Roundtrip zu <c>accounts.spotify.com</c> entsteht.
    /// </summary>
    public sealed class SpotifyTokenClient : IDisposable
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ISpotifyClientCredentialsProvider _credentialsProvider;
        private readonly IClock _clock;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger;
        private readonly SemaphoreSlim _tokenLock = new(1, 1);

        private SpotifyAccessToken? _currentToken;

        /// <summary>
        /// Initialisiert den Token-Client mit den DI-Dependencies. Die Zugangsdaten
        /// werden erst beim ersten <see cref="GetAccessTokenAsync"/>-Aufruf asynchron
        /// aus dem <paramref name="credentialsProvider"/> geholt, damit der Konstruktor
        /// nie synchron blockiert.
        /// </summary>
        public SpotifyTokenClient(
            IHttpClientFactory httpClientFactory,
            ISpotifyClientCredentialsProvider credentialsProvider,
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory,
            IClock clock)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            ArgumentNullException.ThrowIfNull(clock);
            _httpClientFactory = httpClientFactory;
            _credentialsProvider = credentialsProvider;
            _clock = clock;
            _logger = loggerFactory.CreateLogger("SpotifyTokenClient");
        }

        /// <summary>
        /// Liefert ein gültiges Zugriffstoken. Nutzt einen prozessweiten Cache und
        /// serialisiert parallele Aufrufe über einen Semaphor.
        /// </summary>
        /// <exception cref="InvalidOperationException">Wenn keine Spotify-Credentials konfiguriert sind.</exception>
        /// <exception cref="HttpRequestException">Wenn die Spotify-Token-API nicht erreichbar ist oder die Zugangsdaten ungültig sind.</exception>
        /// <exception cref="JsonException">Wenn die Token-Antwort nicht verarbeitet werden kann.</exception>
        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            if (_currentToken != null && !_currentToken.IsExpired(_clock.UtcNow))
            {
                _logger.Debug("Gültiges Token aus Cache verwendet.");
                return _currentToken.AccessToken;
            }

            await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Zweite Prüfung nach Lock-Erwerb: ein anderer Task hat evtl. inzwischen
                // einen Token geholt, während wir am Semaphor warteten.
                if (_currentToken != null && !_currentToken.IsExpired(_clock.UtcNow))
                {
                    _logger.Debug("Gültiges Token aus Cache verwendet (post-lock).");
                    return _currentToken.AccessToken;
                }

                _currentToken = await RequestNewTokenAsync(cancellationToken).ConfigureAwait(false);
                return _currentToken.AccessToken;
            }
            finally
            {
                _ = _tokenLock.Release();
            }
        }

        /// <summary>
        /// Verwirft den gecachten Token. Der nächste <see cref="GetAccessTokenAsync"/>-Aufruf
        /// fordert zwingend einen neuen Token an. Wird vom <see cref="SpotifyAuthMessageHandler"/>
        /// bei einer 401-Response aufgerufen, um nach einem serverseitigen Revoke automatisch
        /// einen frischen Token zu ziehen.
        /// </summary>
        public async Task InvalidateAsync(CancellationToken cancellationToken = default)
        {
            await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _currentToken = null;
                _logger.Debug("Token-Cache invalidiert.");
            }
            finally
            {
                _ = _tokenLock.Release();
            }
        }

        private async Task<SpotifyAccessToken> RequestNewTokenAsync(CancellationToken cancellationToken)
        {
            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("API:Spotify:GetAccessToken");

            SpotifyClientCredentials credentials = await _credentialsProvider
                .GetAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Spotify-Credentials sind nicht konfiguriert. Nutzer muss ClientId und Secret in den Einstellungen hinterlegen.");

            _logger.Debug("Kein gültiges Token vorhanden – neues Token wird angefordert.");

            // Spotify erwartet ClientId und Secret Base64-kodiert im Authorization-Header.
            string basicAuth = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{credentials.ClientId}:{credentials.ClientSecret}"));

            HttpClient tokenHttpClient = _httpClientFactory.CreateClient("SpotifyToken");

            try
            {
                // Pro Retry-Versuch muss ein neues HttpRequestMessage erzeugt werden,
                // da es nach dem Senden nicht wiederverwendet werden kann.
                using HttpResponseMessage response = await SpotifyHttpRetry.SendWithRetryAsync(async () =>
                {
                    using HttpRequestMessage req = new(HttpMethod.Post, "api/token");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
                    req.Content = new FormUrlEncodedContent(
                    [
                        new KeyValuePair<string, string>("grant_type", "client_credentials")
                    ]);
                    return await tokenHttpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
                _ = response.EnsureSuccessStatusCode();

                using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using JsonDocument document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken).ConfigureAwait(false);

                // GetString()! – Null-Forgiving: Spotify garantiert "access_token" im Response,
                // wenn EnsureSuccessStatusCode() nicht geworfen hat.
                string accessToken = document.RootElement.GetProperty("access_token").GetString()!;
                int expiresInSeconds = document.RootElement.GetProperty("expires_in").GetInt32();

                _logger.Info("Spotify-Token erfolgreich erneuert.");

                // Sicherheitsmarge: Token läuft bewusst etwas früher ab, um Grenzfälle während Requests zu vermeiden.
                return new SpotifyAccessToken
                {
                    AccessToken = accessToken,
                    ExpiresAtUtc = _clock.UtcNow.AddSeconds(expiresInSeconds - 60)
                };
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

        /// <inheritdoc/>
        public void Dispose() => _tokenLock.Dispose();
    }
}

using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.Spotify.Auth
{
    /// <summary>
    /// HTTP-Message-Handler zur automatischen Anreicherung von Spotify-API-Requests mit einem gültigen Authorization-Header.
    /// Die Klasse entkoppelt Token-Handling vollständig vom eigentlichen API-Client.
    /// </summary>
    /// <remarks>
    /// Initialisiert den Handler mit dem Token-Client.
    /// </remarks>
    /// <param name="tokenClient">Der Client zur Beschaffung gültiger Zugriffstoken.</param>
    public sealed class SpotifyAuthMessageHandler(SpotifyTokenClient tokenClient) : DelegatingHandler
    {
        private readonly SpotifyTokenClient _tokenClient = tokenClient;

        /// <summary>
        /// Fügt jedem ausgehenden Request automatisch einen gültigen Bearer-Token hinzu.
        /// </summary>
        /// <param name="request">Der ausgehende HTTP-Request.</param>
        /// <param name="cancellationToken">Abbruchtoken.</param>
        /// <returns>Die HTTP-Antwort von Spotify.</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Das Token wird hier bewusst pro Request abgefragt, da der TokenClient intern cached und bei Bedarf erneuert.
            string accessToken = await _tokenClient.GetAccessTokenAsync().ConfigureAwait(false);

            // Der Authorization-Header wird zentral gesetzt, damit kein anderer Teil der Anwendung Spotify-spezifische Header kennen muss.
            request.Headers.Authorization = new("Bearer", accessToken);

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}

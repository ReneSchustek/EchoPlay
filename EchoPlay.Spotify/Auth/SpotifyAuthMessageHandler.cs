using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.Spotify.Auth
{
    /// <summary>
    /// HTTP-Message-Handler zur automatischen Anreicherung von Spotify-API-Requests mit einem gültigen Authorization-Header.
    /// Die Klasse entkoppelt Token-Handling vollständig vom eigentlichen API-Client.
    ///
    /// Bei einer 401-Antwort wird der gecachte Token einmalig invalidiert und der Request
    /// mit einem frisch geholten Token wiederholt. Ein Infinite-Loop-Schutz verhindert,
    /// dass ein dauerhaft ungültiger Token (z.B. bei revoked credentials) endlose Retries
    /// auslöst — nach dem zweiten 401 wird die Original-Response propagiert.
    /// </summary>
    /// <remarks>
    /// Initialisiert den Handler mit dem Token-Client.
    /// </remarks>
    /// <param name="tokenClient">Der Client zur Beschaffung gültiger Zugriffstoken.</param>
    public sealed class SpotifyAuthMessageHandler(SpotifyTokenClient tokenClient) : DelegatingHandler
    {
        // Marker im HttpRequestMessage.Options: signalisiert, dass der Request bereits
        // einmal wegen 401 wiederholt wurde. Verhindert endlose Refresh-Loops.
        private static readonly HttpRequestOptionsKey<bool> RetryAfter401Key = new("EchoPlay.Spotify.RetryAfter401");

        // Lifetime-Ownership liegt beim DI-Container (Singleton); der Handler darf den
        // TokenClient nicht disposen, weil er über alle AuthMessageHandler-Instanzen
        // geteilt wird. Daher CA2213-Unterdrückung.
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
            Justification = "SpotifyTokenClient ist ein Singleton und wird vom DI-Container verwaltet.")]
        private readonly SpotifyTokenClient _tokenClient = tokenClient;

        /// <summary>
        /// Fügt jedem ausgehenden Request automatisch einen gültigen Bearer-Token hinzu
        /// und wiederholt den Request bei 401 einmalig mit frisch geholtem Token.
        /// </summary>
        /// <param name="request">Der ausgehende HTTP-Request.</param>
        /// <param name="cancellationToken">Abbruchtoken.</param>
        /// <returns>Die HTTP-Antwort von Spotify.</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            await AttachBearerTokenAsync(request, cancellationToken).ConfigureAwait(false);

            HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // 401 → genau einmal erneut probieren mit frischem Token. Der Marker verhindert
            // eine zweite Runde, falls auch der neue Token vom Server abgelehnt wird.
            if (response.StatusCode == HttpStatusCode.Unauthorized && !HasRetryMarker(request))
            {
                response.Dispose();

                await _tokenClient.InvalidateAsync(cancellationToken).ConfigureAwait(false);

                request.Options.Set(RetryAfter401Key, true);
                await AttachBearerTokenAsync(request, cancellationToken).ConfigureAwait(false);

                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }

            return response;
        }

        private async Task AttachBearerTokenAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string accessToken = await _tokenClient.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        private static bool HasRetryMarker(HttpRequestMessage request)
            => request.Options.TryGetValue(RetryAfter401Key, out bool retried) && retried;
    }
}

using System.Net;

namespace EchoPlay.Spotify.Http
{
    /// <summary>
    /// Kapselt die Retry-Logik für transiente HTTP-Fehler der Spotify-API.
    ///
    /// Wiederholungsversuche erfolgen ausschließlich bei transienten Fehlern –
    /// Netzwerkausfälle, Server-Fehler (5xx) und Rate-Limiting (429).
    /// Anwendungsfehler (4xx außer 429) und Parse-Fehler werden sofort weitergegeben.
    /// </summary>
    internal static class SpotifyHttpRetry
    {
        // Linearer Backoff: 500 ms vor dem 2. Versuch, 1000 ms vor dem 3. Versuch.
        private static readonly TimeSpan[] Delays =
        [
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(1000)
        ];

        /// <summary>
        /// Führt die übergebene HTTP-Anfrage aus und wiederholt sie bei transienten Fehlern.
        ///
        /// Als transient gelten: HTTP 5xx, HTTP 429 (Rate Limit) und Verbindungsfehler
        /// (<see cref="HttpRequestException"/> ohne Status-Code).
        /// HTTP 4xx (außer 429) und Parse-Fehler sind keine transienten Fehler und werden
        /// sofort weitergegeben.
        /// </summary>
        /// <param name="sendAsync">
        /// Factory-Funktion, die bei jedem Versuch eine neue HTTP-Anfrage erstellt und ausführt.
        /// Eine neue Instanz pro Versuch ist erforderlich, da <see cref="HttpRequestMessage"/>
        /// nicht wiederverwendet werden kann.
        /// </param>
        /// <param name="cancellationToken">Abbruchtoken für Wartezeiten zwischen den Versuchen.</param>
        /// <returns>
        /// Die HTTP-Antwort des letzten Versuchs. Der Aufrufer ist verantwortlich für
        /// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> und die Freigabe der Ressource.
        /// Nach erschöpften Versuchen mit transientem Status-Code wird die letzte fehlerhafte
        /// Antwort zurückgegeben.
        /// </returns>
        /// <exception cref="HttpRequestException">
        /// Bei Verbindungsfehlern (kein Status-Code) nach Ausschöpfung aller Versuche.
        /// </exception>
        internal static async Task<HttpResponseMessage> SendWithRetryAsync(
            Func<Task<HttpResponseMessage>> sendAsync,
            CancellationToken cancellationToken = default)
        {
            int maxRetries = Delays.Length;
            HttpRequestException? lastConnectionException = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    HttpResponseMessage response = await sendAsync().ConfigureAwait(false);

                    // Bei transienten Status-Codes (5xx, 429) wird nach Möglichkeit wiederholt.
                    if (IsTransientStatusCode(response.StatusCode) && attempt < maxRetries)
                    {
                        response.Dispose();
                        await Task.Delay(Delays[attempt], cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    return response;
                }
                catch (HttpRequestException ex) when (ex.StatusCode is null && attempt < maxRetries)
                {
                    // Verbindungsfehler ohne Status-Code (DNS, Timeout, Netzwerk) → Retry
                    lastConnectionException = ex;
                    await Task.Delay(Delays[attempt], cancellationToken).ConfigureAwait(false);
                }
            }

            // Alle Versuche erschöpft – letzten Verbindungsfehler weitergeben.
            throw lastConnectionException!;
        }

        /// <summary>
        /// Gibt an, ob ein HTTP-Status-Code als transient und damit wiederholbar gilt.
        /// </summary>
        /// <param name="statusCode">Der zu prüfende Status-Code.</param>
        /// <returns><see langword="true"/> für HTTP 429 (Rate Limit) und alle HTTP 5xx Status-Codes.</returns>
        private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
        }
    }
}

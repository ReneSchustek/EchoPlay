using EchoPlay.Spotify.Http;
using System.Net;

namespace EchoPlay.Spotify.Tests.Http
{
    /// <summary>
    /// Tests für <see cref="SpotifyHttpRetry"/>.
    ///
    /// Prüft, dass transiente Fehler (5xx, 429, Verbindungsausfälle) korrekt wiederholt
    /// werden und dass nicht-transiente Fehler (4xx) sofort zurückgegeben werden.
    /// </summary>
    public sealed class SpotifyHttpRetryTests
    {
        [Fact]
        public async Task SendWithRetryAsync_ReturnsResponse_OnFirstSuccess()
        {
            StubHttpMessageHandler handler = new();
            handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK));
            HttpClient client = new(handler) { BaseAddress = new Uri("http://fake/") };

            HttpResponseMessage response = await SpotifyHttpRetry.SendWithRetryAsync(
                () => client.GetAsync(new Uri("api/test", UriKind.Relative)));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task SendWithRetryAsync_Retries_On503_ThenSucceeds()
        {
            // Erster Aufruf schlägt mit 503 fehl, zweiter gelingt
            StubHttpMessageHandler handler = new();
            handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK));
            HttpClient client = new(handler) { BaseAddress = new Uri("http://fake/") };

            HttpResponseMessage response = await SpotifyHttpRetry.SendWithRetryAsync(
                () => client.GetAsync(new Uri("api/test", UriKind.Relative)));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, handler.CallCount);
        }

        [Fact]
        public async Task SendWithRetryAsync_Retries_On429_ThenSucceeds()
        {
            // Rate-Limiting (429) wird als transient behandelt
            StubHttpMessageHandler handler = new();
            handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK));
            HttpClient client = new(handler) { BaseAddress = new Uri("http://fake/") };

            HttpResponseMessage response = await SpotifyHttpRetry.SendWithRetryAsync(
                () => client.GetAsync(new Uri("api/test", UriKind.Relative)));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, handler.CallCount);
        }

        [Fact]
        public async Task SendWithRetryAsync_DoesNotRetry_On404()
        {
            // 404 ist kein transienter Fehler – kein Retry
            StubHttpMessageHandler handler = new();
            handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.NotFound));
            HttpClient client = new(handler) { BaseAddress = new Uri("http://fake/") };

            HttpResponseMessage response = await SpotifyHttpRetry.SendWithRetryAsync(
                () => client.GetAsync(new Uri("api/test", UriKind.Relative)));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task SendWithRetryAsync_DoesNotRetry_On401()
        {
            // Ungültige Zugangsdaten (401) sind kein transienter Fehler – kein Retry
            StubHttpMessageHandler handler = new();
            handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            HttpClient client = new(handler) { BaseAddress = new Uri("http://fake/") };

            HttpResponseMessage response = await SpotifyHttpRetry.SendWithRetryAsync(
                () => client.GetAsync(new Uri("api/test", UriKind.Relative)));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task SendWithRetryAsync_ReturnsLastResponse_WhenAllRetriesExhaustedWith5xx()
        {
            // Alle 3 Versuche scheitern – letzte fehlerhafte Antwort wird zurückgegeben
            StubHttpMessageHandler handler = new();
            handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            HttpClient client = new(handler) { BaseAddress = new Uri("http://fake/") };

            HttpResponseMessage response = await SpotifyHttpRetry.SendWithRetryAsync(
                () => client.GetAsync(new Uri("api/test", UriKind.Relative)));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(3, handler.CallCount);
        }

        [Fact]
        public async Task SendWithRetryAsync_ThrowsHttpRequestException_WhenConnectionFailsOnAllRetries()
        {
            // Verbindungsfehler (kein StatusCode) auf allen Versuchen → Exception nach maxRetries
            HttpRequestException connectionError = new("Verbindung fehlgeschlagen", inner: null, statusCode: null);
            StubHttpMessageHandler handler = new();
            handler.EnqueueException(connectionError);
            handler.EnqueueException(connectionError);
            handler.EnqueueException(connectionError);
            HttpClient client = new(handler) { BaseAddress = new Uri("http://fake/") };

            _ = await Assert.ThrowsAsync<HttpRequestException>(
                () => SpotifyHttpRetry.SendWithRetryAsync(() => client.GetAsync(new Uri("api/test", UriKind.Relative))));

            Assert.Equal(3, handler.CallCount);
        }

        [Fact]
        public async Task SendWithRetryAsync_Retries_OnConnectionError_ThenSucceeds()
        {
            // Verbindungsfehler beim ersten Versuch, danach Erfolg
            HttpRequestException connectionError = new("Verbindung fehlgeschlagen", inner: null, statusCode: null);
            StubHttpMessageHandler handler = new();
            handler.EnqueueException(connectionError);
            handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK));
            HttpClient client = new(handler) { BaseAddress = new Uri("http://fake/") };

            HttpResponseMessage response = await SpotifyHttpRetry.SendWithRetryAsync(
                () => client.GetAsync(new Uri("api/test", UriKind.Relative)));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, handler.CallCount);
        }
    }
}

using System.Net;

namespace EchoPlay.Spotify.Tests.Http
{
    /// <summary>
    /// Stub-HTTP-Handler für Tests der Retry-Logik.
    ///
    /// Liefert vordefinierte Antworten oder wirft Exceptions in der Reihenfolge,
    /// in der sie eingereiht wurden. So können Transient-Fehler deterministisch
    /// simuliert werden, ohne echte Netzwerkaufrufe zu benötigen.
    /// </summary>
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        private int _callCount;

        /// <summary>Reiht eine HTTP-Antwort ein, die beim nächsten Aufruf zurückgegeben wird.</summary>
        /// <param name="response">Die zu liefernde Antwort.</param>
        internal void EnqueueResponse(HttpResponseMessage response) =>
            _responses.Enqueue(() => response);

        /// <summary>Reiht eine Exception ein, die beim nächsten Aufruf geworfen wird.</summary>
        /// <param name="exception">Die zu werfende Exception.</param>
        internal void EnqueueException(HttpRequestException exception) =>
            _responses.Enqueue(() => throw exception);

        /// <summary>Gibt die Anzahl der bisher empfangenen Anfragen zurück.</summary>
        internal int CallCount => _callCount;

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _callCount++;

            if (_responses.TryDequeue(out Func<HttpResponseMessage>? factory))
                return Task.FromResult(factory());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}

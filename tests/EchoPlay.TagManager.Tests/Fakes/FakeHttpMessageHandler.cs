using System.Net;

namespace EchoPlay.TagManager.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="HttpMessageHandler"/>, der HTTP-Anfragen ohne Netzwerk beantwortet.
    /// Wird für Tests von <c>MusicBrainzLookupService</c> verwendet, damit keine echten
    /// API-Anfragen an MusicBrainz gesendet werden.
    /// </summary>
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseContent;

        /// <summary>
        /// Initialisiert den Fake mit einer festen Antwort.
        /// </summary>
        /// <param name="responseContent">JSON-Inhalt der Antwort.</param>
        /// <param name="statusCode">HTTP-Statuscode der Antwort. Standard: 200 OK.</param>
        public FakeHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _statusCode = statusCode;
            _responseContent = responseContent;
        }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(_statusCode)
            {
                Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}

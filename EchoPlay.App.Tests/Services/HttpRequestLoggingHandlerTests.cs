using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="HttpRequestLoggingHandler"/>. Prüft Erfolgs- und
    /// Fehlerpfad, die Redaktion sensibler Query-Parameter sowie die
    /// Versuchszählung bei mehrfachen Aufrufen (Retry-Semantik).
    /// </summary>
    public sealed class HttpRequestLoggingHandlerTests
    {
        private const string ClientName = "TestClient";

        [Fact]
        public async Task SendAsync_Success_LogsRequestAndResponseAsInformation()
        {
            CapturingLogger logger = new();
            StubInnerHandler inner = new(HttpStatusCode.OK, "OK");
            using HttpClient client = BuildClient(logger, inner);

            HttpResponseMessage response = await client.GetAsync(
                new Uri("https://itunes.apple.com/search?term=TKKG&country=DE&entity=podcast"),
                CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            (string Level, string Message, Exception? _) requestEntry = FindEntry(logger, "HTTP GET");
            Assert.Equal("Info", requestEntry.Level);
            Assert.Contains("term=TKKG", requestEntry.Message, StringComparison.Ordinal);
            Assert.Contains("country=DE", requestEntry.Message, StringComparison.Ordinal);
            Assert.Contains($"Client: {ClientName}", requestEntry.Message, StringComparison.Ordinal);
            Assert.Contains("Attempt: 1", requestEntry.Message, StringComparison.Ordinal);

            (string Level, string Message, Exception? _) responseEntry = FindEntry(logger, "-> 200");
            Assert.Equal("Info", responseEntry.Level);
            Assert.Contains("in ", responseEntry.Message, StringComparison.Ordinal);
            Assert.Contains("ms", responseEntry.Message, StringComparison.Ordinal);
            Assert.Contains("Attempts: 1", responseEntry.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        public async Task SendAsync_ErrorStatus_LogsResponseAsWarning(HttpStatusCode status)
        {
            CapturingLogger logger = new();
            StubInnerHandler inner = new(status, status.ToString());
            using HttpClient client = BuildClient(logger, inner);

            HttpResponseMessage response = await client.GetAsync(
                new Uri("https://api.spotify.com/v1/search"),
                CancellationToken.None);

            Assert.Equal(status, response.StatusCode);

            string needle = $"-> {(int)status}";
            (string Level, string Message, Exception? _) responseEntry = FindEntry(logger, needle);
            Assert.Equal("Warning", responseEntry.Level);
            Assert.Contains($"Client: {ClientName}", responseEntry.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SendAsync_SensitiveQueryParameters_AreRedactedInLog()
        {
            CapturingLogger logger = new();
            StubInnerHandler inner = new(HttpStatusCode.OK, "OK");
            using HttpClient client = BuildClient(logger, inner);

            _ = await client.GetAsync(
                new Uri("https://example.com/api?term=TKKG&access_token=geheim123&api_key=abc&client_secret=xyz&country=DE"),
                CancellationToken.None);

            (string Level, string Message, Exception? _) requestEntry = FindEntry(logger, "HTTP GET");
            Assert.Contains("access_token=***", requestEntry.Message, StringComparison.Ordinal);
            Assert.Contains("api_key=***", requestEntry.Message, StringComparison.Ordinal);
            Assert.Contains("client_secret=***", requestEntry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("geheim123", requestEntry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("xyz", requestEntry.Message, StringComparison.Ordinal);
            // Nicht-sensible Parameter bleiben unverändert sichtbar.
            Assert.Contains("term=TKKG", requestEntry.Message, StringComparison.Ordinal);
            Assert.Contains("country=DE", requestEntry.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SendAsync_MultipleAttemptsOnSameRequest_IncrementAttemptCounter()
        {
            CapturingLogger logger = new();
            StubInnerHandler inner = new(HttpStatusCode.OK, "OK");
            HttpRequestLoggingHandler handler = new(new CapturingLoggerFactory(logger), ClientName)
            {
                InnerHandler = inner,
            };

            using HttpRequestMessage request = new(
                HttpMethod.Get,
                new Uri("https://example.com/retry"));
            using HttpMessageInvoker invoker = new(handler);

            HttpResponseMessage first = await invoker.SendAsync(request, CancellationToken.None);
            first.Dispose();
            HttpResponseMessage second = await invoker.SendAsync(request, CancellationToken.None);
            second.Dispose();
            HttpResponseMessage third = await invoker.SendAsync(request, CancellationToken.None);
            third.Dispose();

            // Drei Request-Zeilen mit Attempt: 1, 2, 3
            int[] attempts = new[] { 1, 2, 3 };
            foreach (int attempt in attempts)
            {
                Assert.Contains(logger.Entries, e =>
                    e.Level == "Info"
                    && e.Message.Contains("HTTP GET", StringComparison.Ordinal)
                    && e.Message.Contains($"Attempt: {attempt}", StringComparison.Ordinal));
            }

            // Drei Response-Zeilen mit Attempts: 1, 2, 3
            foreach (int attempt in attempts)
            {
                Assert.Contains(logger.Entries, e =>
                    e.Level == "Info"
                    && e.Message.Contains("-> 200", StringComparison.Ordinal)
                    && e.Message.Contains($"Attempts: {attempt}", StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task SendAsync_InnerException_IsLoggedAsWarningAndRethrown()
        {
            CapturingLogger logger = new();
            HttpRequestException expected = new("simulierter Netzwerkfehler");
            ThrowingInnerHandler inner = new(expected);
            using HttpClient client = BuildClient(logger, inner);

            HttpRequestException actual = await Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetAsync(new Uri("https://example.com/"), CancellationToken.None));

            Assert.Same(expected, actual);
            Assert.Contains(logger.Entries, e =>
                e.Level == "Warning"
                && e.Message.Contains("Fehler nach", StringComparison.Ordinal)
                && e.Message.Contains("HttpRequestException", StringComparison.Ordinal));
        }

        [Fact]
        public void RedactUrl_NullUri_ReturnsPlaceholder()
        {
            Assert.Equal("(unbekannte URL)", HttpRequestLoggingHandler.RedactUrl(null));
        }

        [Fact]
        public void RedactUrl_NoQuery_ReturnsPathOnly()
        {
            Uri uri = new("https://example.com/api/resource");
            Assert.Equal("https://example.com/api/resource", HttpRequestLoggingHandler.RedactUrl(uri));
        }

        [Fact]
        public void RedactUrl_MixedQuery_PreservesNonSensitiveAndMasksSecrets()
        {
            Uri uri = new("https://example.com/search?term=abc&token=geheim&country=DE&password=pwd");
            string redacted = HttpRequestLoggingHandler.RedactUrl(uri);

            Assert.Contains("term=abc", redacted, StringComparison.Ordinal);
            Assert.Contains("country=DE", redacted, StringComparison.Ordinal);
            Assert.Contains("token=***", redacted, StringComparison.Ordinal);
            Assert.Contains("password=***", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("geheim", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("pwd", redacted, StringComparison.Ordinal);
        }

        private static HttpClient BuildClient(CapturingLogger logger, HttpMessageHandler inner)
        {
            HttpRequestLoggingHandler handler = new(new CapturingLoggerFactory(logger), ClientName)
            {
                InnerHandler = inner,
            };
            // Test-Fixture: der DelegatingHandler selbst wird getestet, deshalb kein Umweg über IHttpClientFactory.
            return new HttpClient(handler, disposeHandler: true);
        }

        private static (string Level, string Message, Exception? Exception) FindEntry(CapturingLogger logger, string contains)
        {
            foreach ((string Level, string Message, Exception? Exception) entry in logger.Entries)
            {
                if (entry.Message.Contains(contains, StringComparison.Ordinal))
                {
                    return entry;
                }
            }
            throw new Xunit.Sdk.XunitException(
                $"Kein Log-Eintrag enthält '{contains}'. Vorhandene Einträge: "
                + string.Join(" | ", FormatEntries(logger.Entries)));
        }

        private static IEnumerable<string> FormatEntries(IEnumerable<(string Level, string Message, Exception? Exception)> entries)
        {
            foreach ((string Level, string Message, Exception? _) entry in entries)
            {
                yield return $"[{entry.Level}] {entry.Message}";
            }
        }

        private sealed class StubInnerHandler(HttpStatusCode status, string reason) : HttpMessageHandler
        {
            private readonly HttpStatusCode _status = status;
            private readonly string _reason = reason;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpResponseMessage response = new(_status)
                {
                    ReasonPhrase = _reason,
                    RequestMessage = request,
                };
                return Task.FromResult(response);
            }
        }

        private sealed class ThrowingInnerHandler(Exception exception) : HttpMessageHandler
        {
            private readonly Exception _exception = exception;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromException<HttpResponseMessage>(_exception);
            }
        }
    }
}

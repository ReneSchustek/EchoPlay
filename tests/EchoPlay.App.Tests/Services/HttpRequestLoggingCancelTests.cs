using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft, was die Anfrage-Protokollierung bei einem Abbruch schreibt — und wie sie mit
    /// Adressen umgeht, deren Abfrageteil kein gewöhnliches Schlüssel-Wert-Paar enthält.
    /// </summary>
    /// <remarks>
    /// Ein Abbruch ist kein Fehlschlag: Der Anwender hat die Seite verlassen oder eine neue
    /// Suche begonnen. Stünde er als Warnung im Protokoll, wäre jede Sitzung voller
    /// Warnungen und die echten gingen darin unter.
    ///
    /// Eigene Datei statt Ergänzung von <c>HttpRequestLoggingHandlerTests</c>: Die liegt
    /// bereits über der Grenze aus <c>testing.md</c>.
    /// </remarks>
    public sealed class HttpRequestLoggingCancelTests
    {
        private const string ClientName = "TestClient";

        [Fact]
        public async Task SendAsync_WhenCancelled_LogsItAsInformationNotAsWarning()
        {
            CapturingLogger logger = new();
            using HttpClient client = BuildClient(logger, new CancellingInnerHandler());

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.GetAsync(new Uri("https://itunes.apple.com/search?term=TKKG"), CancellationToken.None));

            (string Level, string Message, Exception? Exception) entry = FindEntry(logger, "Abgebrochen");
            Assert.Equal("Info", entry.Level);
            Assert.Contains($"Client: {ClientName}", entry.Message, StringComparison.Ordinal);
            Assert.Contains("Attempts: 1", entry.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SendAsync_WithAValuelessQueryPart_KeepsItInTheLine()
        {
            CapturingLogger logger = new();
            using HttpClient client = BuildClient(
                logger, new StubOkHandler());

            _ = await client.GetAsync(
                new Uri("https://example.invalid/suche?debug&term=TKKG"), CancellationToken.None);

            // Ein Schalter ohne Wert ist eine gültige Adresse. Fiele er beim Redigieren
            // heraus, zeigte das Protokoll eine Anfrage, die so nie gestellt wurde.
            (string Level, string Message, Exception? Exception) entry = FindEntry(logger, "HTTP GET");
            Assert.Contains("debug", entry.Message, StringComparison.Ordinal);
            Assert.Contains("term=TKKG", entry.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SendAsync_WithAnEmptyQueryPart_DoesNotProduceADoubleSeparator()
        {
            CapturingLogger logger = new();
            using HttpClient client = BuildClient(logger, new StubOkHandler());

            _ = await client.GetAsync(
                new Uri("https://example.invalid/suche?&term=TKKG"), CancellationToken.None);

            (string Level, string Message, Exception? Exception) entry = FindEntry(logger, "HTTP GET");
            Assert.DoesNotContain("?&", entry.Message, StringComparison.Ordinal);
        }

        private static HttpClient BuildClient(CapturingLogger logger, HttpMessageHandler inner)
        {
            HttpRequestLoggingHandler handler = new(new CapturingLoggerFactory(logger), ClientName)
            {
                InnerHandler = inner,
            };

            // Test-Fixture — hier ist der DelegatingHandler selbst das Prüfobjekt.
            return new HttpClient(handler, disposeHandler: true);
        }

        private static (string Level, string Message, Exception? Exception) FindEntry(
            CapturingLogger logger, string contains)
        {
            return logger.Entries.First(e => e.Message.Contains(contains, StringComparison.Ordinal));
        }

        private sealed class CancellingInnerHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromException<HttpResponseMessage>(new OperationCanceledException());
        }

        private sealed class StubOkHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    RequestMessage = request,
                });
        }
    }
}

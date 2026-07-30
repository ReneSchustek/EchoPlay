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
    /// Tests für <see cref="CoverDownloader"/>. Der Dienst hat die vier vorher getrennt
    /// implementierten Download-Wrapper ersetzt (Arbeitspaket 451); geprüft wird deshalb die
    /// Normalisierung, auf die sich alle Aufrufer verlassen.
    /// </summary>
    public sealed class CoverDownloaderTests
    {
        private static CoverDownloader Build(HttpMessageHandler handler) =>
            new(new StubHttpClientFactory(handler), new FakeLoggerFactory());

        [Fact]
        public async Task DownloadAsync_Erfolg_LiefertBytes()
        {
            byte[] payload = [0x01, 0x02, 0x03];
            CoverDownloader downloader = Build(new StubHandler(HttpStatusCode.OK, payload));

            byte[]? result = await downloader.DownloadAsync(
                "https://example.invalid/cover.jpg", TestContext.Current.CancellationToken);

            Assert.Equal(payload, result);
        }

        [Fact]
        public async Task DownloadAsync_LeereAntwort_LiefertNull()
        {
            // 0 Bytes dürfen nicht in CoverImages landen: der Platzhalter greift dann nicht
            // mehr, und die Nachlade-Läufe halten den Eintrag für erledigt. Zwei der vier
            // alten Kopien haben das leere Ergebnis gespeichert.
            CoverDownloader downloader = Build(new StubHandler(HttpStatusCode.OK, []));

            byte[]? result = await downloader.DownloadAsync(
                "https://example.invalid/leer.jpg", TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task DownloadAsync_HttpFehler_LiefertNull()
        {
            CoverDownloader downloader = Build(new StubHandler(HttpStatusCode.NotFound, []));

            byte[]? result = await downloader.DownloadAsync(
                "https://example.invalid/fehlt.jpg", TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task DownloadAsync_Zeitüberschreitung_LiefertNull()
        {
            // HttpClient meldet eine Zeitüberschreitung als TaskCanceledException, obwohl
            // niemand abgebrochen hat. Sie darf nicht als Abbruch durchgeworfen werden —
            // sonst reißt ein langsamer Anbieter den ganzen Hintergrundlauf ab.
            CoverDownloader downloader = Build(new ThrowingHandler(
                new TaskCanceledException("timeout", new TimeoutException())));

            byte[]? result = await downloader.DownloadAsync(
                "https://example.invalid/langsam.jpg", TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task DownloadAsync_Abbruch_WirftWeiter()
        {
            // Gegenstück zum Test darüber: ein echter Abbruch ist kein fehlgeschlagener
            // Download und muss die Schleife des Aufrufers beenden.
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            CoverDownloader downloader = Build(new StubHandler(HttpStatusCode.OK, [0x01]));

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => downloader.DownloadAsync("https://example.invalid/cover.jpg", cts.Token));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("kein-uri")]
        [InlineData("/relativ/cover.jpg")]
        public async Task DownloadAsync_UnbrauchbareUrl_LiefertNullOhneAnfrage(string eingabe)
        {
            CountingHandler handler = new();
            CoverDownloader downloader = Build(handler);

            byte[]? result = await downloader.DownloadAsync(eingabe, TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Equal(0, handler.CallCount);
        }

        // ── Stubs ──────────────────────────────────────────────────────────────

        private sealed class StubHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;

            public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly byte[] _payload;

            public StubHandler(HttpStatusCode status, byte[] payload)
            {
                _status = status;
                _payload = payload;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new ByteArrayContent(_payload)
                });
            }
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            private readonly Exception _exception;

            public ThrowingHandler(Exception exception) => _exception = exception;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromException<HttpResponseMessage>(_exception);
        }

        private sealed class CountingHandler : HttpMessageHandler
        {
            public int CallCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([0x01])
                });
            }
        }
    }
}

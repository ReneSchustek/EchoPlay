using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Verifiziert die Sicherheits-Vorprüfungen des Update-Downloads:
    /// Versions-Whitelist gegen Path-Traversal und ContentLength-Vergleich
    /// gegen das im Release-Asset gemeldete Größenlimit.
    /// </summary>
    public sealed class UpdateDownloadServiceTests
    {
        [Theory]
        [InlineData("../../bad")]
        [InlineData("..\\bad")]
        [InlineData("1.2/3")]
        [InlineData("v1.2.3")]
        [InlineData("1.2.3-beta")]
        [InlineData("")]
        public async Task DownloadAndInstallAsync_InvalidVersionFormat_ReturnsFalseWithoutDownload(string version)
        {
            (UpdateDownloadService service, RecordingHandler handler) = BuildService();

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: "https://example.org/setup.exe",
                version: version,
                expectedFileSize: 100);

            Assert.False(result);
            Assert.Equal(0, handler.RequestCount);
        }

        [Theory]
        [InlineData("1")]
        [InlineData("1.2")]
        [InlineData("1.2.3")]
        [InlineData("1.2.3.4")]
        public async Task DownloadAndInstallAsync_ValidVersion_ProceedsToHttpRequest(string version)
        {
            // ContentLength bewusst falsch, damit der Test nicht versucht, einen echten
            // Process.Start auszulösen — wir prüfen nur, dass die Whitelist passieren wurde.
            (UpdateDownloadService service, RecordingHandler handler) = BuildService(
                payload: new byte[10],
                contentLength: 10);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: "https://example.org/setup.exe",
                version: version,
                expectedFileSize: 999_999); // Mismatch erzwingt Abbruch nach Download

            Assert.False(result);
            Assert.Equal(1, handler.RequestCount);
        }

        [Fact]
        public async Task DownloadAndInstallAsync_ContentLengthMismatch_DeletesFileAndReturnsFalse()
        {
            byte[] payload = new byte[5];
            (UpdateDownloadService service, RecordingHandler handler) = BuildService(payload, contentLength: 5);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: "https://example.org/setup.exe",
                version: "1.0.0",
                expectedFileSize: 999); // Erwartet 999 Bytes, geliefert wurden 5

            Assert.False(result);
            Assert.Equal(1, handler.RequestCount);

            // Datei muss nach Mismatch entfernt sein, damit kein veralteter Setup im TEMP liegen bleibt.
            string expectedTempPath = Path.Combine(Path.GetTempPath(), "EchoPlay-Setup-1.0.0.exe");
            Assert.False(File.Exists(expectedTempPath));
        }

        [Fact]
        public async Task DownloadAndInstallAsync_HttpFailure_ReturnsFalse()
        {
            (UpdateDownloadService service, _) = BuildService(
                statusCode: HttpStatusCode.InternalServerError);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: "https://example.org/setup.exe",
                version: "1.0.0",
                expectedFileSize: 0);

            Assert.False(result);
        }

        [Fact]
        public async Task DownloadAndInstallAsync_CancellationToken_PropagatesCancel()
        {
            (UpdateDownloadService service, _) = BuildService(payload: new byte[1], contentLength: 1);
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            // Cancellation wird intern als Exception abgefangen und auf false gemappt — das ist
            // dokumentiertes Verhalten (Brief: "Nutzer kann es beim nächsten Start erneut versuchen").
            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: "https://example.org/setup.exe",
                version: "1.0.0",
                expectedFileSize: 1,
                cancellationToken: cts.Token);

            Assert.False(result);
        }

        // ── Test-Helfer ──────────────────────────────────────────────────────────

        private static (UpdateDownloadService Service, RecordingHandler Handler) BuildService(
            byte[]? payload = null,
            long contentLength = 0,
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            RecordingHandler handler = new(payload ?? [], contentLength, statusCode);

            ServiceCollection services = new();
            _ = services.AddSingleton<IHttpClientFactory>(new SingleHandlerFactory(handler));

            ServiceProvider provider = services.BuildServiceProvider();
            UpdateDownloadService service = new(
                provider.GetRequiredService<IHttpClientFactory>(),
                new FakeLoggerFactory());

            return (service, handler);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly byte[] _payload;
            private readonly long _contentLength;
            private readonly HttpStatusCode _statusCode;

            public int RequestCount { get; private set; }

            public RecordingHandler(byte[] payload, long contentLength, HttpStatusCode statusCode)
            {
                _payload = payload;
                _contentLength = contentLength;
                _statusCode = statusCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                HttpResponseMessage response = new(_statusCode)
                {
                    Content = new ByteArrayContent(_payload)
                };
                if (_contentLength > 0)
                {
                    response.Content.Headers.ContentLength = _contentLength;
                }
                return Task.FromResult(response);
            }
        }

        private sealed class SingleHandlerFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;
            public SingleHandlerFactory(HttpMessageHandler handler) => _handler = handler;
            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }
    }
}

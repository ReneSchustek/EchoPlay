using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
    /// Versions-Whitelist gegen Path-Traversal, ContentLength-Vergleich
    /// gegen das im Release-Asset gemeldete Größenlimit und SHA-256-Hash-Pin
    /// gegen Inhalts-Manipulation.
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
                expectedFileSize: 100,
                expectedSha256: string.Empty, cancellationToken: TestContext.Current.CancellationToken);

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
                expectedFileSize: 999_999, // Mismatch erzwingt Abbruch nach Download
                expectedSha256: string.Empty, cancellationToken: TestContext.Current.CancellationToken);

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
                expectedFileSize: 999, // Erwartet 999 Bytes, geliefert wurden 5
                expectedSha256: string.Empty, cancellationToken: TestContext.Current.CancellationToken);

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
                expectedFileSize: 0,
                expectedSha256: string.Empty, cancellationToken: TestContext.Current.CancellationToken);

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
                expectedSha256: string.Empty,
                cancellationToken: cts.Token);

            Assert.False(result);
        }

        [Fact]
        public async Task DownloadAndInstallAsync_HashMismatch_DeletesFileAndReturnsFalse()
        {
            // Payload-Hash ist für die korrekte Datei berechenbar; wir reichen aber einen
            // erwarteten Hash durch, der definitiv abweicht (alle Nullen).
            byte[] payload = [1, 2, 3, 4, 5];
            string version = "1.0.1"; // eigene Version, damit andere Tests die Datei nicht stören
            (UpdateDownloadService service, RecordingHandler handler) = BuildService(payload, contentLength: payload.Length);

            string wrongHash = new('0', 64);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: "https://example.org/setup.exe",
                version: version,
                expectedFileSize: payload.Length,
                expectedSha256: wrongHash, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(1, handler.RequestCount);

            string expectedTempPath = Path.Combine(Path.GetTempPath(), $"EchoPlay-Setup-{version}.exe");
            Assert.False(File.Exists(expectedTempPath));
        }

        [Fact]
        public async Task DownloadAndInstallAsync_InvalidHexHash_DeletesFileAndReturnsFalse()
        {
            byte[] payload = [9, 9, 9];
            string version = "1.0.2";
            (UpdateDownloadService service, _) = BuildService(payload, contentLength: payload.Length);

            // 64 Zeichen, aber 'z' ist kein Hex — Convert.FromHexString muss werfen.
            string invalidHash = new('z', 64);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: "https://example.org/setup.exe",
                version: version,
                expectedFileSize: payload.Length,
                expectedSha256: invalidHash, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);

            string expectedTempPath = Path.Combine(Path.GetTempPath(), $"EchoPlay-Setup-{version}.exe");
            Assert.False(File.Exists(expectedTempPath));
        }

        [Fact]
        public async Task DownloadAndInstallAsync_HashWrongLength_DeletesFileAndReturnsFalse()
        {
            byte[] payload = [42];
            string version = "1.0.3";
            (UpdateDownloadService service, _) = BuildService(payload, contentLength: payload.Length);

            // 32 Hex-Zeichen = 16 Bytes — gültiges Hex, aber falsche Länge für SHA-256 (32 Bytes).
            string shortHash = new('a', 32);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: "https://example.org/setup.exe",
                version: version,
                expectedFileSize: payload.Length,
                expectedSha256: shortHash, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);

            string expectedTempPath = Path.Combine(Path.GetTempPath(), $"EchoPlay-Setup-{version}.exe");
            Assert.False(File.Exists(expectedTempPath));
        }

        [Fact]
        public async Task DownloadAndInstallAsync_HashMatch_PassesHashCheck()
        {
            // Sauberer Hash-Match-Pfad: Hash matcht, ContentLength matcht. Der Service kommt
            // bis zum Process.Start mit der heruntergeladenen Datei. Echtes Setup ist es nicht,
            // also wirft Process.Start eine Win32Exception, die im Service-eigenen catch-Block
            // auf false gemappt wird. Der Test prüft daher indirekt: der Hash-Check hat
            // **nicht** abgebrochen (sonst wäre die TryDelete-Phase aktiv geworden und der
            // Pfad zu Process.Start nie erreicht). Das wäre nicht testbar ohne Hash-Match.
            byte[] payload = [1, 2, 3, 4, 5];
            string version = "1.0.4";
            string correctHash = Convert.ToHexString(SHA256.HashData(payload));
            (UpdateDownloadService service, _) = BuildService(payload, contentLength: payload.Length);

            // Wir verifizieren nur, dass kein Throw nach außen dringt — ein expliziter
            // „passed hash check"-Returnwert ist im aktuellen API-Vertrag nicht vorgesehen.
            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: "https://example.org/setup.exe",
                version: version,
                expectedFileSize: payload.Length,
                expectedSha256: correctHash, cancellationToken: TestContext.Current.CancellationToken);

            // Process.Start mit Dummy-payload schlägt fehl → false. Aber der Pfad bis dorthin
            // ist nur erreichbar, wenn ContentLength- UND Hash-Check beide grün waren.
            Assert.False(result);

            // Die Datei wurde im Hash-Match-Pfad NICHT gelöscht (TryDelete läuft erst bei
            // Mismatch). Sie kann zwar durch den fehlgeschlagenen Process.Start verändert
            // sein, aber existieren tut sie noch — das entspricht der dokumentierten
            // Aufräum-Semantik (Cleanup nur bei Mismatch).
            string tempPath = Path.Combine(Path.GetTempPath(), $"EchoPlay-Setup-{version}.exe");
            try
            {
                Assert.True(File.Exists(tempPath));
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
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

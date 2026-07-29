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
    /// Versions-Whitelist gegen Path-Traversal, HTTPS- und Host-Bindung der
    /// Download-URL, ContentLength-Vergleich gegen das im Release-Asset gemeldete
    /// Größenlimit und den SHA-256-Hash-Pin gegen Inhalts-Manipulation.
    /// </summary>
    public sealed class UpdateDownloadServiceTests
    {
        private const string ValidUrl = "https://github.com/ReneSchustek/EchoPlay/releases/download/v1.0.0/EchoPlay-Setup.exe";

        // Wohlgeformter Hash (64 Hex-Zeichen), der zu keiner realen Nutzlast passt.
        // Für Tests, die vor dem Hash-Vergleich abbrechen sollen.
        private static readonly string WellFormedHash = new('0', 64);

        [Theory]
        [InlineData("../../bad")]
        [InlineData("..\\bad")]
        [InlineData("1.2/3")]
        [InlineData("v1.2.3")]
        [InlineData("1.2.3-beta")]
        [InlineData("")]
        public async Task DownloadAndInstallAsync_InvalidVersionFormat_ReturnsFalseWithoutDownload(string version)
        {
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService();

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: ValidUrl,
                version: version,
                expectedFileSize: 100,
                expectedSha256: WellFormedHash, cancellationToken: TestContext.Current.CancellationToken);

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
            // ContentLength bewusst falsch, damit der Ablauf nach dem Download abbricht —
            // wir prüfen nur, dass die Whitelist passiert wurde.
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService(
                payload: new byte[10],
                contentLength: 10);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: ValidUrl,
                version: version,
                expectedFileSize: 999_999, // Mismatch erzwingt Abbruch nach Download
                expectedSha256: WellFormedHash, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(1, handler.RequestCount);
        }

        // ── URL-Bindung ──────────────────────────────────────────────────────────

        [Theory]
        [InlineData("http://github.com/ReneSchustek/EchoPlay/releases/download/v1/setup.exe")] // kein TLS
        [InlineData("file:///C:/Windows/System32/calc.exe")]                                   // lokales Programm
        [InlineData("ftp://github.com/setup.exe")]                                             // fremdes Protokoll
        [InlineData("/releases/download/v1/setup.exe")]                                        // nicht absolut
        [InlineData("")]
        public async Task DownloadAndInstallAsync_UrlIsNotHttps_ReturnsFalseWithoutDownload(string candidate)
        {
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService();

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: candidate,
                version: "1.0.0",
                expectedFileSize: 0,
                expectedSha256: WellFormedHash, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(0, handler.RequestCount);
        }

        [Theory]
        [InlineData("https://example.org/setup.exe")]
        [InlineData("https://github.com.angreifer.example/setup.exe")] // Host als Präfix getarnt
        [InlineData("https://evil-github.com/setup.exe")]
        [InlineData("https://githubusercontent.com/setup.exe")]
        public async Task DownloadAndInstallAsync_UrlPointsToForeignHost_ReturnsFalseWithoutDownload(string candidate)
        {
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService();

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: candidate,
                version: "1.0.0",
                expectedFileSize: 0,
                expectedSha256: WellFormedHash, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(0, handler.RequestCount);
        }

        [Theory]
        [InlineData("https://github.com/ReneSchustek/EchoPlay/releases/download/v1/setup.exe")]
        [InlineData("https://objects.githubusercontent.com/release-assets/setup.exe")]
        [InlineData("https://release-assets.githubusercontent.com/releases/setup.exe")]
        [InlineData("https://GitHub.com/ReneSchustek/EchoPlay/releases/download/v1/setup.exe")] // Groß-/Kleinschreibung egal
        public async Task DownloadAndInstallAsync_UrlPointsToReleaseHost_ProceedsToHttpRequest(string candidate)
        {
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService(
                payload: new byte[4],
                contentLength: 4);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: candidate,
                version: "1.0.0",
                expectedFileSize: 999, // Mismatch bricht nach dem Download ab
                expectedSha256: WellFormedHash, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(1, handler.RequestCount);
        }

        // ── Hash-Pin ─────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task DownloadAndInstallAsync_NoHashInReleaseBody_ReturnsFalseWithoutDownload(string missingHash)
        {
            // Ohne Hash gibt es keine Integritätsprüfung — und ohne Integritätsprüfung
            // keine Installation. Sonst genügte es, die SHA-Zeile aus dem Release-Body
            // zu streichen, um den Schutz auszuhebeln.
            (UpdateDownloadService service, RecordingHandler handler, FakeInstallerLauncher launcher) = BuildService(
                payload: [1, 2, 3],
                contentLength: 3);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: ValidUrl,
                version: "1.0.0",
                expectedFileSize: 3,
                expectedSha256: missingHash, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(0, handler.RequestCount);
            Assert.Empty(launcher.StartedPaths);
        }

        [Fact]
        public async Task DownloadAndInstallAsync_ContentLengthMismatch_DeletesFileAndReturnsFalse()
        {
            byte[] payload = new byte[5];
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService(payload, contentLength: 5);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: ValidUrl,
                version: "1.0.0",
                expectedFileSize: 999, // Erwartet 999 Bytes, geliefert wurden 5
                expectedSha256: WellFormedHash, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(1, handler.RequestCount);

            // Datei muss nach Mismatch entfernt sein, damit kein veralteter Setup im TEMP liegen bleibt.
            string expectedTempPath = Path.Combine(Path.GetTempPath(), "EchoPlay-Setup-1.0.0.exe");
            Assert.False(File.Exists(expectedTempPath));
        }

        [Fact]
        public async Task DownloadAndInstallAsync_HttpFailure_ReturnsFalse()
        {
            (UpdateDownloadService service, _, _) = BuildService(
                statusCode: HttpStatusCode.InternalServerError);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: ValidUrl,
                version: "1.0.0",
                expectedFileSize: 0,
                expectedSha256: WellFormedHash, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
        }

        [Fact]
        public async Task DownloadAndInstallAsync_CancellationToken_PropagatesCancel()
        {
            (UpdateDownloadService service, _, _) = BuildService(payload: new byte[1], contentLength: 1);
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            // Cancellation wird intern als Exception abgefangen und auf false gemappt — das ist
            // dokumentiertes Verhalten (Arbeitspaket: "Nutzer kann es beim nächsten Start erneut versuchen").
            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: ValidUrl,
                version: "1.0.0",
                expectedFileSize: 1,
                expectedSha256: WellFormedHash,
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
            (UpdateDownloadService service, RecordingHandler handler, FakeInstallerLauncher launcher) =
                BuildService(payload, contentLength: payload.Length);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: ValidUrl,
                version: version,
                expectedFileSize: payload.Length,
                expectedSha256: WellFormedHash, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(1, handler.RequestCount);
            Assert.Empty(launcher.StartedPaths);

            string expectedTempPath = Path.Combine(Path.GetTempPath(), $"EchoPlay-Setup-{version}.exe");
            Assert.False(File.Exists(expectedTempPath));
        }

        [Fact]
        public async Task DownloadAndInstallAsync_InvalidHexHash_ReturnsFalseWithoutDownload()
        {
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService(
                payload: [9, 9, 9],
                contentLength: 3);

            // 64 Zeichen, aber 'z' ist kein Hex — Convert.FromHexString muss werfen.
            string invalidHash = new('z', 64);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: ValidUrl,
                version: "1.0.2",
                expectedFileSize: 3,
                expectedSha256: invalidHash, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(0, handler.RequestCount);
        }

        [Fact]
        public async Task DownloadAndInstallAsync_HashWrongLength_ReturnsFalseWithoutDownload()
        {
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService(
                payload: [42],
                contentLength: 1);

            // 32 Hex-Zeichen = 16 Bytes — gültiges Hex, aber falsche Länge für SHA-256 (32 Bytes).
            string shortHash = new('a', 32);

            bool result = await service.DownloadAndInstallAsync(
                downloadUrl: ValidUrl,
                version: "1.0.3",
                expectedFileSize: 1,
                expectedSha256: shortHash, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(0, handler.RequestCount);
        }

        [Fact]
        public async Task DownloadAndInstallAsync_HashMatch_StartsInstallerWithDownloadedFile()
        {
            byte[] payload = [1, 2, 3, 4, 5];
            string version = "1.0.4";
            string correctHash = Convert.ToHexString(SHA256.HashData(payload));
            (UpdateDownloadService service, _, FakeInstallerLauncher launcher) =
                BuildService(payload, contentLength: payload.Length);

            string tempPath = Path.Combine(Path.GetTempPath(), $"EchoPlay-Setup-{version}.exe");
            try
            {
                bool result = await service.DownloadAndInstallAsync(
                    downloadUrl: ValidUrl,
                    version: version,
                    expectedFileSize: payload.Length,
                    expectedSha256: correctHash, cancellationToken: TestContext.Current.CancellationToken);

                Assert.True(result);
                Assert.Equal(tempPath, Assert.Single(launcher.StartedPaths));

                // Im Erfolgsfall bleibt die Datei liegen — der Installer braucht sie noch.
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

        [Fact]
        public async Task DownloadAndInstallAsync_InstallerStartFails_ReturnsFalse()
        {
            byte[] payload = [7, 7, 7];
            string version = "1.0.5";
            string correctHash = Convert.ToHexString(SHA256.HashData(payload));
            (UpdateDownloadService service, _, FakeInstallerLauncher launcher) =
                BuildService(payload, contentLength: payload.Length, installerStartSucceeds: false);

            string tempPath = Path.Combine(Path.GetTempPath(), $"EchoPlay-Setup-{version}.exe");
            try
            {
                bool result = await service.DownloadAndInstallAsync(
                    downloadUrl: ValidUrl,
                    version: version,
                    expectedFileSize: payload.Length,
                    expectedSha256: correctHash, cancellationToken: TestContext.Current.CancellationToken);

                Assert.False(result);
                _ = Assert.Single(launcher.StartedPaths);
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

        private static (UpdateDownloadService Service, RecordingHandler Handler, FakeInstallerLauncher Launcher) BuildService(
            byte[]? payload = null,
            long contentLength = 0,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            bool installerStartSucceeds = true)
        {
            RecordingHandler handler = new(payload ?? [], contentLength, statusCode);
            FakeInstallerLauncher launcher = new(installerStartSucceeds);

            ServiceCollection services = new();
            _ = services.AddSingleton<IHttpClientFactory>(new SingleHandlerFactory(handler));

            ServiceProvider provider = services.BuildServiceProvider();
            UpdateDownloadService service = new(
                provider.GetRequiredService<IHttpClientFactory>(),
                launcher,
                new FakeLoggerFactory());

            return (service, handler, launcher);
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

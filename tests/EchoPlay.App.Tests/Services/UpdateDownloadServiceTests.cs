using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Models;
using EchoPlay.Core.Security;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Verifiziert die Sicherheits-Vorprüfungen des Update-Downloads:
    /// Versions-Whitelist gegen Path-Traversal, HTTPS- und Host-Bindung beider Adressen,
    /// Größenvergleich, SHA-256-Hash-Pin gegen Inhalts-Manipulation und die Signatur gegen ein
    /// ausgetauschtes Release.
    /// </summary>
    public sealed class UpdateDownloadServiceTests : IDisposable
    {
        private static readonly Uri ValidUrl = new("https://github.com/ReneSchustek/EchoPlay/releases/download/v1.0.0/EchoPlay-Setup.exe");
        private static readonly Uri ValidSignatureUrl = new("https://github.com/ReneSchustek/EchoPlay/releases/download/v1.0.0/EchoPlay-Setup.exe.sig");

        // Wohlgeformter Hash (64 Hex-Zeichen), der zu keiner realen Nutzlast passt.
        // Für Tests, die vor dem Hash-Vergleich abbrechen sollen.
        private static readonly string WellFormedHash = new('0', 64);

        // Der Schlüssel des „Herausgebers“ in diesen Tests; der eingebettete echte hat seinen
        // privaten Teil nicht im Repository.
        private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public void Dispose() => _signingKey.Dispose();

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
                Offer(version, expectedFileSize: 100), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(0, handler.TotalRequests);
        }

        [Theory]
        [InlineData("1")]
        [InlineData("1.2")]
        [InlineData("1.2.3")]
        [InlineData("1.2.3.4")]
        public async Task DownloadAndInstallAsync_ValidVersion_ProceedsToHttpRequest(string version)
        {
            // Die erwartete Dateigröße passt nicht, damit der Ablauf nach dem Download abbricht.
            // Geprüft wird nur, dass die Versionsprüfung durchgelassen hat.
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService(payload: new byte[10], contentLength: 10);

            bool result = await service.DownloadAndInstallAsync(
                Offer(version, expectedFileSize: 999_999), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(1, handler.SetupRequests);
        }

        [Theory]
        [InlineData("http://github.com/ReneSchustek/EchoPlay/releases/download/v1/setup.exe")] // kein TLS
        [InlineData("file:///C:/Windows/System32/calc.exe")]                                   // lokales Programm
        [InlineData("ftp://github.com/setup.exe")]                                             // fremdes Protokoll
        public async Task DownloadAndInstallAsync_UrlIsNotHttps_ReturnsFalseWithoutDownload(string candidate)
        {
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService();

            bool result = await service.DownloadAndInstallAsync(
                Offer("1.0.0", downloadUrl: new Uri(candidate)), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(0, handler.TotalRequests);
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
                Offer("1.0.0", downloadUrl: new Uri(candidate)), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(0, handler.TotalRequests);
        }

        [Theory]
        [InlineData("http://github.com/ReneSchustek/EchoPlay/releases/download/v1/setup.exe.sig")]
        [InlineData("https://example.org/setup.exe.sig")]
        [InlineData("https://github.com.angreifer.example/setup.exe.sig")]
        public async Task DownloadAndInstallAsync_SignatureUrlNotTrusted_ReturnsFalseWithoutDownload(string candidate)
        {
            // Die Signatur kommt aus derselben fremden Antwort wie die Setup-Datei. Ein Angreifer,
            // der sie von einem eigenen Server liefern lässt, hätte sonst wenigstens erfahren, wer
            // gerade aktualisiert.
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService();

            bool result = await service.DownloadAndInstallAsync(
                Offer("1.0.0", signatureUrl: new Uri(candidate)), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(0, handler.TotalRequests);
        }

        [Theory]
        [InlineData("https://github.com/ReneSchustek/EchoPlay/releases/download/v1/setup.exe")]
        [InlineData("https://objects.githubusercontent.com/release-assets/setup.exe")]
        [InlineData("https://release-assets.githubusercontent.com/releases/setup.exe")]
        [InlineData("https://GitHub.com/ReneSchustek/EchoPlay/releases/download/v1/setup.exe")] // Groß-/Kleinschreibung egal
        public async Task DownloadAndInstallAsync_UrlPointsToReleaseHost_ProceedsToHttpRequest(string candidate)
        {
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService(payload: new byte[4], contentLength: 4);

            bool result = await service.DownloadAndInstallAsync(
                Offer("1.0.0", downloadUrl: new Uri(candidate), expectedFileSize: 999), // Mismatch bricht nach dem Download ab
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(1, handler.SetupRequests);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task DownloadAndInstallAsync_NoHashInReleaseBody_ReturnsFalseWithoutDownload(string missingHash)
        {
            // Ohne Hash keine Installation, auch nicht mit Signatur: Ein Release ohne Hash-Zeile
            // zeigt einen Fehler beim Veröffentlichen an, den man sehen will.
            (UpdateDownloadService service, RecordingHandler handler, FakeInstallerLauncher launcher) = BuildService(payload: [1, 2, 3], contentLength: 3);

            bool result = await service.DownloadAndInstallAsync(
                Offer("1.0.0", expectedFileSize: 3, expectedSha256: missingHash), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(0, handler.TotalRequests);
            Assert.Empty(launcher.StartedPaths);
        }

        [Fact]
        public async Task DownloadAndInstallAsync_ContentLengthMismatch_DeletesFileAndReturnsFalse()
        {
            byte[] payload = new byte[5];
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService(payload, contentLength: 5);

            bool result = await service.DownloadAndInstallAsync(
                Offer("1.0.0", expectedFileSize: 999), // Erwartet 999 Bytes, geliefert wurden 5
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(1, handler.SetupRequests);

            // Datei muss nach Mismatch entfernt sein, damit kein veralteter Setup im TEMP liegen bleibt.
            Assert.False(File.Exists(TempSetupPath("1.0.0")));
        }

        [Fact]
        public async Task DownloadAndInstallAsync_HttpFailure_ReturnsFalse()
        {
            (UpdateDownloadService service, _, _) = BuildService(statusCode: HttpStatusCode.InternalServerError);

            bool result = await service.DownloadAndInstallAsync(
                Offer("1.0.0"), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
        }

        [Fact]
        public async Task DownloadAndInstallAsync_CancellationToken_PropagatesCancel()
        {
            (UpdateDownloadService service, _, _) = BuildService(payload: new byte[1], contentLength: 1);
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            // Ein Abbruch des Aufrufers ist kein gescheiterter Download und kommt als Ausnahme an;
            // sonst hielte der Aufrufer ihn für einen Fehler und böte einen neuen Versuch an.
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAndInstallAsync(
                Offer("1.0.0", expectedFileSize: 1), cancellationToken: cts.Token));
        }

        [Fact]
        public async Task DownloadAndInstallAsync_HashMismatch_DeletesFileAndReturnsFalse()
        {
            // Der Hash der Nutzdaten wäre berechenbar; erwartet wird aber einer, der sicher
            // abweicht (alle Nullen). Die Signatur ist gültig, rettet die Datei aber nicht.
            byte[] payload = [1, 2, 3, 4, 5];
            string version = "1.0.1"; // eigene Version, damit andere Tests die Datei nicht stören
            (UpdateDownloadService service, RecordingHandler handler, FakeInstallerLauncher launcher) = BuildService(payload, contentLength: payload.Length);

            bool result = await service.DownloadAndInstallAsync(
                Offer(version, expectedFileSize: payload.Length), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(1, handler.SetupRequests);
            Assert.Empty(launcher.StartedPaths);
            Assert.False(File.Exists(TempSetupPath(version)));
        }

        [Fact]
        public async Task DownloadAndInstallAsync_InvalidHexHash_ReturnsFalseWithoutDownload()
        {
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService(payload: [9, 9, 9], contentLength: 3);

            // 64 Zeichen, aber 'z' ist kein Hex, also muss Convert.FromHexString werfen.
            bool result = await service.DownloadAndInstallAsync(
                Offer("1.0.2", expectedFileSize: 3, expectedSha256: new string('z', 64)), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(0, handler.TotalRequests);
        }

        [Fact]
        public async Task DownloadAndInstallAsync_HashWrongLength_ReturnsFalseWithoutDownload()
        {
            (UpdateDownloadService service, RecordingHandler handler, _) = BuildService(payload: [42], contentLength: 1);

            // 32 Hex-Zeichen = 16 Bytes, gültiges Hex, aber falsche Länge für SHA-256 (32 Bytes).
            bool result = await service.DownloadAndInstallAsync(
                Offer("1.0.3", expectedFileSize: 1, expectedSha256: new string('a', 32)), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(0, handler.TotalRequests);
        }

        [Fact]
        public async Task DownloadAndInstallAsync_HashAndSignatureValid_StartsInstallerWithDownloadedFile()
        {
            byte[] payload = [1, 2, 3, 4, 5];
            string version = "1.0.4";
            (UpdateDownloadService service, _, FakeInstallerLauncher launcher) = BuildService(payload, contentLength: payload.Length);

            string tempPath = TempSetupPath(version);
            try
            {
                bool result = await service.DownloadAndInstallAsync(
                    Offer(version, expectedFileSize: payload.Length, expectedSha256: HashOf(payload)), cancellationToken: TestContext.Current.CancellationToken);

                Assert.True(result);
                Assert.Equal(tempPath, Assert.Single(launcher.StartedPaths));

                // Im Erfolgsfall bleibt die Datei liegen, denn der Installer braucht sie noch.
                Assert.True(File.Exists(tempPath));
            }
            finally
            {
                DeleteIfPresent(tempPath);
            }
        }

        [Fact]
        public async Task DownloadAndInstallAsync_SignatureOfAnotherKey_DeletesFileAndDoesNotInstall()
        {
            // Das Angreifermodell, für das die Signatur da ist: Datei und Hash-Zeile sind
            // ausgetauscht und passen zueinander, signiert hat aber jemand anderes.
            byte[] payload = [6, 6, 6, 6];
            string version = "1.0.6";
            using ECDsa attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] forged = attacker.SignData(payload, HashAlgorithmName.SHA256);
            (UpdateDownloadService service, _, FakeInstallerLauncher launcher) = BuildService(payload, contentLength: payload.Length, signature: forged);

            bool result = await service.DownloadAndInstallAsync(
                Offer(version, expectedFileSize: payload.Length, expectedSha256: HashOf(payload)), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Empty(launcher.StartedPaths);
            Assert.False(File.Exists(TempSetupPath(version)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(63)]
        [InlineData(65)]
        [InlineData(4096)]
        public async Task DownloadAndInstallAsync_SignatureOfWrongLength_DoesNotLoadTheSetup(int length)
        {
            // Die Signatur wird zuerst geladen. Taugt sie nicht, bleibt der große Download aus.
            byte[] payload = [5, 5, 5];
            (UpdateDownloadService service, RecordingHandler handler, FakeInstallerLauncher launcher) =
                BuildService(payload, contentLength: payload.Length, signature: new byte[length]);

            bool result = await service.DownloadAndInstallAsync(
                Offer("1.0.7", expectedFileSize: payload.Length, expectedSha256: HashOf(payload)), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(1, handler.SignatureRequests);
            Assert.Equal(0, handler.SetupRequests);
            Assert.Empty(launcher.StartedPaths);
        }

        [Fact]
        public async Task DownloadAndInstallAsync_SignatureMissingOnServer_DoesNotLoadTheSetup()
        {
            byte[] payload = [4, 4, 4];
            (UpdateDownloadService service, RecordingHandler handler, FakeInstallerLauncher launcher) =
                BuildService(payload, contentLength: payload.Length, signatureStatusCode: HttpStatusCode.NotFound);

            bool result = await service.DownloadAndInstallAsync(
                Offer("1.0.8", expectedFileSize: payload.Length, expectedSha256: HashOf(payload)), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(0, handler.SetupRequests);
            Assert.Empty(launcher.StartedPaths);
        }

        [Fact]
        public async Task DownloadAndInstallAsync_WhileTheInstallerStarts_TheFileCannotBeReplaced()
        {
            // Zwischen Prüfung und Start darf niemand die geprüfte Datei überschreiben, sonst
            // startete der Installer etwas anderes als das, was geprüft wurde.
            byte[] payload = [3, 1, 4, 1, 5];
            string version = "1.0.9";
            WriteProbeLauncher launcher = new();
            (UpdateDownloadService service, _, _) = BuildService(payload, contentLength: payload.Length, launcher: launcher);

            try
            {
                bool result = await service.DownloadAndInstallAsync(
                    Offer(version, expectedFileSize: payload.Length, expectedSha256: HashOf(payload)), cancellationToken: TestContext.Current.CancellationToken);

                Assert.True(result);
                Assert.True(launcher.WasStarted);
                Assert.False(launcher.CouldWrite);
            }
            finally
            {
                DeleteIfPresent(TempSetupPath(version));
            }
        }

        [Fact]
        public async Task DownloadAndInstallAsync_InstallerStartFails_ReturnsFalse()
        {
            byte[] payload = [7, 7, 7];
            string version = "1.0.5";
            (UpdateDownloadService service, _, FakeInstallerLauncher launcher) =
                BuildService(payload, contentLength: payload.Length, installerStartSucceeds: false);

            try
            {
                bool result = await service.DownloadAndInstallAsync(
                    Offer(version, expectedFileSize: payload.Length, expectedSha256: HashOf(payload)), cancellationToken: TestContext.Current.CancellationToken);

                Assert.False(result);
                _ = Assert.Single(launcher.StartedPaths);
            }
            finally
            {
                DeleteIfPresent(TempSetupPath(version));
            }
        }

        private static UpdateInfo Offer(
            string version,
            long expectedFileSize = 0,
            string? expectedSha256 = null,
            Uri? downloadUrl = null,
            Uri? signatureUrl = null)
            => new(
                Version: version,
                ReleaseNotes: string.Empty,
                DownloadUrl: downloadUrl ?? ValidUrl,
                FileSizeBytes: expectedFileSize,
                ExpectedSha256: expectedSha256 ?? WellFormedHash,
                SignatureUrl: signatureUrl ?? ValidSignatureUrl);

        private static string HashOf(byte[] payload) => Convert.ToHexString(SHA256.HashData(payload));

        private static string TempSetupPath(string version) => Path.Combine(Path.GetTempPath(), $"EchoPlay-Setup-{version}.exe");

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Baut den Dienst. Ohne ausdrückliche Signatur liefert der Handler eine gültige Signatur
        /// des Testschlüssels über die Nutzdaten.
        /// </summary>
        private (UpdateDownloadService Service, RecordingHandler Handler, FakeInstallerLauncher Launcher) BuildService(
            byte[]? payload = null,
            long contentLength = 0,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            bool installerStartSucceeds = true,
            byte[]? signature = null,
            HttpStatusCode signatureStatusCode = HttpStatusCode.OK,
            IInstallerLauncher? launcher = null)
        {
            byte[] setupBytes = payload ?? [];
            byte[] signatureBytes = signature ?? _signingKey.SignData(setupBytes, HashAlgorithmName.SHA256);
            RecordingHandler handler = new(setupBytes, contentLength, statusCode, signatureBytes, signatureStatusCode);
            FakeInstallerLauncher fakeLauncher = new(installerStartSucceeds);
            ReleaseSignatureVerifier verifier = new(Convert.ToBase64String(_signingKey.ExportSubjectPublicKeyInfo()));

            UpdateDownloadService service = new(
                new SingleHandlerFactory(handler),
                launcher ?? fakeLauncher,
                verifier,
                new FakeLoggerFactory());

            return (service, handler, fakeLauncher);
        }

        /// <summary>
        /// Liefert für Adressen auf <c>.sig</c> die Signatur, sonst die Setup-Bytes, und zählt
        /// beide Arten getrennt. Eine Längenangabe über 0 setzt den Content-Length-Kopf der
        /// Setup-Antwort.
        /// </summary>
        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly byte[] _payload;
            private readonly long _contentLength;
            private readonly HttpStatusCode _statusCode;
            private readonly byte[] _signature;
            private readonly HttpStatusCode _signatureStatusCode;

            public int SetupRequests { get; private set; }

            public int SignatureRequests { get; private set; }

            public int TotalRequests => SetupRequests + SignatureRequests;

            public RecordingHandler(byte[] payload, long contentLength, HttpStatusCode statusCode, byte[] signature, HttpStatusCode signatureStatusCode)
            {
                _payload = payload;
                _contentLength = contentLength;
                _statusCode = statusCode;
                _signature = signature;
                _signatureStatusCode = signatureStatusCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri?.AbsolutePath.EndsWith(".sig", StringComparison.OrdinalIgnoreCase) == true)
                {
                    SignatureRequests++;
                    return Task.FromResult(new HttpResponseMessage(_signatureStatusCode) { Content = new ByteArrayContent(_signature) });
                }

                SetupRequests++;
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

        /// <summary>
        /// Versucht im Moment des Starts, die Setup-Datei zum Schreiben zu öffnen, und merkt sich,
        /// ob das ging.
        /// </summary>
        private sealed class WriteProbeLauncher : IInstallerLauncher
        {
            public bool WasStarted { get; private set; }

            public bool CouldWrite { get; private set; }

            public bool Start(string setupPath)
            {
                WasStarted = true;
                try
                {
                    using FileStream writer = new(setupPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                    CouldWrite = true;
                }
                catch (IOException)
                {
                    CouldWrite = false;
                }

                return true;
            }
        }

        /// <summary>
        /// Gibt Clients über einem gemeinsamen Handler aus, der dabei nicht freigegeben wird.
        /// </summary>
        private sealed class SingleHandlerFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;
            public SingleHandlerFactory(HttpMessageHandler handler) => _handler = handler;
            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }
    }
}

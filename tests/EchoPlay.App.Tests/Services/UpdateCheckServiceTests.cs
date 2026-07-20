using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Verifiziert den GitHub-Releases-basierten Update-Check für die wichtigsten
    /// Pfade — Offline-Modus, HTTP-Fehler, SkippedVersion.
    /// </summary>
    public sealed class UpdateCheckServiceTests
    {
        [Fact]
        public async Task CheckForUpdateAsync_OfflineMode_ReturnsNullWithoutHttpCall()
        {
            FakeAppSettingsDataService settings = new(new AppSettings { OfflineMode = true });
            (UpdateCheckService service, RecordingHandler handler) = BuildService(settings);

            UpdateInfo? result = await service.CheckForUpdateAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Equal(0, handler.RequestCount);
        }

        [Fact]
        public async Task CheckForUpdateAsync_HttpError_ReturnsNull()
        {
            FakeAppSettingsDataService settings = new(new AppSettings());
            (UpdateCheckService service, _) = BuildService(settings, statusCode: HttpStatusCode.InternalServerError);

            UpdateInfo? result = await service.CheckForUpdateAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task CheckForUpdateAsync_NetworkException_ReturnsNull()
        {
            FakeAppSettingsDataService settings = new(new AppSettings());
            ThrowingHandler handler = new();
            UpdateCheckService service = BuildServiceWithHandler(settings, handler);

            UpdateInfo? result = await service.CheckForUpdateAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task CheckForUpdateAsync_CanceledToken_ReturnsNull()
        {
            FakeAppSettingsDataService settings = new(new AppSettings());
            (UpdateCheckService service, _) = BuildService(settings);
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            UpdateInfo? result = await service.CheckForUpdateAsync(cts.Token);

            Assert.Null(result);
        }

        [Fact]
        public async Task SkipVersionAsync_PersistsVersionInSettings()
        {
            FakeAppSettingsDataService settings = new(new AppSettings());
            (UpdateCheckService service, _) = BuildService(settings);

            await service.SkipVersionAsync("1.2.3", cancellationToken: TestContext.Current.CancellationToken);

            AppSettings persisted = await settings.GetAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("1.2.3", persisted.SkippedUpdateVersion);
        }

        [Fact]
        public async Task SkipVersionAsync_CanceledToken_ThrowsOperationCanceled()
        {
            FakeAppSettingsDataService settings = new(new AppSettings());
            (UpdateCheckService service, _) = BuildService(settings);
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            _ = await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await service.SkipVersionAsync("1.2.3", cts.Token));
        }

        // ── Hash-Extraktor (SHA-256-Hash-Pin aus Release-Body) ────────────────────

        [Fact]
        public void ExtractSha256_StandardLine_ReturnsHash()
        {
            string body = "## Release Notes\n\nFeature X.\n\nSHA256: 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\n\n— ENDE —";

            string hash = UpdateCheckService.ExtractSha256(body);

            Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", hash);
        }

        [Fact]
        public void ExtractSha256_HyphenAndEqualsSignAndUpperCase_ReturnsHashAsWritten()
        {
            string body = "Setup-Datei:\nSHA-256 = ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890";

            string hash = UpdateCheckService.ExtractSha256(body);

            // Groß-/Kleinschreibung wird nicht normalisiert — der Vergleich im Updater
            // läuft über Convert.FromHexString und ist case-insensitive.
            Assert.Equal("ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890", hash);
        }

        [Fact]
        public void ExtractSha256_NoHashInBody_ReturnsEmpty()
        {
            string body = "## Release\n\nFeature X.\nNur Release-Notes, kein Hash gepflegt.";

            string hash = UpdateCheckService.ExtractSha256(body);

            Assert.Equal(string.Empty, hash);
        }

        [Fact]
        public void ExtractSha256_EmptyBody_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, UpdateCheckService.ExtractSha256(string.Empty));
        }

        [Fact]
        public void ExtractSha256_HashTooShort_ReturnsEmpty()
        {
            // 60 Hex-Zeichen statt 64 — Pattern matcht nicht, weil \b nach genau 64 erwartet wird.
            string body = "SHA256: 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcd";

            string hash = UpdateCheckService.ExtractSha256(body);

            Assert.Equal(string.Empty, hash);
        }

        [Fact]
        public void ExtractSha256_NonHexChars_ReturnsEmpty()
        {
            // 'g'..'z' sind keine Hex-Zeichen — Pattern lehnt ab.
            string body = "SHA256: gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg";

            string hash = UpdateCheckService.ExtractSha256(body);

            Assert.Equal(string.Empty, hash);
        }

        // ── Test-Helfer ──────────────────────────────────────────────────────────

        private static (UpdateCheckService Service, RecordingHandler Handler) BuildService(
            FakeAppSettingsDataService settings,
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            RecordingHandler handler = new(statusCode);

            ServiceCollection services = new();
            _ = services.AddScoped<EchoPlay.Data.Services.Interfaces.IAppSettingsDataService>(_ => settings);
            _ = services.AddSingleton<IHttpClientFactory>(new SingleHandlerFactory(handler));

            ServiceProvider provider = services.BuildServiceProvider();
            UpdateCheckService service = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IHttpClientFactory>(),
                new FakeLoggerFactory());

            return (service, handler);
        }

        private static UpdateCheckService BuildServiceWithHandler(
            FakeAppSettingsDataService settings,
            HttpMessageHandler handler)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<EchoPlay.Data.Services.Interfaces.IAppSettingsDataService>(_ => settings);
            _ = services.AddSingleton<IHttpClientFactory>(new SingleHandlerFactory(handler));

            ServiceProvider provider = services.BuildServiceProvider();
            return new UpdateCheckService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IHttpClientFactory>(),
                new FakeLoggerFactory());
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            public int RequestCount { get; private set; }

            public RecordingHandler(HttpStatusCode statusCode)
            {
                _statusCode = statusCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                HttpResponseMessage response = new(_statusCode)
                {
                    Content = new StringContent("{}")
                };
                return Task.FromResult(response);
            }
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("Simulierter Netzwerkfehler");
        }

        private sealed class SingleHandlerFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;
            public SingleHandlerFactory(HttpMessageHandler handler) => _handler = handler;
            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }
    }
}

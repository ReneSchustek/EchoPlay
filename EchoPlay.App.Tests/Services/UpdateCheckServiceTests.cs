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
    /// Verifiziert den GitHub-Releases-basierten Update-Check.
    /// Pilot-Tests fuer die wichtigsten Pfade — Offline-Modus, HTTP-Fehler,
    /// SkippedVersion. Tests fuer den vollstaendigen Versionsvergleich gegen die
    /// Assembly-Version sind im Brief 297 Folge-Brief abgedeckt.
    /// </summary>
    public sealed class UpdateCheckServiceTests
    {
        [Fact]
        public async Task CheckForUpdateAsync_OfflineMode_ReturnsNullWithoutHttpCall()
        {
            FakeAppSettingsDataService settings = new(new AppSettings { OfflineMode = true });
            (UpdateCheckService service, RecordingHandler handler) = BuildService(settings);

            UpdateInfo? result = await service.CheckForUpdateAsync();

            Assert.Null(result);
            Assert.Equal(0, handler.RequestCount);
        }

        [Fact]
        public async Task CheckForUpdateAsync_HttpError_ReturnsNull()
        {
            FakeAppSettingsDataService settings = new(new AppSettings());
            (UpdateCheckService service, _) = BuildService(settings, statusCode: HttpStatusCode.InternalServerError);

            UpdateInfo? result = await service.CheckForUpdateAsync();

            Assert.Null(result);
        }

        [Fact]
        public async Task CheckForUpdateAsync_NetworkException_ReturnsNull()
        {
            FakeAppSettingsDataService settings = new(new AppSettings());
            ThrowingHandler handler = new();
            UpdateCheckService service = BuildServiceWithHandler(settings, handler);

            UpdateInfo? result = await service.CheckForUpdateAsync();

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

            await service.SkipVersionAsync("1.2.3");

            AppSettings persisted = await settings.GetAsync();
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

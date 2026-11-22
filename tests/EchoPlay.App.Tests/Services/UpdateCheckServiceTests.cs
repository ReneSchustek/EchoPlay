using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.App.Composition;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Verifiziert den GitHub-Releases-basierten Update-Check für die wichtigsten
    /// Pfade: Offline-Modus, HTTP-Fehler, SkippedVersion.
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
        public async Task CheckForUpdateAsync_HangingServer_GivesUpAtTheClientLimitAndOffersNothing()
        {
            // Über den echten Client mit seiner Wiederholungsregel: Der Dienst setzt keine
            // eigene Frist, also endet die Prüfung genau an der Gesamtgrenze des Clients,
            // nach mindestens einer Wiederholung.
            FakeTimeProvider time = ClockDriver.CreateClock();
            HangingHttpHandler hanging = new();

            ServiceCollection services = new();
            _ = services.AddSingleton<TimeProvider>(time);
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddScoped<EchoPlay.Data.Services.Interfaces.IAppSettingsDataService>(_ => new FakeAppSettingsDataService());
            _ = services.AddEchoPlayHttpClients();
            _ = services.AddHttpClient("UpdateCheck").ConfigurePrimaryHttpMessageHandler(() => hanging);

            using ServiceProvider provider = services.BuildServiceProvider();
            UpdateCheckService service = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IHttpClientFactory>(),
                new FakeLoggerFactory());

            Task<UpdateInfo?> check = service.CheckForUpdateAsync(TestContext.Current.CancellationToken);
            TimeSpan elapsed = await ClockDriver.RunUntilCompletedAsync(time, check, hanging.FirstAttempt);

            Assert.Null(await check);
            TimeSpan limit = HttpClientRegistration.UpdateCheckTotalTimeout;
            Assert.InRange(elapsed, limit, limit + ClockDriver.Tolerance);
            Assert.True(hanging.Attempts >= 2, $"Nur {hanging.Attempts} Versuch(e) bis zur Grenze.");
        }

        [Fact]
        public async Task CheckForUpdateAsync_CanceledToken_PassesTheCancellationOn()
        {
            FakeAppSettingsDataService settings = new(new AppSettings());
            (UpdateCheckService service, _) = BuildService(settings);
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            // Abgebrochen ist nicht „kein Update": Der Aufrufer soll den Unterschied sehen.
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckForUpdateAsync(cts.Token));
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

            // Groß-/Kleinschreibung wird nicht normalisiert, der Vergleich im Updater
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
            // 60 Hex-Zeichen statt 64, das Pattern matcht nicht, weil \b nach genau 64 erwartet wird.
            string body = "SHA256: 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcd";

            string hash = UpdateCheckService.ExtractSha256(body);

            Assert.Equal(string.Empty, hash);
        }

        [Fact]
        public void ExtractSha256_NonHexChars_ReturnsEmpty()
        {
            // 'g'..'z' sind keine Hex-Zeichen, das Pattern lehnt ab.
            string body = "SHA256: gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg";

            string hash = UpdateCheckService.ExtractSha256(body);

            Assert.Equal(string.Empty, hash);
        }

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

        [Fact]
        public async Task CheckForUpdateAsync_ReleaseWithoutHashLine_OffersNoUpdate()
        {
            // Ein Release ohne brauchbaren Hash ist nicht installierbar, der Downloader lehnt
            // es ohnehin ab. Es anzubieten hieße, zu einem Update einzuladen, das systematisch
            // scheitert.
            FakeAppSettingsDataService settings = new(new AppSettings());
            UpdateCheckService service = BuildServiceWithHandler(
                settings, new ReleaseJsonHandler(ReleaseJson("v99.0.0", body: "Kein Hash hier drin.")));

            UpdateInfo? result = await service.CheckForUpdateAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task CheckForUpdateAsync_ReleaseWithHashLine_OffersUpdate()
        {
            // Gegenstück: Mit gültiger Hash-Zeile kommt das Angebot durch, sonst würde der
            // Test darüber auch bei einem kaputten Update-Check bestehen.
            const string hash = "d3811116eb3683555d77374287d21a44286c78f2818057b43a20e402ec57c172";
            FakeAppSettingsDataService settings = new(new AppSettings());
            UpdateCheckService service = BuildServiceWithHandler(
                settings, new ReleaseJsonHandler(ReleaseJson("v99.0.0", body: $"Neue Version.\n\nSHA256: {hash}")));

            UpdateInfo? result = await service.CheckForUpdateAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.Equal("99.0.0", result.Version);
            Assert.Equal(hash, result.ExpectedSha256);
        }

        [Fact]
        public async Task CheckForUpdateAsync_HashLineDoesNotAppearInTheReleaseNotes()
        {
            // 64 Hex-Zeichen sagen dem Nutzer nichts und verdrängen im Dialog, was ihn
            // interessiert. Der Hash bleibt als ExpectedSha256 erhalten, nur die Anzeige nicht.
            const string hash = "d3811116eb3683555d77374287d21a44286c78f2818057b43a20e402ec57c172";
            FakeAppSettingsDataService settings = new(new AppSettings());
            UpdateCheckService service = BuildServiceWithHandler(
                settings,
                new ReleaseJsonHandler(ReleaseJson(
                    "v99.0.0",
                    body: $"Neue Version mit zwei Korrekturen.\n\n**Integritätsprüfung**\n\nSHA256: {hash}")));

            UpdateInfo? result = await service.CheckForUpdateAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.Equal(hash, result.ExpectedSha256);
            Assert.DoesNotContain(hash, result.ReleaseNotes, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SHA", result.ReleaseNotes, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("zwei Korrekturen", result.ReleaseNotes, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("Nur Text ohne Hash.", "Nur Text ohne Hash.")]
        [InlineData("", "")]
        public void StripSha256Line_WithoutHash_LeavesTextUnchanged(string body, string expected)
        {
            Assert.Equal(expected, UpdateCheckService.StripSha256Line(body));
        }

        [Fact]
        public async Task CheckForUpdateAsync_OlderRelease_OffersNothing()
        {
            FakeAppSettingsDataService settings = new(new AppSettings());
            UpdateCheckService service = BuildServiceWithHandler(
                settings,
                new ReleaseJsonHandler(ReleaseJson("v0.0.1", body: $"Alt.\n\nSHA256: {ValidHash}")));

            UpdateInfo? result = await service.CheckForUpdateAsync(TestContext.Current.CancellationToken);

            // Ein älteres Release ist kein Update. Es anzubieten hieße, den Anwender auf einen
            // Stand zurückzuschicken, den er längst hinter sich hat.
            Assert.Null(result);
        }

        [Fact]
        public async Task CheckForUpdateAsync_SkippedVersion_IsNotOfferedAgain()
        {
            FakeAppSettingsDataService settings = new(new AppSettings { SkippedUpdateVersion = "99.0.0" });
            UpdateCheckService service = BuildServiceWithHandler(
                settings,
                new ReleaseJsonHandler(ReleaseJson("v99.0.0", body: $"Neu.\n\nSHA256: {ValidHash}")));

            UpdateInfo? result = await service.CheckForUpdateAsync(TestContext.Current.CancellationToken);

            // „Diese Version überspringen" ist eine Ansage des Anwenders. Wird sie beim
            // nächsten Start ignoriert, fragt die Anwendung ihn immer wieder dasselbe.
            Assert.Null(result);
        }

        [Fact]
        public async Task CheckForUpdateAsync_SkippedOtherVersion_StillOffersTheNewOne()
        {
            FakeAppSettingsDataService settings = new(new AppSettings { SkippedUpdateVersion = "98.0.0" });
            UpdateCheckService service = BuildServiceWithHandler(
                settings,
                new ReleaseJsonHandler(ReleaseJson("v99.0.0", body: $"Neu.\n\nSHA256: {ValidHash}")));

            UpdateInfo? result = await service.CheckForUpdateAsync(TestContext.Current.CancellationToken);

            // Übersprungen wurde eine andere Fassung, die neue darf trotzdem erscheinen.
            Assert.NotNull(result);
        }

        [Fact]
        public async Task CheckForUpdateAsync_ReleaseWithoutSetupFile_OffersNothing()
        {
            FakeAppSettingsDataService settings = new(new AppSettings());
            const string withoutSetup = $$"""
            {
              "tag_name": "v99.0.0",
              "body": "Neu.\n\nSHA256: {{ValidHash}}",
              "assets": []
            }
            """;

            UpdateCheckService service = BuildServiceWithHandler(settings, new ReleaseJsonHandler(withoutSetup));

            UpdateInfo? result = await service.CheckForUpdateAsync(TestContext.Current.CancellationToken);

            // Ohne Setup-Datei gibt es nichts herunterzuladen. Ein Angebot ohne Ziel endet
            // in einer Fehlermeldung, für die der Anwender nichts kann.
            Assert.Null(result);
        }

        [Fact]
        public async Task CheckForUpdateAsync_UnreadableVersionTag_OffersNothing()
        {
            FakeAppSettingsDataService settings = new(new AppSettings());
            UpdateCheckService service = BuildServiceWithHandler(
                settings,
                new ReleaseJsonHandler(ReleaseJson("Weihnachtsausgabe", body: $"Neu.\n\nSHA256: {ValidHash}")));

            UpdateInfo? result = await service.CheckForUpdateAsync(TestContext.Current.CancellationToken);

            // Eine Kennzeichnung, die keine Versionsnummer ist, lässt sich nicht vergleichen.
            // Dann wird nichts angeboten, statt zu raten.
            Assert.Null(result);
        }

        /// <summary>Ein formal gültiger Hash; der Inhalt spielt für diese Tests keine Rolle.</summary>
        private const string ValidHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static string ReleaseJson(string tag, string body) =>
            ReleaseJson(tag, body, signatureName: $"EchoPlay-Setup-{tag}.exe.sig");

        /// <summary>Release mit Setup-Datei und, wenn ein Name genannt ist, einer Signatur dazu.</summary>
        private static string ReleaseJson(string tag, string body, string? signatureName)
        {
            string signatureAsset = signatureName is null
                ? string.Empty
                : $$"""
                ,
                {
                  "name": "{{signatureName}}",
                  "browser_download_url": "https://github.com/ReneSchustek/EchoPlay/releases/download/{{tag}}/{{signatureName}}",
                  "size": 64
                }
                """;

            return $$"""
            {
              "tag_name": "{{tag}}",
              "body": {{System.Text.Json.JsonSerializer.Serialize(body)}},
              "assets": [
                {
                  "name": "EchoPlay-Setup-{{tag}}.exe",
                  "browser_download_url": "https://github.com/ReneSchustek/EchoPlay/releases/download/{{tag}}/EchoPlay-Setup-{{tag}}.exe",
                  "size": 80000000
                }{{signatureAsset}}
              ]
            }
            """;
        }

        [Fact]
        public async Task CheckForUpdateAsync_ReleaseWithSignature_OffersItsAddress()
        {
            FakeAppSettingsDataService settings = new(new AppSettings());
            UpdateCheckService service = BuildServiceWithHandler(
                settings, new ReleaseJsonHandler(ReleaseJson("v99.0.0", body: $"Neu.\n\nSHA256: {ValidHash}")));

            UpdateInfo? result = await service.CheckForUpdateAsync(TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.Equal(
                new Uri("https://github.com/ReneSchustek/EchoPlay/releases/download/v99.0.0/EchoPlay-Setup-v99.0.0.exe.sig"),
                result.SignatureUrl);
        }

        [Fact]
        public async Task CheckForUpdateAsync_ReleaseWithoutSignature_OffersNothing()
        {
            // Ohne Signatur installiert der Downloader nicht. Das Release anzubieten hieße, zu
            // einem Update einzuladen, das in jedem Fall abgelehnt wird.
            FakeAppSettingsDataService settings = new(new AppSettings());
            UpdateCheckService service = BuildServiceWithHandler(
                settings, new ReleaseJsonHandler(ReleaseJson("v99.0.0", $"Neu.\n\nSHA256: {ValidHash}", signatureName: null)));

            UpdateInfo? result = await service.CheckForUpdateAsync(TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task CheckForUpdateAsync_SignatureOfAnotherFile_OffersNothing()
        {
            // Eine Signatur gilt für die Datei, deren Namen sie trägt. Eine beliebige .sig
            // anzunehmen, prüfte womöglich die Signatur einer anderen Datei.
            FakeAppSettingsDataService settings = new(new AppSettings());
            UpdateCheckService service = BuildServiceWithHandler(
                settings, new ReleaseJsonHandler(ReleaseJson("v99.0.0", $"Neu.\n\nSHA256: {ValidHash}", signatureName: "EchoPlay-Portable.zip.sig")));

            UpdateInfo? result = await service.CheckForUpdateAsync(TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        /// <summary>
        /// Antwortet mit dem vorgegebenen Release-JSON.
        /// </summary>
        private sealed class ReleaseJsonHandler : HttpMessageHandler
        {
            private readonly string _json;

            public ReleaseJsonHandler(string json) => _json = json;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_json, System.Text.Encoding.UTF8, "application/json")
                });
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

        /// <summary>
        /// Zählt Anfragen und antwortet mit festem Status.
        /// </summary>
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

        /// <summary>
        /// Scheitert mit einem Netzwerkfehler.
        /// </summary>
        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("Simulierter Netzwerkfehler");
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

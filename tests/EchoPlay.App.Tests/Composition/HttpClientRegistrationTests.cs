using EchoPlay.App.Composition;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using System.Threading;
using Xunit;

namespace EchoPlay.App.Tests.Composition
{
    /// <summary>
    /// Prüft die zentrale Registrierung der HTTP-Clients: Jeder benannte Client bekommt
    /// seine eigene Zeitgrenze und seine eigene Kennung.
    /// </summary>
    /// <remarks>
    /// Die Zeitgrenzen unterscheiden sich um zwei Größenordnungen — fünf Sekunden für die
    /// Erreichbarkeitsprüfung, fünf Minuten für das Herunterladen des Installationspakets.
    /// Griffe ein Verbraucher zum falschen Client, bräche entweder der Download nach
    /// wenigen Sekunden ab oder die Prüfung hinge minutenlang.
    ///
    /// Die Kennung im Kopf jeder Anfrage ist kein Beiwerk: Fremde Schnittstellen wie das
    /// Cover Art Archive weisen Anfragen ohne erkennbare Kennung ab.
    /// </remarks>
    public sealed class HttpClientRegistrationTests
    {
        [Fact]
        public void CoverDownloadClient_HasItsOwnTimeoutAndUserAgent()
        {
            using HttpClient client = CreateClient(CoverDownloader.HttpClientName);

            // Mit Wiederholungsregel übernimmt deren Gesamtfrist die Zeitmessung; der Client
            // selbst läuft dann ohne eigene Frist, sonst kämen sich beide ins Gehege.
            Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
            Assert.Contains("EchoPlay-CoverDownload", client.DefaultRequestHeaders.UserAgent.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void OnlineCheckClient_GivesUpAfterFiveSeconds()
        {
            using HttpClient client = CreateClient("OnlineCheck");

            // Die Erreichbarkeitsprüfung läuft beim Start; hinge sie, stünde die Anwendung.
            Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
            Assert.Contains("EchoPlay-OnlineCheck", client.DefaultRequestHeaders.UserAgent.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void UpdateDownloadClient_HasRoomForALargePackage()
        {
            using HttpClient client = CreateClient("UpdateDownload");

            Assert.Equal(TimeSpan.FromMinutes(5), client.Timeout);
            Assert.Contains("EchoPlay-UpdateDownload", client.DefaultRequestHeaders.UserAgent.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void UpdateCheckClient_AsksGitHubForItsOwnAnswerFormat()
        {
            using HttpClient client = CreateClient("UpdateCheck");

            Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
            Assert.Contains("application/vnd.github+json", client.DefaultRequestHeaders.Accept.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void AttachRequestLogging_WithoutBuilder_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(
                () => HttpClientRegistration.AttachRequestLogging(null!));
        }

        private static HttpClient CreateClient(string name)
        {
            ServiceCollection services = new();
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddEchoPlayHttpClients();

            ServiceProvider provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        }
    }
}

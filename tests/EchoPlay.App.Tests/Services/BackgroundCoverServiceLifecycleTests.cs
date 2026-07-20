using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für den Graceful-Shutdown-Kontrakt von <see cref="BackgroundCoverService"/>.
    /// Prüft das Zusammenspiel aus Task-Tracking, CancellationTokenSource und Timeout.
    /// </summary>
    public sealed class BackgroundCoverServiceLifecycleTests
    {
        [Fact]
        public async Task StopAsync_WaitsForRunningIteration_WhenCancellationIsObserved()
        {
            // InitialDelay lang genug, damit die Iteration beim CancellationToken hängt,
            // aber sofort beim Cancel sauber mit OperationCanceledException endet.
            BackgroundCoverServiceOptions options = new()
            {
                InitialDelay = TimeSpan.FromSeconds(30),
                Interval = TimeSpan.FromMinutes(5)
            };

            BackgroundCoverService service = CreateService(new NoopServiceScopeFactory(), options);

            service.Start();
            // Kurz warten, damit RunAsync bis zum ersten await Task.Delay(InitialDelay, ct) läuft.
            await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

            Stopwatch sw = Stopwatch.StartNew();
            await service.StopAsync(TimeSpan.FromSeconds(2), cancellationToken: TestContext.Current.CancellationToken);
            sw.Stop();

            // Wenn der Token respektiert wird, endet StopAsync deutlich vor dem Timeout.
            Assert.True(sw.ElapsedMilliseconds < 1500,
                $"Erwartet < 1500 ms (Iteration muss auf Cancel reagieren), tatsächlich {sw.ElapsedMilliseconds} ms.");

            // Zweiter StopAsync-Aufruf ist idempotent und muss sofort zurückkehren.
            await service.StopAsync(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task StopAsync_RespectsTimeout_WhenIterationIgnoresCancellation()
        {
            // InitialDelay = Zero, damit die Iteration sofort in LoadMissingLocalCoversAsync landet
            // und dort auf dem HangingServiceScopeFactory.CreateScope() blockiert — unabhängig vom CT.
            BackgroundCoverServiceOptions options = new()
            {
                InitialDelay = TimeSpan.Zero,
                Interval = TimeSpan.FromMinutes(5)
            };

            using HangingServiceScopeFactory hangingFactory = new();
            BackgroundCoverService service = CreateService(hangingFactory, options);

            service.Start();
            // Kurz warten, damit RunAsync in den blockierenden CreateScope()-Aufruf läuft.
            await Task.Delay(100, cancellationToken: TestContext.Current.CancellationToken);

            Stopwatch sw = Stopwatch.StartNew();
            await service.StopAsync(TimeSpan.FromMilliseconds(500), cancellationToken: TestContext.Current.CancellationToken);
            sw.Stop();

            // StopAsync kehrt nach dem Timeout zurück, statt auf den hängenden Task zu warten.
            Assert.True(sw.ElapsedMilliseconds < 2000,
                $"Erwartet < 2000 ms (Timeout greift), tatsächlich {sw.ElapsedMilliseconds} ms.");
            Assert.True(sw.ElapsedMilliseconds >= 400,
                $"Erwartet >= 400 ms (Timeout soll tatsächlich abgewartet werden), tatsächlich {sw.ElapsedMilliseconds} ms.");

            // Hang aufheben — blockierter ThreadPool-Thread darf nach dem Test wieder frei werden.
            hangingFactory.Release();
        }

        private static BackgroundCoverService CreateService(
            Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory,
            BackgroundCoverServiceOptions options)
        {
            FakeLoggerFactory loggerFactory = new();
            CoverService coverService = new(scopeFactory, loggerFactory);
            FakeHttpClientFactory httpClientFactory = new();
            FakeSpotifyCredentialStore credentialStore = new();

            return new BackgroundCoverService(
                scopeFactory,
                coverService,
                httpClientFactory,
                credentialStore,
                options,
                loggerFactory);
        }

        /// <summary>
        /// Fake-ScopeFactory, die CreateScope() nie aufrufen lässt — der Test cancelt den
        /// Service bereits im InitialDelay. Wirft beim unerwarteten Aufruf, damit Abweichungen
        /// im Ablauf laut schlagen.
        /// </summary>
        private sealed class NoopServiceScopeFactory : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory
        {
            public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope()
                => throw new InvalidOperationException("CreateScope wurde unerwartet aufgerufen.");
        }

        /// <summary>
        /// Fake-ScopeFactory, deren CreateScope() blockiert, bis <see cref="Release"/> oder
        /// <see cref="Dispose"/> aufgerufen wird. Simuliert eine Iteration, die den
        /// <see cref="CancellationToken"/> nicht beobachtet.
        /// </summary>
        private sealed class HangingServiceScopeFactory
            : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory, IDisposable
        {
            private readonly ManualResetEventSlim _release = new(false);

            public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope()
            {
                // Bewusst ohne CancellationToken-Argument: simuliert Code, der CT ignoriert.
                _release.Wait(cancellationToken: TestContext.Current.CancellationToken);
                throw new InvalidOperationException("Released — Iteration wird hart abgebrochen.");
            }

            public void Release() => _release.Set();

            public void Dispose()
            {
                _release.Set();
                _release.Dispose();
            }
        }

        private sealed class FakeHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new();
        }
    }
}

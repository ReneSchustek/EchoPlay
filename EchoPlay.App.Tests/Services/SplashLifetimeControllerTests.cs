using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.App.Services;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Verifiziert, dass die Mindest-Anzeigedauer des Splash auch bei warmem Cache eingehalten wird.
    /// </summary>
    public sealed class SplashLifetimeControllerTests
    {
        [Fact]
        public async Task WaitForMinimumDurationAsync_ElapsedAlreadyExceedsMinimum_ReturnsImmediately()
        {
            SplashLifetimeController controller = new();
            // Mindestdauer bewusst ueberschreiten, damit die Wartezeit entfaellt.
            await Task.Delay(SplashLifetimeController.MinimumDuration + TimeSpan.FromMilliseconds(50));

            Stopwatch sw = Stopwatch.StartNew();
            await controller.WaitForMinimumDurationAsync(cancellationToken: TestContext.Current.CancellationToken);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 200,
                $"Erwartet sofortiger Return (< 200 ms), tatsaechlich {sw.ElapsedMilliseconds} ms.");
        }

        [Fact]
        public async Task WaitForMinimumDurationAsync_FreshController_WaitsCloseToMinimum()
        {
            SplashLifetimeController controller = new();

            Stopwatch sw = Stopwatch.StartNew();
            await controller.WaitForMinimumDurationAsync(cancellationToken: TestContext.Current.CancellationToken);
            sw.Stop();

            // Der Test toleriert Scheduler-Jitter, prueft aber, dass die Wartezeit signifikant
            // ueber 1000 ms liegt (Default-Mindestdauer ist 1500 ms).
            Assert.True(sw.ElapsedMilliseconds >= 1000,
                $"Erwartet >= 1000 ms Wartezeit, tatsaechlich {sw.ElapsedMilliseconds} ms.");
        }

        [Fact]
        public async Task WaitForMinimumDurationAsync_TokenCanceled_ReturnsEarly()
        {
            SplashLifetimeController controller = new();
            using CancellationTokenSource cts = new();

            // Cancellation kurz nach Beginn — Wartezeit muss vorzeitig enden.
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                await cts.CancelAsync();
            });

            Stopwatch sw = Stopwatch.StartNew();
            await controller.WaitForMinimumDurationAsync(cts.Token);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 1000,
                $"Erwartet vorzeitiges Ende (< 1000 ms), tatsaechlich {sw.ElapsedMilliseconds} ms.");
        }
    }
}

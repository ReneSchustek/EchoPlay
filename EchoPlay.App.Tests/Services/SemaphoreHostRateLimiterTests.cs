using EchoPlay.App.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="SemaphoreHostRateLimiter"/>.
    /// Prüft das Einhalten des Minimum-Intervalls zwischen aufeinanderfolgenden Aufrufen.
    /// </summary>
    public sealed class SemaphoreHostRateLimiterTests
    {
        [Fact]
        public async Task WaitAsync_FirstCall_ReturnsImmediately()
        {
            SemaphoreHostRateLimiter limiter = new(new Dictionary<string, TimeSpan>
            {
                ["test.host"] = TimeSpan.FromSeconds(1)
            });

            Stopwatch sw = Stopwatch.StartNew();
            await limiter.WaitAsync("test.host");
            sw.Stop();

            // Erster Aufruf darf keine nennenswerte Wartezeit haben
            Assert.True(sw.ElapsedMilliseconds < 200);
        }

        [Fact]
        public async Task WaitAsync_SecondCallTooFast_EnforcesMinimumInterval()
        {
            SemaphoreHostRateLimiter limiter = new(new Dictionary<string, TimeSpan>
            {
                ["slow.host"] = TimeSpan.FromMilliseconds(300)
            });

            await limiter.WaitAsync("slow.host");

            Stopwatch sw = Stopwatch.StartNew();
            await limiter.WaitAsync("slow.host");
            sw.Stop();

            // Zweiter Aufruf muss mindestens ~300 ms warten
            Assert.True(sw.ElapsedMilliseconds >= 250, $"Erwartet >= 250 ms, tatsächlich {sw.ElapsedMilliseconds} ms");
        }

        [Fact]
        public async Task WaitAsync_DifferentHosts_AreIndependent()
        {
            SemaphoreHostRateLimiter limiter = new(new Dictionary<string, TimeSpan>
            {
                ["host-a"] = TimeSpan.FromSeconds(5),
                ["host-b"] = TimeSpan.FromSeconds(5)
            });

            await limiter.WaitAsync("host-a");

            // Host-B wurde noch nie aufgerufen — darf sofort zurückkehren
            Stopwatch sw = Stopwatch.StartNew();
            await limiter.WaitAsync("host-b");
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 200);
        }

        [Fact]
        public async Task Dispose_ReleasesSemaphores_AndBlocksFurtherWaitAsync()
        {
            SemaphoreHostRateLimiter limiter = new(new Dictionary<string, TimeSpan>
            {
                ["disposed.host"] = TimeSpan.FromMilliseconds(100)
            });

            // Ersten Aufruf durchlaufen, damit intern ein SemaphoreSlim angelegt wird.
            await limiter.WaitAsync("disposed.host");

            limiter.Dispose();

            // Ein zweiter Dispose ist idempotent und darf nicht werfen.
            limiter.Dispose();

            // Nach Dispose ist die Instanz unbenutzbar — WaitAsync muss werfen,
            // damit Konsumenten den Shutdown-Fehler sofort bemerken statt auf einen
            // disposeten Semaphore zu warten.
            _ = await Assert.ThrowsAsync<ObjectDisposedException>(
                () => limiter.WaitAsync("disposed.host"));
        }
    }
}

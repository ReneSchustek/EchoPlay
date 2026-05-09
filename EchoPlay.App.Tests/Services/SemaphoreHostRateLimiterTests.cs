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
            await limiter.WaitAsync("test.host", ct: TestContext.Current.CancellationToken);
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

            await limiter.WaitAsync("slow.host", ct: TestContext.Current.CancellationToken);

            Stopwatch sw = Stopwatch.StartNew();
            await limiter.WaitAsync("slow.host", ct: TestContext.Current.CancellationToken);
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

            await limiter.WaitAsync("host-a", ct: TestContext.Current.CancellationToken);

            // Host-B wurde noch nie aufgerufen — darf sofort zurückkehren
            Stopwatch sw = Stopwatch.StartNew();
            await limiter.WaitAsync("host-b", ct: TestContext.Current.CancellationToken);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 200);
        }

        [Fact]
        public async Task WaitAsync_ForegroundGoesBeforeBackground()
        {
            // Host-Intervall bewusst lang: der erste Background-Call setzt den Zeitstempel,
            // der nächste Background-Call muss das volle Intervall abwarten. In dieses
            // Zeitfenster schalten wir eine Foreground-Anfrage — sie darf sofort durch,
            // der Background-Call hinter ihr wartet zusätzlich, bis der Foreground-Slot
            // wieder frei ist.
            TimeSpan interval = TimeSpan.FromMilliseconds(300);
            SemaphoreHostRateLimiter limiter = new(new Dictionary<string, TimeSpan>
            {
                ["mixed.host"] = interval
            });

            // Erster Aufruf: setzt den letzten Aufruf-Zeitstempel sofort.
            await limiter.WaitAsync("mixed.host", CoverFetchPriority.Background, ct: TestContext.Current.CancellationToken);

            // Foreground-Anfrage im Hintergrund starten; sie darf wegen des Intervalls
            // erst nach ca. 300 ms zurückkehren, blockiert aber während ihrer Laufzeit
            // jede Background-Anfrage.
            Stopwatch sw = Stopwatch.StartNew();
            Task<long> foregroundTask = Task.Run(async () =>
            {
                await limiter.WaitAsync("mixed.host", CoverFetchPriority.Foreground, ct: TestContext.Current.CancellationToken);
                return sw.ElapsedMilliseconds;
            }, cancellationToken: TestContext.Current.CancellationToken);

            // Kurzer Abstand, damit der Foreground-Call seinen Slot reserviert hat.
            await Task.Delay(30, cancellationToken: TestContext.Current.CancellationToken);

            Task<long> backgroundTask = Task.Run(async () =>
            {
                await limiter.WaitAsync("mixed.host", CoverFetchPriority.Background, ct: TestContext.Current.CancellationToken);
                return sw.ElapsedMilliseconds;
            }, cancellationToken: TestContext.Current.CancellationToken);

            long foregroundMs = await foregroundTask;
            long backgroundMs = await backgroundTask;
            sw.Stop();

            Assert.True(foregroundMs < backgroundMs,
                $"Foreground muss vor Background zurückkehren — tatsächlich FG={foregroundMs} ms, BG={backgroundMs} ms.");
        }

        [Fact]
        public async Task Dispose_ReleasesSemaphores_AndBlocksFurtherWaitAsync()
        {
            SemaphoreHostRateLimiter limiter = new(new Dictionary<string, TimeSpan>
            {
                ["disposed.host"] = TimeSpan.FromMilliseconds(100)
            });

            // Ersten Aufruf durchlaufen, damit intern ein SemaphoreSlim angelegt wird.
            await limiter.WaitAsync("disposed.host", ct: TestContext.Current.CancellationToken);

            limiter.Dispose();

            // Ein zweiter Dispose ist idempotent und darf nicht werfen.
            limiter.Dispose();

            // Nach Dispose ist die Instanz unbenutzbar — WaitAsync muss werfen,
            // damit Konsumenten den Shutdown-Fehler sofort bemerken statt auf einen
            // disposeten Semaphore zu warten.
            _ = await Assert.ThrowsAsync<ObjectDisposedException>(
                () => limiter.WaitAsync("disposed.host", ct: TestContext.Current.CancellationToken));
        }
    }
}

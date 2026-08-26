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
            // Zwei verschiedene Hosts, damit wirklich der Vorrang geprüft wird und nicht die
            // Reihenfolge am gemeinsamen Semaphore: Die Background-Anfrage geht auf einen Host,
            // der noch nie aufgerufen wurde und kein Intervall kennt — sie liefe also sofort
            // durch. Sie muss trotzdem warten, solange eine Foreground-Anfrage im Flug ist.
            SemaphoreHostRateLimiter limiter = new(new Dictionary<string, TimeSpan>
            {
                ["foreground.host"] = TimeSpan.FromMilliseconds(300),
                ["background.host"] = TimeSpan.Zero
            });

            // Setzt den Zeitstempel: Die nächste Anfrage an diesen Host wartet das volle
            // Intervall ab und hält den Vorrang so lange offen.
            await limiter.WaitAsync("foreground.host", CoverFetchPriority.Foreground, ct: TestContext.Current.CancellationToken);

            // Direkt aufrufen und nicht über Task.Run: WaitAsync zählt den Foreground-Vorrang
            // synchron hoch, bevor es das erste Mal wartet. Damit steht die Reservierung fest,
            // sobald der Aufruf zurückkehrt — eine über Task.Run gestartete Anfrage kann unter
            // Last später anlaufen, und dann sieht die Background-Anfrage keinen Vorrang.
            Task foreground = limiter.WaitAsync("foreground.host", CoverFetchPriority.Foreground, ct: TestContext.Current.CancellationToken);
            Task background = limiter.WaitAsync("background.host", CoverFetchPriority.Background, ct: TestContext.Current.CancellationToken);

            // Deutlich vor Ablauf des Foreground-Intervalls nachsehen: Ohne Vorrangregel wäre
            // die Background-Anfrage zu diesem Zeitpunkt längst durch.
            await Task.Delay(100, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(background.IsCompleted,
                "Die Background-Anfrage darf nicht durchlaufen, solange eine Foreground-Anfrage wartet.");

            await foreground;
            await background;
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

        [Fact]
        public async Task WaitAsync_ForAnImageHost_UsesTheShortIntervalOfItsSuffix()
        {
            // Bilder holt man bei einem Auslieferungsnetz, nicht bei einer API: Dort gibt es
            // kein Kontingent zu schonen. Mit dem Standardabstand von einer Sekunde erschien
            // das fünfzehnte Cover einer Trefferseite erst nach fünfzehn Sekunden.
            SemaphoreHostRateLimiter limiter = new(
                new Dictionary<string, TimeSpan> { ["itunes.apple.com"] = TimeSpan.FromSeconds(5) },
                defaultInterval: TimeSpan.FromSeconds(5),
                suffixIntervals: new Dictionary<string, TimeSpan> { [".mzstatic.com"] = TimeSpan.FromMilliseconds(20) });

            await limiter.WaitAsync("is1-ssl.mzstatic.com", ct: TestContext.Current.CancellationToken);

            Stopwatch sw = Stopwatch.StartNew();
            await limiter.WaitAsync("is1-ssl.mzstatic.com", ct: TestContext.Current.CancellationToken);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 500, $"Erwartet < 500 ms, tatsächlich {sw.ElapsedMilliseconds} ms");
        }

        [Fact]
        public async Task WaitAsync_ForAnApiHost_IgnoresTheImageSuffixes()
        {
            // Der exakte Eintrag schlägt die Endung — sonst würde eine großzügige Regel für
            // Bildhosts versehentlich auch die API entfesseln.
            SemaphoreHostRateLimiter limiter = new(
                new Dictionary<string, TimeSpan> { ["itunes.apple.com"] = TimeSpan.FromMilliseconds(300) },
                defaultInterval: TimeSpan.FromMilliseconds(10),
                suffixIntervals: new Dictionary<string, TimeSpan> { [".com"] = TimeSpan.FromMilliseconds(10) });

            await limiter.WaitAsync("itunes.apple.com", ct: TestContext.Current.CancellationToken);

            Stopwatch sw = Stopwatch.StartNew();
            await limiter.WaitAsync("itunes.apple.com", ct: TestContext.Current.CancellationToken);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds >= 250, $"Erwartet >= 250 ms, tatsächlich {sw.ElapsedMilliseconds} ms");
        }

        [Fact]
        public async Task WaitAsync_ForAHostWithoutAnyRule_UsesTheDefaultInterval()
        {
            SemaphoreHostRateLimiter limiter = new(
                new Dictionary<string, TimeSpan>(),
                defaultInterval: TimeSpan.FromMilliseconds(300),
                suffixIntervals: new Dictionary<string, TimeSpan> { [".mzstatic.com"] = TimeSpan.FromMilliseconds(10) });

            await limiter.WaitAsync("beispiel.test", ct: TestContext.Current.CancellationToken);

            Stopwatch sw = Stopwatch.StartNew();
            await limiter.WaitAsync("beispiel.test", ct: TestContext.Current.CancellationToken);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds >= 250, $"Erwartet >= 250 ms, tatsächlich {sw.ElapsedMilliseconds} ms");
        }
    }
}

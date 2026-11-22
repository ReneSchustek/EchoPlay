using EchoPlay.App.Services;
using Microsoft.Extensions.Time.Testing;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="SemaphoreHostRateLimiter"/>.
    /// Prüft das Einhalten des Minimum-Intervalls zwischen aufeinanderfolgenden Aufrufen.
    /// </summary>
    public sealed class SemaphoreHostRateLimiterTests
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public void WaitAsync_FirstCall_ReturnsImmediately()
        {
            FakeTimeProvider clock = new();
            SemaphoreHostRateLimiter limiter = new(
                new Dictionary<string, TimeSpan> { ["test.host"] = TimeSpan.FromSeconds(1) },
                timeProvider: clock);

            Assert.True(limiter.WaitAsync("test.host", ct: Ct).IsCompletedSuccessfully);
        }

        [Fact]
        public async Task WaitAsync_IntervalAlreadyPassedOnTheClock_DoesNotWait()
        {
            // Zehn Minuten Abstand, die auf der Uhr schon vergangen sind: Wartete die Drossel nach
            // der Wanduhr statt nach ihrer Zeitquelle, hinge der zweite Aufruf zehn Minuten.
            FakeTimeProvider clock = new();
            SemaphoreHostRateLimiter limiter = new(
                new Dictionary<string, TimeSpan> { ["slow.host"] = TimeSpan.FromMinutes(10) },
                timeProvider: clock);
            await limiter.WaitAsync("slow.host", ct: Ct);

            clock.Advance(TimeSpan.FromMinutes(11));
            Task second = limiter.WaitAsync("slow.host", ct: Ct);

            Assert.True(second.IsCompletedSuccessfully);
            await second;
        }

        [Fact]
        public async Task WaitAsync_SecondCallTooFast_WaitsExactlyTheRemainingInterval()
        {
            FakeTimeProvider clock = new();
            SemaphoreHostRateLimiter limiter = new(
                new Dictionary<string, TimeSpan> { ["slow.host"] = TimeSpan.FromMilliseconds(300) },
                timeProvider: clock);
            await limiter.WaitAsync("slow.host", ct: Ct);
            clock.Advance(TimeSpan.FromMilliseconds(100));

            Task second = limiter.WaitAsync("slow.host", ct: Ct);
            clock.Advance(TimeSpan.FromMilliseconds(199));
            Assert.False(second.IsCompleted);

            clock.Advance(TimeSpan.FromMilliseconds(1));
            await second;
        }

        [Fact]
        public async Task WaitAsync_DifferentHosts_AreIndependent()
        {
            FakeTimeProvider clock = new();
            SemaphoreHostRateLimiter limiter = new(
                new Dictionary<string, TimeSpan>
                {
                    ["host-a"] = TimeSpan.FromSeconds(5),
                    ["host-b"] = TimeSpan.FromSeconds(5),
                },
                timeProvider: clock);
            await limiter.WaitAsync("host-a", ct: Ct);

            // Host-B wurde noch nie aufgerufen und darf sofort zurückkehren, obwohl die Uhr steht.
            Assert.True(limiter.WaitAsync("host-b", ct: Ct).IsCompletedSuccessfully);
        }

        [Fact]
        public async Task WaitAsync_ForegroundGoesBeforeBackground()
        {
            // Zwei verschiedene Hosts, damit wirklich der Vorrang geprüft wird und nicht die
            // Reihenfolge am gemeinsamen Semaphore: Die Background-Anfrage geht auf einen Host,
            // der noch nie aufgerufen wurde und kein Intervall kennt, sie liefe also sofort
            // durch. Sie muss trotzdem warten, solange eine Foreground-Anfrage im Flug ist.
            FakeTimeProvider clock = new();
            SemaphoreHostRateLimiter limiter = new(
                new Dictionary<string, TimeSpan>
                {
                    ["foreground.host"] = TimeSpan.FromMilliseconds(300),
                    ["background.host"] = TimeSpan.Zero,
                },
                timeProvider: clock);
            await limiter.WaitAsync("foreground.host", CoverFetchPriority.Foreground, ct: Ct);

            // Direkt aufrufen und nicht über Task.Run: WaitAsync zählt den Foreground-Vorrang
            // synchron hoch, bevor es das erste Mal wartet.
            Task foreground = limiter.WaitAsync("foreground.host", CoverFetchPriority.Foreground, ct: Ct);
            Task background = limiter.WaitAsync("background.host", CoverFetchPriority.Background, ct: Ct);

            clock.Advance(TimeSpan.FromMilliseconds(299));
            Assert.False(background.IsCompleted,
                "Die Background-Anfrage darf nicht durchlaufen, solange eine Foreground-Anfrage wartet.");

            clock.Advance(TimeSpan.FromMilliseconds(1));
            await foreground;
            await AdvanceUntilAsync(clock, background);
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

            // Nach Dispose ist die Instanz unbenutzbar, WaitAsync muss werfen,
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
            FakeTimeProvider clock = new();
            SemaphoreHostRateLimiter limiter = new(
                new Dictionary<string, TimeSpan> { ["itunes.apple.com"] = TimeSpan.FromSeconds(5) },
                defaultInterval: TimeSpan.FromSeconds(5),
                suffixIntervals: new Dictionary<string, TimeSpan> { [".mzstatic.com"] = TimeSpan.FromMilliseconds(20) },
                timeProvider: clock);
            await limiter.WaitAsync("is1-ssl.mzstatic.com", ct: Ct);

            Task second = limiter.WaitAsync("is1-ssl.mzstatic.com", ct: Ct);
            clock.Advance(TimeSpan.FromMilliseconds(20));

            await second;
        }

        [Fact]
        public async Task WaitAsync_ForAnApiHost_IgnoresTheImageSuffixes()
        {
            // Der exakte Eintrag schlägt die Endung, sonst würde eine großzügige Regel für
            // Bildhosts versehentlich auch die API entfesseln.
            FakeTimeProvider clock = new();
            SemaphoreHostRateLimiter limiter = new(
                new Dictionary<string, TimeSpan> { ["itunes.apple.com"] = TimeSpan.FromMilliseconds(300) },
                defaultInterval: TimeSpan.FromMilliseconds(10),
                suffixIntervals: new Dictionary<string, TimeSpan> { [".com"] = TimeSpan.FromMilliseconds(10) },
                timeProvider: clock);
            await limiter.WaitAsync("itunes.apple.com", ct: Ct);

            Task second = limiter.WaitAsync("itunes.apple.com", ct: Ct);
            clock.Advance(TimeSpan.FromMilliseconds(299));
            Assert.False(second.IsCompleted);

            clock.Advance(TimeSpan.FromMilliseconds(1));
            await second;
        }

        [Fact]
        public async Task WaitAsync_ForAHostWithoutAnyRule_UsesTheDefaultInterval()
        {
            FakeTimeProvider clock = new();
            SemaphoreHostRateLimiter limiter = new(
                new Dictionary<string, TimeSpan>(),
                defaultInterval: TimeSpan.FromMilliseconds(300),
                suffixIntervals: new Dictionary<string, TimeSpan> { [".mzstatic.com"] = TimeSpan.FromMilliseconds(10) },
                timeProvider: clock);
            await limiter.WaitAsync("beispiel.test", ct: Ct);

            Task second = limiter.WaitAsync("beispiel.test", ct: Ct);
            clock.Advance(TimeSpan.FromMilliseconds(299));
            Assert.False(second.IsCompleted);

            clock.Advance(TimeSpan.FromMilliseconds(1));
            await second;
        }

        /// <summary>
        /// Stellt die Uhr in Schritten des Abfragetakts vor, bis die Anfrage durch ist. Die
        /// Background-Schleife legt ihre nächste Wartezeit erst an, wenn die vorige abgelaufen
        /// ist; ein einzelner Sprung träfe deshalb unter Umständen ins Leere.
        /// </summary>
        private static async Task AdvanceUntilAsync(FakeTimeProvider clock, Task pending)
        {
            for (int step = 0; step < 200 && !pending.IsCompleted; step++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(25));
                await Task.Yield();
            }

            await pending;
        }
    }
}

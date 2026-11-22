using Microsoft.Extensions.Time.Testing;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Dreht eine künstliche Uhr, bis eine Aufgabe fertig ist, und misst dabei die
    /// verstrichene künstliche Zeit. So lassen sich Zeitgrenzen der HTTP-Pipeline prüfen,
    /// ohne ihre Sekunden wirklich abzuwarten.
    /// </summary>
    internal static class ClockDriver
    {
        /// <summary>
        /// Spielraum nach oben: Zwischen dem Auslösen einer Grenze und dem Ende der Aufgabe
        /// liegen ein paar Fortsetzungen auf dem Thread-Pool, in denen die Uhr weiterläuft.
        /// </summary>
        public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(1);

        // Fein genug, um eine Grenze auf eine Viertelsekunde zu treffen, grob genug, dass
        // dreißig Sekunden in rund 120 Schritten vergehen.
        private static readonly TimeSpan ClockStep = TimeSpan.FromMilliseconds(250);

        // Echte Wartezeit je Schritt, damit Fortsetzungen nach einem Zeitsprung laufen können.
        private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(5);

        // Abbruch, falls keine Grenze greift; deutlich über jeder Gesamtgrenze der Anwendung.
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(2);

        /// <summary>Künstliche Uhr mit festem Startpunkt.</summary>
        public static FakeTimeProvider CreateClock() => new(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));

        /// <summary>
        /// Wartet auf <paramref name="started"/> und dreht die Uhr danach schrittweise, bis
        /// <paramref name="work"/> fertig ist.
        /// </summary>
        /// <param name="time">Die Uhr, die die Pipeline aus dem Container liest.</param>
        /// <param name="work">Die laufende Anfrage.</param>
        /// <param name="started">
        /// Erfüllt, sobald der erste Versuch läuft. Erst dann stehen die Zeitgeber der
        /// Pipeline, und kein Sprung der Uhr geht an ihnen vorbei.
        /// </param>
        /// <returns>Die künstliche Zeit vom Start bis zum Ende der Aufgabe.</returns>
        public static async Task<TimeSpan> RunUntilCompletedAsync(FakeTimeProvider time, Task work, Task started)
        {
            DateTimeOffset start = time.GetUtcNow();
            await started.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            while (!work.IsCompleted && time.GetUtcNow() - start < GiveUpAfter)
            {
                time.Advance(ClockStep);
                await work.WaitAsync(SettleTime, TestContext.Current.CancellationToken)
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            return time.GetUtcNow() - start;
        }
    }
}

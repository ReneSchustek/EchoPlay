namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Dreht eine künstliche Uhr, bis eine Anfrage gegen einen hängenden Server aufgibt, und
    /// misst dabei die verstrichene künstliche Zeit. So lassen sich Zeitgrenzen der HTTP-Pipeline
    /// prüfen, ohne ihre Sekunden wirklich abzuwarten.
    /// </summary>
    /// <remarks>
    /// Die Uhr läuft nur weiter, wenn die Pipeline auf sie wartet. Löst ein Zeitgeber aus, rechnet
    /// die Pipeline erst und wartet dann wieder: auf die Pause vor dem nächsten Versuch oder auf
    /// den Server. Ein Treiber, der in dieser Zeit weiterdreht, misst die Geschwindigkeit des
    /// Rechners mit; im kalten Lauf, wenn der Wiederholungspfad erst übersetzt wird, lief so die
    /// Gesamtgrenze ab, bevor der zweite Versuch ankam.
    /// </remarks>
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

        // Echte Obergrenze für das Warten auf die Pipeline nach einem Auslösen. Greift sie, dreht
        // die Uhr trotzdem weiter; der Test scheitert dann an seiner Prüfung, nicht am Hängen.
        private static readonly TimeSpan PipelineSettleLimit = TimeSpan.FromSeconds(10);

        // Abbruch, falls keine Grenze greift; deutlich über jeder Gesamtgrenze der Anwendung.
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(2);

        /// <summary>Künstliche Uhr mit festem Startpunkt.</summary>
        public static ObservedClock CreateClock() => new(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));

        /// <summary>
        /// Wartet auf den ersten Versuch und dreht die Uhr danach schrittweise, bis
        /// <paramref name="work"/> fertig ist.
        /// </summary>
        /// <param name="time">Die Uhr, die die Pipeline aus dem Container liest.</param>
        /// <param name="work">Die laufende Anfrage.</param>
        /// <param name="server">Der hängende Server; er zeigt, ob gerade ein Versuch wartet.</param>
        /// <returns>Die künstliche Zeit vom Start bis zum Ende der Aufgabe.</returns>
        public static async Task<TimeSpan> RunUntilCompletedAsync(ObservedClock time, Task work, HangingHttpHandler server)
        {
            DateTimeOffset start = time.GetUtcNow();
            await server.FirstAttempt.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            while (!work.IsCompleted && time.GetUtcNow() - start < GiveUpAfter)
            {
                bool attemptWasWaiting = server.InFlight > 0;
                long firedBefore = time.FiredCount;
                long armedBefore = time.ArmedCount;
                int attemptsBefore = server.Attempts;

                time.Advance(ClockStep);

                if (time.FiredCount > firedBefore)
                {
                    await WaitForPipelineAsync(time, work, server, attemptWasWaiting, armedBefore, attemptsBefore);
                }
                else
                {
                    await work.WaitAsync(SettleTime, TestContext.Current.CancellationToken)
                        .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
            }

            return time.GetUtcNow() - start;
        }

        /// <summary>
        /// Wartet nach einem ausgelösten Zeitgeber, bis die Pipeline wieder auf die Uhr wartet.
        /// Lief gerade ein Versuch, war es dessen Frist: Als Nächstes stellt die Pipeline die Pause
        /// vor der Wiederholung. Lief keiner, war es die Pause: Als Nächstes kommt der Versuch am
        /// Server an. Das Stellen seiner Frist reicht dann nicht, denn bis zum Server ist es von dort
        /// noch ein Stück Pipeline.
        /// </summary>
        private static async Task WaitForPipelineAsync(
            ObservedClock time, Task work, HangingHttpHandler server, bool attemptWasWaiting, long armedBefore, int attemptsBefore)
        {
            Func<bool> pipelineWaitsAgain = attemptWasWaiting
                ? () => time.ArmedCount > armedBefore || server.Attempts > attemptsBefore
                : () => server.Attempts > attemptsBefore;

            DateTime giveUp = DateTime.UtcNow + PipelineSettleLimit;
            while (!work.IsCompleted && !pipelineWaitsAgain() && DateTime.UtcNow < giveUp)
            {
                await Task.Delay(1, TestContext.Current.CancellationToken);
            }
        }
    }
}

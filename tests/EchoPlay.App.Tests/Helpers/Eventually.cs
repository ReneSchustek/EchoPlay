using System.Diagnostics;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Wartet auf eine Bedingung, die ein Hintergrundablauf herbeiführt. Gefragt wird in kurzen
    /// Abständen; nach fünf Sekunden Echtzeit gibt der Test mit klarer Meldung auf, statt zu
    /// hängen. Die Frist misst die Stoppuhr, nicht die Wanduhr, damit eine Zeitumstellung sie
    /// nicht verschiebt.
    /// </summary>
    internal static class Eventually
    {
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

        /// <summary>Wartet, bis die Bedingung erfüllt ist.</summary>
        /// <param name="condition">Die Bedingung.</param>
        /// <param name="step">
        /// Läuft vor jeder erneuten Abfrage, etwa um eine vorgestellte Uhr weiterzudrehen;
        /// darf fehlen.
        /// </param>
        public static async Task TrueAsync(Func<bool> condition, Action? step = null)
        {
            long start = Stopwatch.GetTimestamp();
            while (!condition())
            {
                Assert.True(Stopwatch.GetElapsedTime(start) < GiveUpAfter, "Die Bedingung trat nicht ein.");
                step?.Invoke();
                await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
            }
        }
    }
}

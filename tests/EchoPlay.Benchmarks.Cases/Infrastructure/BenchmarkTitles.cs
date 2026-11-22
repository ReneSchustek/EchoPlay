using System.Globalization;

namespace EchoPlay.Benchmarks.Cases.Infrastructure
{
    /// <summary>
    /// Folgentitel in den Formen, die Anbieter und lokale Ordner liefern („Folge 170: …“,
    /// „Die drei ??? 001/…“, Umlaute, Gedankenstriche), so viele, wie der Bestand Folgen hat.
    /// </summary>
    internal static class BenchmarkTitles
    {
        private static readonly string[] Forms =
        [
            "Folge {0}: und der Schatz im Dunkeln",
            "Die drei ??? {0:000}/und das weiße Auge",
            "Fünf Freunde - Folge {0} - Gefährliches Eis",
            "{0:000} - Die Insel der Abenteuer (Hörspiel)",
            "TKKG Junior – Folge {0}: Spuk im Schloss"
        ];

        /// <summary>Erzeugt die Titel.</summary>
        /// <returns><see cref="LibraryShape.EpisodeCount"/> Titel in wechselnder Form.</returns>
        public static string[] Create() =>
        [
            .. Enumerable.Range(1, LibraryShape.EpisodeCount)
                .Select(i => string.Format(CultureInfo.InvariantCulture, Forms[i % Forms.Length], (i % LibraryShape.LargestSeriesEpisodes) + 1))
        ];
    }
}

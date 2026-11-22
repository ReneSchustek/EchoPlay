using BenchmarkDotNet.Attributes;
using EchoPlay.Core.Parsing;
using EchoPlay.Core.Scoring;

namespace EchoPlay.Benchmarks.Cases.Benchmarks
{
    /// <summary>
    /// Die Textarbeit, die bei Suche, Import und Cover-Zuordnung über jeden Titel läuft:
    /// Normalisieren, Folgennummer lesen, Cover-Treffer bewerten.
    /// </summary>
    /// <remarks>
    /// Die Titel kommen aus <see cref="Infrastructure.BenchmarkTitles"/>, so viele, wie der Bestand
    /// Folgen hat.
    /// </remarks>
    [MemoryDiagnoser]
    public class TextBenchmarks
    {
        private string[] _titles = [];

        /// <summary>Erzeugt die Titel einmal je Messlauf.</summary>
        [GlobalSetup]
        public void CreateTitles() => _titles = Infrastructure.BenchmarkTitles.Create();

        /// <summary>Normalisiert jeden Titel für Vergleich und Suche.</summary>
        /// <returns>Gesamtlänge der Ergebnisse, damit nichts wegoptimiert wird.</returns>
        [Benchmark]
        public int Normalize()
        {
            int length = 0;
            foreach (string title in _titles)
            {
                length += HoerspielTextNormalizer.Normalize(title).Length;
            }

            return length;
        }

        /// <summary>Liest die Folgennummer aus jedem Titel.</summary>
        /// <returns>Anzahl der erkannten Nummern.</returns>
        [Benchmark]
        public int ExtractEpisodeNumbers()
        {
            int found = 0;
            foreach (string title in _titles)
            {
                if (EpisodeNumberParser.Extract(title).HasValue)
                {
                    found++;
                }
            }

            return found > 0 ? found : throw new InvalidOperationException("Keine Folgennummer erkannt: Die Titel passen nicht zum Parser.");
        }

        /// <summary>Bewertet jeden Titel als Cover-Treffer zu Serie und Folge.</summary>
        /// <returns>Summe der Punkte.</returns>
        [Benchmark]
        public int ScoreCoverCandidates()
        {
            int score = 0;
            foreach (string title in _titles)
            {
                score += CoverRelevanceScorer.CalculateScore(title, "Die drei ???", 170, "und der Schatz im Dunkeln");
            }

            return score;
        }
    }
}

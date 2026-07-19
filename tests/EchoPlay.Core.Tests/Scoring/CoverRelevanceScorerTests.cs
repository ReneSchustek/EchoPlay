using EchoPlay.Core.Scoring;

namespace EchoPlay.Core.Tests.Scoring
{
    /// <summary>
    /// Tests für <see cref="CoverRelevanceScorer"/>.
    /// Stellt sicher, dass nur relevante Cover-Ergebnisse den Mindest-Score erreichen
    /// und irrelevante Treffer (anderer Künstler, andere Serie) zuverlässig gefiltert werden.
    /// </summary>
    public sealed class CoverRelevanceScorerTests
    {
        [Fact]
        public void CalculateScore_WithSeriesAndNumber_ReturnsHighScore()
        {
            // „Die drei ??? Kids - Folge 42" enthält Serienname + Nummer
            int score = CoverRelevanceScorer.CalculateScore(
                "Die drei ??? Kids - Folge 42 - Internetpiraten",
                "Die drei ??? Kids",
                42,
                "Internetpiraten");

            Assert.Equal(100, score);
        }

        [Fact]
        public void CalculateScore_WithSeriesNameOnly_ReturnsMediumScore()
        {
            // Nur Serienname, keine Nummer und kein Folgentitel im Ergebnis
            int score = CoverRelevanceScorer.CalculateScore(
                "Die drei ??? Kids - Best Of",
                "Die drei ??? Kids",
                42,
                "Internetpiraten");

            Assert.Equal(50, score);
        }

        [Fact]
        public void CalculateScore_WithSeriesAndEpisodeTitle_Returns70()
        {
            // Serienname + Folgentitel, aber keine Nummer
            int score = CoverRelevanceScorer.CalculateScore(
                "Die drei ??? Kids - Internetpiraten",
                "Die drei ??? Kids",
                42,
                "Internetpiraten");

            Assert.Equal(70, score);
        }

        [Fact]
        public void CalculateScore_IrrelevantArtist_ReturnsZero()
        {
            // „Mimi Rutherford" hat nichts mit „Die drei ??? Kids" zu tun
            int score = CoverRelevanceScorer.CalculateScore(
                "Mimi Rutherford - Summer Vibes",
                "Die drei ??? Kids",
                42,
                "Internetpiraten");

            Assert.Equal(0, score);
        }

        [Fact]
        public void CalculateScore_NullReleaseTitle_ReturnsZero()
        {
            int score = CoverRelevanceScorer.CalculateScore(
                null, "Die drei ??? Kids", 42, "Internetpiraten");

            Assert.Equal(0, score);
        }

        [Fact]
        public void CalculateScore_EmptySeriesName_ReturnsZero()
        {
            int score = CoverRelevanceScorer.CalculateScore(
                "Die drei ??? Kids - Folge 42", "", 42, "Internetpiraten");

            Assert.Equal(0, score);
        }

        [Fact]
        public void CalculateScore_WithoutEpisodeNumber_IgnoresNumberPoints()
        {
            // Kein episodeNumber → Nummern-Bonus entfällt
            int score = CoverRelevanceScorer.CalculateScore(
                "Die drei ??? Kids - Internetpiraten",
                "Die drei ??? Kids",
                null,
                "Internetpiraten");

            Assert.Equal(70, score);
        }

        [Fact]
        public void CalculateScore_ShortEpisodeTitle_IgnoresEpisodeTitlePoints()
        {
            // Sehr kurzer Folgentitel (≤3 Zeichen) ist zu generisch, wird ignoriert
            int score = CoverRelevanceScorer.CalculateScore(
                "Die drei ??? Kids - 42 - Eis",
                "Die drei ??? Kids",
                42,
                "Eis");

            // 50 (Serienname) + 30 (Nummer) = 80, Folgentitel zu kurz
            Assert.Equal(80, score);
        }

        [Fact]
        public void CalculateScore_CaseInsensitive_MatchesCorrectly()
        {
            // Groß-/Kleinschreibung darf keine Rolle spielen
            int score = CoverRelevanceScorer.CalculateScore(
                "DIE DREI ??? KIDS - FOLGE 42",
                "Die drei ??? Kids",
                42,
                null);

            Assert.Equal(80, score);
        }

        [Fact]
        public void CalculateScore_WrongSeriesVariant_ReturnsBelowThreshold()
        {
            // „Die drei ???" (ohne Kids) bei Suche nach „Die drei ??? Kids":
            // Serienname „die drei  kids" ist NICHT in „die drei  folge 42" enthalten → 0.
            // Aber Nummer 42 ist im Titel enthalten → 30.
            // Score 30 liegt unter MinimumThreshold (50) → wird von FindBestMatch verworfen.
            int score = CoverRelevanceScorer.CalculateScore(
                "Die drei ??? - Folge 42",
                "Die drei ??? Kids",
                42,
                null);

            Assert.Equal(30, score);
            Assert.True(score < CoverRelevanceScorer.MinimumThreshold);
        }

        [Fact]
        public void CalculateScore_MaxScore_CappedAt100()
        {
            // Alle Kriterien erfüllt: 50 + 30 + 20 = 100
            int score = CoverRelevanceScorer.CalculateScore(
                "TKKG - 42 - Vorsicht Bissig",
                "TKKG",
                42,
                "Vorsicht Bissig");

            Assert.Equal(100, score);
        }

        [Fact]
        public void MinimumThreshold_Is50()
        {
            // Mindest-Schwelle muss dem Seriennamen-Score entsprechen
            Assert.Equal(50, CoverRelevanceScorer.MinimumThreshold);
        }
    }
}

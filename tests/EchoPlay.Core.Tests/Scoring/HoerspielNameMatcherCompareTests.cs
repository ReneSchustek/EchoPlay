using EchoPlay.Core.Scoring;

namespace EchoPlay.Core.Tests.Scoring
{
    /// <summary>
    /// Tests für <see cref="HoerspielNameMatcher.Compare"/>: die drei Namensmerkmale, die Apple Music
    /// und Spotify gleich bewerten.
    /// </summary>
    public sealed class HoerspielNameMatcherCompareTests
    {
        private static readonly Dictionary<string, string> NumberWords = new()
        {
            ["3"] = "drei",
            ["5"] = "fünf",
            ["7"] = "sieben"
        };

        [Fact]
        public void Compare_NameContainsQueryAsWord_SetsContainsAndExactWord()
        {
            HoerspielNameMatch match = HoerspielNameMatcher.Compare("Die drei ??? Kids", "drei", NumberWords);

            Assert.True(match.ContainsQuery);
            Assert.True(match.ExactWordMatch);
            Assert.False(match.NumberVariantMatch);
        }

        [Fact]
        public void Compare_QueryWithDigit_MatchesWrittenNumber()
        {
            // "3" im Suchbegriff passt auf "drei" im Namen, nicht aber als Teilstring.
            HoerspielNameMatch match = HoerspielNameMatcher.Compare("Die drei ???", "Die 3", NumberWords);

            Assert.True(match.NumberVariantMatch);
            Assert.False(match.ContainsQuery);
        }

        [Fact]
        public void Compare_DigitQueryAgainstNumberWordWithUmlaut_MatchesVariant()
        {
            // Beim Normalisieren verliert "fünf" den Umlaut; die Zuordnung muss dieselbe Form vergleichen.
            HoerspielNameMatch match = HoerspielNameMatcher.Compare("Fünf Freunde", "5 Freunde", NumberWords);

            Assert.True(match.NumberVariantMatch);
            Assert.False(match.ContainsQuery);
        }

        [Fact]
        public void Compare_NumberWordWithUmlautAgainstDigitName_MatchesVariant()
        {
            HoerspielNameMatch match = HoerspielNameMatcher.Compare("5 Freunde", "Fünf Freunde", NumberWords);

            Assert.True(match.NumberVariantMatch);
            Assert.False(match.ContainsQuery);
        }

        [Fact]
        public void HasNumberVariantMatch_NormalizedNumberWordWithUmlaut_MatchesDigit()
        {
            // Die Vorauswahl von Apple Music ruft diesen Weg mit bereits normalisierten Texten auf.
            bool match = HoerspielNameMatcher.HasNumberVariantMatch(
                HoerspielTextNormalizer.Normalize("5 Freunde"),
                HoerspielTextNormalizer.Normalize("Fünf Freunde"),
                NumberWords);

            Assert.True(match);
        }

        [Fact]
        public void Compare_QueryOnlyPartOfAWord_IsNoExactWordMatch()
        {
            HoerspielNameMatch match = HoerspielNameMatcher.Compare("Hanni und Nanni", "Hann", NumberWords);

            Assert.True(match.ContainsQuery);
            Assert.False(match.ExactWordMatch);
        }

        [Fact]
        public void Compare_CaseAndUmlautsDiffer_StillMatches()
        {
            // Beide Seiten werden normalisiert: Groß-/Kleinschreibung und Umlaute zählen nicht.
            HoerspielNameMatch match = HoerspielNameMatcher.Compare("Die Schule der magischen Tiere", "SCHULE DER MAGISCHEN", NumberWords);

            Assert.True(match.ContainsQuery);
            Assert.True(match.ExactWordMatch);
        }

        [Fact]
        public void Compare_UnrelatedName_MatchesNothing()
        {
            HoerspielNameMatch match = HoerspielNameMatcher.Compare("Benjamin Blümchen", "TKKG", NumberWords);

            Assert.Equal(new HoerspielNameMatch(false, false, false), match);
        }

        [Fact]
        public void Compare_NullName_Throws()
        {
            _ = Assert.Throws<ArgumentNullException>(() => HoerspielNameMatcher.Compare(null!, "x", NumberWords));
        }

        [Fact]
        public void Compare_NullQuery_Throws()
        {
            _ = Assert.Throws<ArgumentNullException>(() => HoerspielNameMatcher.Compare("x", null!, NumberWords));
        }
    }
}

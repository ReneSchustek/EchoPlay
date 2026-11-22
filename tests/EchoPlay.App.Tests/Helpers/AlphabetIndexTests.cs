using EchoPlay.App.Helpers;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Sichert die Zuordnung Titel → Sprungbuchstabe ab.
    /// Der wichtigste Fall steht in <see cref="BucketOf_LeadingArticle_FollowsTheTitleNotTheArticle"/>:
    /// Die Leiste muss der vorhandenen Sortierung folgen, sonst zeigt ein Sprung ins Leere.
    /// </summary>
    public sealed class AlphabetIndexTests
    {
        [Theory]
        [InlineData("Benjamin Blümchen", 'B')]
        [InlineData("TKKG", 'T')]
        [InlineData("hui buh", 'H')]
        public void BucketOf_PlainTitle_UsesFirstLetter(string title, char expected) =>
            Assert.Equal(expected, AlphabetIndex.BucketOf(title));

        /// <summary>
        /// <c>SeriesDataService</c> sortiert nach <c>Title</c> und trennt keine Artikel ab.
        /// „Die drei ???" steht in der Liste deshalb unter D, und die Leiste muss dorthin
        /// zeigen, nicht unter T.
        /// </summary>
        [Theory]
        [InlineData("Die drei ???", 'D')]
        [InlineData("Das Wunder von Bern", 'D')]
        [InlineData("Der Herr der Ringe", 'D')]
        public void BucketOf_LeadingArticle_FollowsTheTitleNotTheArticle(string title, char expected) =>
            Assert.Equal(expected, AlphabetIndex.BucketOf(title));

        [Theory]
        [InlineData("Ärger im Paradies", 'A')]
        [InlineData("Öl für die Lampen", 'O')]
        [InlineData("Über den Wolken", 'U')]
        [InlineData("Élodie", 'E')]
        public void BucketOf_Diacritics_MapToBaseLetter(string title, char expected) =>
            Assert.Equal(expected, AlphabetIndex.BucketOf(title));

        [Theory]
        [InlineData("ßeltsam", 'S')]
        public void BucketOf_SharpS_MapsToS(string title, char expected) =>
            Assert.Equal(expected, AlphabetIndex.BucketOf(title));

        [Theory]
        [InlineData("5 Freunde", '#')]
        [InlineData("...und dann?", '#')]
        [InlineData("¡Hola!", '#')]
        public void BucketOf_DigitsAndSymbols_GoToOtherBucket(string title, char expected) =>
            Assert.Equal(expected, AlphabetIndex.BucketOf(title));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void BucketOf_EmptyTitle_GoesToOtherBucket(string? title) =>
            Assert.Equal('#', AlphabetIndex.BucketOf(title));

        /// <summary>
        /// Führende Leerzeichen dürfen einen Titel nicht ins Sammelfach schieben, sonst
        /// verschwindet ein Eintrag aus dem Buchstaben, unter dem er sichtbar steht.
        /// </summary>
        [Fact]
        public void BucketOf_LeadingWhitespace_IsIgnored() =>
            Assert.Equal('B', AlphabetIndex.BucketOf("  Bibi Blocksberg"));

        [Fact]
        public void BucketOf_IsCaseInsensitive() =>
            Assert.Equal(AlphabetIndex.BucketOf("tkkg"), AlphabetIndex.BucketOf("TKKG"));

        /// <summary>
        /// Die Leiste führt immer alle 27 Fächer. Eine Leiste, die ihre Breite je nach Bestand
        /// ändert, ist kein verlässlicher Anlaufpunkt.
        /// </summary>
        [Fact]
        public void Buckets_ContainAToZPlusOtherBucket()
        {
            Assert.Equal(27, AlphabetIndex.Buckets.Count);
            Assert.Equal('A', AlphabetIndex.Buckets[0]);
            Assert.Equal('Z', AlphabetIndex.Buckets[25]);
            Assert.Equal('#', AlphabetIndex.Buckets[26]);
        }

        /// <summary>
        /// Jeder Titel muss in genau einem Fach landen, das die Leiste auch anbietet, sonst
        /// gäbe es Einträge, die über kein Fach erreichbar sind.
        /// </summary>
        [Theory]
        [InlineData("Die drei ???")]
        [InlineData("Ärger")]
        [InlineData("5 Freunde")]
        [InlineData("")]
        public void BucketOf_AlwaysReturnsAnOfferedBucket(string? title) =>
            Assert.Contains(AlphabetIndex.BucketOf(title), AlphabetIndex.Buckets);

        [Fact]
        public void OccupiedBuckets_ReportsOnlyLettersThatOccur()
        {
            List<string?> collection = ["Bibi Blocksberg", "Benjamin Blümchen", "5 Freunde", "Ärger"];

            IReadOnlySet<char> occupied = AlphabetIndex.OccupiedBuckets(collection);

            Assert.Equal(['#', 'A', 'B'], occupied.OrderBy(c => c));
        }

        [Fact]
        public void TitleOrder_PutsEveryTitleIntoTheSectionItsLetterPromises()
        {
            List<string> titles = ["Zorro", "5 Freunde", "Ärger im Paradies", "hui buh", "Ahoi", "Benjamin Blümchen", "Öl für die Lampen"];

            List<string> sorted = [.. titles.OrderBy(t => t, AlphabetIndex.TitleOrder)];

            Assert.Equal(
                ["Ahoi", "Ärger im Paradies", "Benjamin Blümchen", "hui buh", "Öl für die Lampen", "Zorro", "5 Freunde"],
                sorted);

            // Jeder Buchstabe bildet genau einen zusammenhängenden Abschnitt, in der Reihenfolge der Leiste.
            List<char> sections = [.. sorted.Select(AlphabetIndex.BucketOf).Distinct()];
            Assert.Equal(['A', 'B', 'H', 'O', 'Z', '#'], sections);
            Assert.Equal(sections.Count, sorted.Select(AlphabetIndex.BucketOf).Where((b, i) => i == 0 || b != AlphabetIndex.BucketOf(sorted[i - 1])).Count());
        }

        [Fact]
        public void OccupiedBuckets_EmptyLibrary_ReportsNothing() =>
            Assert.Empty(AlphabetIndex.OccupiedBuckets([]));
    }
}

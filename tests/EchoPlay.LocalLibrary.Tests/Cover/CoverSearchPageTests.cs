using EchoPlay.LocalLibrary.Cover;

namespace EchoPlay.LocalLibrary.Tests.Cover
{
    /// <summary>
    /// Die Umrechnung Seite → Anbieter-Abschnitt. Sie liegt an einer Stelle, weil die Anbieter
    /// unterschiedlich blättern: MusicBrainz kennt <c>offset</c>, Deezer <c>index</c>, die
    /// iTunes Search API gar nichts.
    /// </summary>
    public sealed class CoverSearchPageTests
    {
        [Fact]
        public void FirstPage_HasNoOffsetAndNoDiscard()
        {
            CoverSearchWindow window = CoverSearchPage.First.ToWindow(pageSize: 9, supportsOffset: true);

            Assert.Equal(9, window.Limit);
            Assert.Equal(0, window.Offset);
            Assert.Equal(0, window.SkipLocally);
        }

        [Theory]
        [InlineData(1, 9, 0)]
        [InlineData(2, 18, 0)]
        public void WithOffset_TheLimitStaysConstant(int page, int expectedOffset, int expectedDiscard)
        {
            CoverSearchWindow window = new CoverSearchPage(page).ToWindow(pageSize: 9, supportsOffset: true);

            // Eine echte Folgeabfrage: gleiche Grenze, verschobener Anfang, nichts zu verwerfen.
            Assert.Equal(9, window.Limit);
            Assert.Equal(expectedOffset, window.Offset);
            Assert.Equal(expectedDiscard, window.SkipLocally);
        }

        [Theory]
        [InlineData(0, 9, 0)]
        [InlineData(1, 18, 9)]
        [InlineData(2, 27, 18)]
        public void WithoutOffset_MoreIsFetchedAndTheStartDiscarded(int page, int expectedLimit, int expectedDiscard)
        {
            CoverSearchWindow window = new CoverSearchPage(page).ToWindow(pageSize: 9, supportsOffset: false);

            // Die iTunes Search API kennt nur limit. Kostet Datenvolumen, ist aber die einzige
            // Variante, die dort überhaupt weiterblättert.
            Assert.Equal(expectedLimit, window.Limit);
            Assert.Equal(0, window.Offset);
            Assert.Equal(expectedDiscard, window.SkipLocally);
        }

        [Fact]
        public void SmallerPageSize_AffectsBothDirections()
        {
            // Deezer-Künstler liefern sechs Treffer, nicht neun.
            CoverSearchWindow withOffset = new CoverSearchPage(2).ToWindow(pageSize: 6, supportsOffset: true);
            CoverSearchWindow withoutOffset = new CoverSearchPage(2).ToWindow(pageSize: 6, supportsOffset: false);

            Assert.Equal((6, 12, 0), (withOffset.Limit, withOffset.Offset, withOffset.SkipLocally));
            Assert.Equal((18, 0, 12), (withoutOffset.Limit, withoutOffset.Offset, withoutOffset.SkipLocally));
        }

        [Fact]
        public void Next_CountsThePageUp()
        {
            Assert.Equal(1, CoverSearchPage.First.Next.Index);
            Assert.Equal(2, CoverSearchPage.First.Next.Next.Index);
        }

        [Fact]
        public void PageSizeZero_IsRejected()
        {
            // Ohne Prüfung wäre die Grenze 0: eine Anfrage, die nie etwas liefert.
            _ = Assert.Throws<ArgumentOutOfRangeException>(
                () => CoverSearchPage.First.ToWindow(pageSize: 0, supportsOffset: true));
        }
    }
}

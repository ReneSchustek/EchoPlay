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
        public void ErsteSeite_IstOhneVersatzUndOhneVerwerfen()
        {
            CoverSearchWindow window = CoverSearchPage.First.ToWindow(pageSize: 9, supportsOffset: true);

            Assert.Equal(9, window.Limit);
            Assert.Equal(0, window.Offset);
            Assert.Equal(0, window.SkipLocally);
        }

        [Theory]
        [InlineData(1, 9, 0)]
        [InlineData(2, 18, 0)]
        public void MitVersatz_BleibtDieGrenzeKonstant(int seite, int erwarteterOffset, int erwartetesVerwerfen)
        {
            CoverSearchWindow window = new CoverSearchPage(seite).ToWindow(pageSize: 9, supportsOffset: true);

            // Eine echte Folgeabfrage: gleiche Grenze, verschobener Anfang, nichts zu verwerfen.
            Assert.Equal(9, window.Limit);
            Assert.Equal(erwarteterOffset, window.Offset);
            Assert.Equal(erwartetesVerwerfen, window.SkipLocally);
        }

        [Theory]
        [InlineData(0, 9, 0)]
        [InlineData(1, 18, 9)]
        [InlineData(2, 27, 18)]
        public void OhneVersatz_WirdMehrGeholtUndDerAnfangVerworfen(int seite, int erwarteteGrenze, int erwartetesVerwerfen)
        {
            CoverSearchWindow window = new CoverSearchPage(seite).ToWindow(pageSize: 9, supportsOffset: false);

            // Die iTunes Search API kennt nur limit. Kostet Datenvolumen, ist aber die einzige
            // Variante, die dort überhaupt weiterblättert.
            Assert.Equal(erwarteteGrenze, window.Limit);
            Assert.Equal(0, window.Offset);
            Assert.Equal(erwartetesVerwerfen, window.SkipLocally);
        }

        [Fact]
        public void KleinereSeitengroesse_WirktSichAufBeideRichtungenAus()
        {
            // Deezer-Künstler liefern sechs Treffer, nicht neun.
            CoverSearchWindow mitVersatz = new CoverSearchPage(2).ToWindow(pageSize: 6, supportsOffset: true);
            CoverSearchWindow ohneVersatz = new CoverSearchPage(2).ToWindow(pageSize: 6, supportsOffset: false);

            Assert.Equal((6, 12, 0), (mitVersatz.Limit, mitVersatz.Offset, mitVersatz.SkipLocally));
            Assert.Equal((18, 0, 12), (ohneVersatz.Limit, ohneVersatz.Offset, ohneVersatz.SkipLocally));
        }

        [Fact]
        public void Next_ZaehltDieSeiteHoch()
        {
            Assert.Equal(1, CoverSearchPage.First.Next.Index);
            Assert.Equal(2, CoverSearchPage.First.Next.Next.Index);
        }

        [Fact]
        public void SeitengroesseNull_WirdAbgewiesen()
        {
            // Ohne Guard wäre die Division im Discogs-URL-Aufbau ein Divide-by-zero und die
            // Grenze 0 — eine Anfrage, die nie etwas liefert.
            _ = Assert.Throws<ArgumentOutOfRangeException>(
                () => CoverSearchPage.First.ToWindow(pageSize: 0, supportsOffset: true));
        }
    }
}

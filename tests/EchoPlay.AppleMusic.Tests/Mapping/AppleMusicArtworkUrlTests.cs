using EchoPlay.AppleMusic.Mapping;

namespace EchoPlay.AppleMusic.Tests.Mapping
{
    /// <summary>
    /// Tests der Größenanpassung von iTunes-Cover-Adressen.
    /// </summary>
    /// <remarks>
    /// Die Suchantwort liefert nur ein Bild mit 100 Pixeln Kantenlänge. Auf einer Kachel von
    /// 160 Pixeln ist das sichtbar unscharf. Die Größe steht im Pfad und lässt sich ohne
    /// eine weitere Anfrage austauschen.
    /// </remarks>
    public sealed class AppleMusicArtworkUrlTests
    {
        [Fact]
        public void WithSize_ForASearchResponseUrl_RaisesTheResolution()
        {
            Uri? result = AppleMusicArtworkUrl.WithSize(
                new Uri("https://is1-ssl.mzstatic.com/image/thumb/Music/abc/100x100bb.jpg"));

            Assert.Equal(
                new Uri("https://is1-ssl.mzstatic.com/image/thumb/Music/abc/300x300bb.jpg"),
                result);
        }

        [Fact]
        public void WithSize_ForAPngWithoutSuffix_KeepsFormatAndSuffix()
        {
            Uri? result = AppleMusicArtworkUrl.WithSize(
                new Uri("https://is5-ssl.mzstatic.com/image/thumb/xyz/60x60.png"), 200);

            Assert.Equal(new Uri("https://is5-ssl.mzstatic.com/image/thumb/xyz/200x200.png"), result);
        }

        [Fact]
        public void WithSize_WithoutASizeInThePath_ReturnsTheUrlUnchanged()
        {
            // Eine geratene Umschrift führte zu einer Adresse, hinter der kein Bild liegt;
            // dann lieber das kleine Bild als gar keines.
            Uri url = new("https://example.com/cover");

            Assert.Equal(url, AppleMusicArtworkUrl.WithSize(url));
        }

        [Fact]
        public void WithSize_WithoutAnUrl_ReturnsNull()
        {
            // Unbrauchbare Zeichenketten weist WebAddress schon an der Grenze ab; hier kommt
            // höchstens null an.
            Assert.Null(AppleMusicArtworkUrl.WithSize(null));
        }

        [Fact]
        public void WithSize_WithASizeOfZero_Throws()
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(
                () => AppleMusicArtworkUrl.WithSize(new Uri("https://example.com/100x100bb.jpg"), 0));
        }
    }
}

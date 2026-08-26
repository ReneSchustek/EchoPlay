using EchoPlay.AppleMusic.Mapping;

namespace EchoPlay.AppleMusic.Tests.Mapping
{
    /// <summary>
    /// Tests der Größenanpassung von iTunes-Cover-Adressen.
    /// </summary>
    /// <remarks>
    /// Die Suchantwort liefert nur ein Bild mit 100 Pixeln Kantenlänge. Auf einer Kachel von
    /// 160 Pixeln ist das sichtbar unscharf — die Größe steht im Pfad und lässt sich ohne
    /// eine weitere Anfrage austauschen.
    /// </remarks>
    public sealed class AppleMusicArtworkUrlTests
    {
        [Fact]
        public void WithSize_ForASearchResponseUrl_RaisesTheResolution()
        {
            string? result = AppleMusicArtworkUrl.WithSize(
                "https://is1-ssl.mzstatic.com/image/thumb/Music/abc/100x100bb.jpg");

            Assert.Equal(
                "https://is1-ssl.mzstatic.com/image/thumb/Music/abc/300x300bb.jpg",
                result);
        }

        [Fact]
        public void WithSize_ForAPngWithoutSuffix_KeepsFormatAndSuffix()
        {
            string? result = AppleMusicArtworkUrl.WithSize(
                "https://is5-ssl.mzstatic.com/image/thumb/xyz/60x60.png", 200);

            Assert.Equal("https://is5-ssl.mzstatic.com/image/thumb/xyz/200x200.png", result);
        }

        [Fact]
        public void WithSize_WithoutASizeInThePath_ReturnsTheUrlUnchanged()
        {
            // Eine geratene Umschrift führte zu einer Adresse, hinter der kein Bild liegt —
            // dann lieber das kleine Bild als gar keines.
            const string url = "https://example.com/cover";

            Assert.Equal(url, AppleMusicArtworkUrl.WithSize(url));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "Der Prüffall gibt bewusst Werte hinein, die keine Adresse sind — als Uri ließen sie sich gar nicht ausdrücken.")]
        public void WithSize_WithoutAnUrl_ReturnsNull(string? url)
        {
            Assert.Null(AppleMusicArtworkUrl.WithSize(url));
        }

        [Fact]
        public void WithSize_WithASizeOfZero_Throws()
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(
                () => AppleMusicArtworkUrl.WithSize("https://example.com/100x100bb.jpg", 0));
        }
    }
}

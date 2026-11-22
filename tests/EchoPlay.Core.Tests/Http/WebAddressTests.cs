using EchoPlay.Core.Http;

namespace EchoPlay.Core.Tests.Http
{
    /// <summary>
    /// Prüft <see cref="WebAddress.TryParse"/>: Nur absolute Web-Adressen kommen durch, alles
    /// andere wird zu <see langword="null"/> statt zu einer Ausnahme.
    /// </summary>
    public sealed class WebAddressTests
    {
        [Theory]
        [InlineData("https://is1-ssl.mzstatic.com/image/thumb/100x100bb.jpg")]
        [InlineData("http://coverartarchive.org/release/abc/front")]
        [InlineData("HTTPS://open.spotify.com/album/1")]
        public void TryParse_WithAnAbsoluteWebAddress_ReturnsIt(string value)
        {
            Uri? address = WebAddress.TryParse(value);

            Assert.NotNull(address);
            Assert.True(address.IsAbsoluteUri);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(@"Cover\folder.jpg")]
        [InlineData("/image/thumb.jpg")]
        [InlineData("keine Adresse")]
        [InlineData(@"C:\Hörspiele\cover.jpg")]
        [InlineData("file:///C:/Windows/System32/calc.exe")]
        [InlineData("spotify:album:1")]
        [InlineData("ftp://example.org/cover.jpg")]
        public void TryParse_WithAnythingElse_ReturnsNull(string? value)
        {
            Assert.Null(WebAddress.TryParse(value));
        }
    }
}

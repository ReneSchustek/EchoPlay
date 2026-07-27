using EchoPlay.App.Helpers;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Tests für <see cref="SpotifyAlbumLink"/>. Prüfen ausschließlich den Linkbau —
    /// geöffnet wird nichts (siehe Arbeitspaket 437: kein echter Prozessstart aus Tests).
    /// </summary>
    public sealed class SpotifyAlbumLinkTests
    {
        [Fact]
        public void TryBuild_ValidId_ReturnsWebLink()
        {
            bool ok = SpotifyAlbumLink.TryBuild("4aawyAB9vmqN3uQ7FjRGTy", out string? url);

            Assert.True(ok);
            Assert.Equal("https://open.spotify.com/album/4aawyAB9vmqN3uQ7FjRGTy", url);
        }

        [Fact]
        public void TryBuild_TrimsSurroundingWhitespace()
        {
            bool ok = SpotifyAlbumLink.TryBuild("  4aawyAB9vmqN3uQ7FjRGTy  ", out string? url);

            Assert.True(ok);
            Assert.Equal("https://open.spotify.com/album/4aawyAB9vmqN3uQ7FjRGTy", url);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("zu-kurz")]
        [InlineData("4aawyAB9vmqN3uQ7FjRGTyZUVIEL")]
        public void TryBuild_InvalidId_ReturnsFalse(string? id)
        {
            Assert.False(SpotifyAlbumLink.TryBuild(id, out string? url));
            Assert.Null(url);
        }

        [Theory]
        [InlineData("../../etc/passwd12345678")]
        [InlineData("4aawyAB9vmqN3uQ7Fj?x=1")]
        [InlineData("4aawyAB9vmqN3uQ7Fj/../x")]
        public void TryBuild_IdWithPathOrQueryCharacters_IsRejected(string id)
        {
            // Die ID landet ungeprüft im Pfad einer URL — alles außer Base62 muss draußen bleiben.
            Assert.False(SpotifyAlbumLink.TryBuild(id, out _));
        }
    }
}

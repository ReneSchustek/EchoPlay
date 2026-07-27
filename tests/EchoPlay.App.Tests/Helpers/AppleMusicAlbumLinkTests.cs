using EchoPlay.App.Helpers;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Tests für <see cref="AppleMusicAlbumLink"/>. Prüfen ausschließlich den Linkbau —
    /// geöffnet wird nichts (siehe Arbeitspaket 437: kein echter Prozessstart aus Tests).
    /// </summary>
    public sealed class AppleMusicAlbumLinkTests
    {
        [Fact]
        public void TryBuild_ValidId_ReturnsLinkWithStorefront()
        {
            // Die Storefront im Pfad ist Pflicht: ohne sie antwortet Apple mit 404.
            bool ok = AppleMusicAlbumLink.TryBuild("1001105149", out string? url);

            Assert.True(ok);
            Assert.Equal("https://music.apple.com/de/album/1001105149", url);
        }

        [Fact]
        public void TryBuild_TrimsSurroundingWhitespace()
        {
            bool ok = AppleMusicAlbumLink.TryBuild("  1001105149  ", out string? url);

            Assert.True(ok);
            Assert.Equal("https://music.apple.com/de/album/1001105149", url);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("123")]
        [InlineData("4aawyAB9vmqN3uQ7FjRGTy")]
        public void TryBuild_InvalidId_ReturnsFalse(string? id)
        {
            Assert.False(AppleMusicAlbumLink.TryBuild(id, out string? url));
            Assert.Null(url);
        }

        [Theory]
        [InlineData("../../1001105149")]
        [InlineData("1001105149?x=1")]
        [InlineData("1001105149/../y")]
        public void TryBuild_IdWithPathOrQueryCharacters_IsRejected(string id)
        {
            Assert.False(AppleMusicAlbumLink.TryBuild(id, out _));
        }
    }
}

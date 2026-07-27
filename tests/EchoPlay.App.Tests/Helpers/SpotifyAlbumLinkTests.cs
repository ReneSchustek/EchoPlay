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

        [Fact]
        public void TrySearch_BuildsEncodedSearchLink()
        {
            bool ok = SpotifyAlbumLink.TrySearch(["Benjamin Blümchen", "Folge 1: Hallo, Lea!"], out string? url);

            Assert.True(ok);
            Assert.NotNull(url);
            Assert.StartsWith("https://open.spotify.com/search/", url, StringComparison.Ordinal);
            Assert.DoesNotContain(' ', url!);
            Assert.Contains("Bl%C3%BCmchen", url!, StringComparison.Ordinal);
        }

        [Fact]
        public void TrySearch_SkipsEmptyTerms()
        {
            bool ok = SpotifyAlbumLink.TrySearch([null, "   ", "TKKG"], out string? url);

            Assert.True(ok);
            Assert.Equal("https://open.spotify.com/search/TKKG", url);
        }

        [Fact]
        public void TrySearch_TermsCannotEscapeThePath()
        {
            // Ein Serientitel mit Schrägstrich oder Fragezeichen darf die URL nicht umbiegen.
            bool ok = SpotifyAlbumLink.TrySearch(["../../evil?x=1"], out string? url);

            Assert.True(ok);
            Assert.Equal("https://open.spotify.com/search/..%2F..%2Fevil%3Fx%3D1", url);
        }

        [Fact]
        public void TrySearch_NoUsableTerms_ReturnsFalse()
        {
            Assert.False(SpotifyAlbumLink.TrySearch([null, "  "], out string? url));
            Assert.Null(url);
        }

        [Fact]
        public void TryBuildAppUri_ValidId_ReturnsSpotifyScheme()
        {
            // Die App-URI öffnet die installierte Spotify-App, in der der Nutzer angemeldet ist.
            bool ok = SpotifyAlbumLink.TryBuildAppUri("4aawyAB9vmqN3uQ7FjRGTy", out string? uri);

            Assert.True(ok);
            Assert.Equal("spotify:album:4aawyAB9vmqN3uQ7FjRGTy", uri);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("kaputt")]
        [InlineData("../../etc/passwd12345678")]
        public void TryBuildAppUri_InvalidId_ReturnsFalse(string? id)
        {
            Assert.False(SpotifyAlbumLink.TryBuildAppUri(id, out _));
        }

        [Fact]
        public void TrySearchAppUri_UsesEncodedTerms()
        {
            bool ok = SpotifyAlbumLink.TrySearchAppUri(["Benjamin Blümchen", "Folge 1"], out string? uri);

            Assert.True(ok);
            Assert.StartsWith("spotify:search:", uri, StringComparison.Ordinal);
            Assert.DoesNotContain(' ', uri!);
            Assert.Contains("Bl%C3%BCmchen", uri!, StringComparison.Ordinal);
        }

        [Fact]
        public void TrySearchAppUri_NoUsableTerms_ReturnsFalse()
        {
            Assert.False(SpotifyAlbumLink.TrySearchAppUri([null, " "], out _));
        }
    }
}

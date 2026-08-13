using EchoPlay.App.Services;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für die Anbieter-Auswahl aus <see cref="PlaybackLauncher"/>.
    /// Geprüft wird ausschließlich, welche Ziele entstehen — geöffnet wird hier nichts:
    /// Ein Test startet weder Browser noch fremde Anwendung.
    /// </summary>
    public sealed class PlaybackLauncherTargetTests
    {
        [Fact]
        public void TryBuildSpotifyTargets_WithAlbumId_PointsToAlbum()
        {
            bool erfolg = PlaybackLauncher.TryBuildSpotifyTargets(
                "4aawyAB9vmqN3uQ7FjRGTy",
                "Fünf Freunde",
                "Folge 17",
                out string? appUri,
                out string? webUrl);

            Assert.True(erfolg);
            Assert.Contains("4aawyAB9vmqN3uQ7FjRGTy", appUri, StringComparison.Ordinal);
            Assert.Contains("4aawyAB9vmqN3uQ7FjRGTy", webUrl, StringComparison.Ordinal);
        }

        [Fact]
        public void TryBuildSpotifyTargets_WithoutAlbumId_FallsBackToSearch()
        {
            // Ohne Kennung bleibt die Suche - besser als eine Abweisung, denn die Folge gibt es.
            bool erfolg = PlaybackLauncher.TryBuildSpotifyTargets(
                null,
                "Fünf Freunde",
                "Folge 17",
                out string? appUri,
                out string? webUrl);

            Assert.True(erfolg);
            Assert.NotNull(webUrl);
            Assert.Contains("search", webUrl, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(appUri);
        }

        [Fact]
        public void ProviderOrder_ExactMatchBeatsSearch()
        {
            // Der Fall, an dem die erste Fassung scheiterte: „Fünf Freunde Junior, Folge 17"
            // trägt keine Spotify-Kennung, aber eine von Apple Music. Eine unscharfe
            // Spotify-Suche darf den genauen Treffer nicht verdrängen.
            bool spotifyGenau = PlaybackLauncher.TryBuildSpotifyTargets(
                spotifyAlbumId: null, string.Empty, string.Empty, out _, out string? ohneTitel);

            Assert.False(spotifyGenau);
            Assert.Null(ohneTitel);

            // Mit Titeln entsteht sehr wohl ein Ziel - deshalb muss die Suche hinter den
            // genauen Treffern stehen und nicht davor.
            bool spotifySuche = PlaybackLauncher.TryBuildSpotifyTargets(
                spotifyAlbumId: null, "Fünf Freunde Junior", "Folge 17", out _, out string? mitTitel);

            Assert.True(spotifySuche);
            Assert.NotNull(mitTitel);
        }

        [Fact]
        public void TryBuildSpotifyTargets_WithoutAnything_YieldsNoTarget()
        {
            // Weder Kennung noch Titel: Hier gibt es nichts zu öffnen, und der Aufrufer muss
            // den Hinweis zeigen statt ins Leere zu springen.
            bool erfolg = PlaybackLauncher.TryBuildSpotifyTargets(
                null,
                string.Empty,
                string.Empty,
                out string? appUri,
                out string? webUrl);

            Assert.False(erfolg);
            Assert.Null(appUri);
            Assert.Null(webUrl);
        }
    }
}

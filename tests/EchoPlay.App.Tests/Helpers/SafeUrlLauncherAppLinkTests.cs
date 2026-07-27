using EchoPlay.App.Helpers;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Tests für den Schema-Wächter in <see cref="SafeUrlLauncher.TryOpenAppLink"/>.
    /// Geprüft werden nur die Ablehnungspfade — sie kehren zurück, bevor irgendein Programm
    /// gestartet wird (siehe Arbeitspaket 437: kein echter Prozessstart aus Tests).
    /// </summary>
    public sealed class SafeUrlLauncherAppLinkTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("kein-uri")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Testparameter spiegelt die string-Signatur der geprueften Methode; ein Uri-Objekt koennte die ungueltigen Eingaben gar nicht darstellen.")]
        public void TryOpenAppLink_InvalidUri_ReturnsFalse(string? uri)
        {
            Assert.False(SafeUrlLauncher.TryOpenAppLink(uri, "spotify"));
        }

        [Theory]
        [InlineData("https://open.spotify.com/album/4aawyAB9vmqN3uQ7FjRGTy")]
        [InlineData("file:///C:/Windows/System32/calc.exe")]
        [InlineData("itunes:album:123")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Testparameter spiegelt die string-Signatur der geprueften Methode.")]
        public void TryOpenAppLink_ForeignScheme_IsRejected(string uri)
        {
            // Nur das erwartete Schema darf durch — sonst startet ein fremdes Protokoll
            // ein beliebiges Programm.
            Assert.False(SafeUrlLauncher.TryOpenAppLink(uri, "spotify"));
        }

        [Fact]
        public void TryOpenAppLink_EmptyExpectedScheme_ReturnsFalse()
        {
            Assert.False(SafeUrlLauncher.TryOpenAppLink("spotify:album:4aawyAB9vmqN3uQ7FjRGTy", ""));
        }
    }
}

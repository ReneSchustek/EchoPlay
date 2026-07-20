using EchoPlay.App.Helpers;
using Xunit;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Tests für <see cref="SafeUrlLauncher"/>. Geprüft werden ausschließlich die
    /// Ablehnungs-Pfade — sie kehren vor dem eigentlichen <c>Process.Start</c> zurück
    /// und starten daher keinen Browser. Der Erfolgsfall (http/https) würde real den
    /// Standard-Browser öffnen und ist deshalb nicht als Unit-Test abbildbar.
    /// </summary>
    public sealed class SafeUrlLauncherTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("notaurl")]
        [InlineData("/relativer/pfad")]
        [InlineData("file:///C:/Windows/System32/calc.exe")]
        [InlineData("ftp://example.com/datei")]
        [InlineData("javascript:alert(1)")]
        [InlineData("ms-settings:")]
        [InlineData("mailto:foo@bar.de")]
        public void TryOpenInBrowser_ReturnsFalse_ForNonHttpSchemesAndInvalidInput(string? candidate)
        {
            bool opened = SafeUrlLauncher.TryOpenInBrowser(candidate);

            Assert.False(opened);
        }
    }
}

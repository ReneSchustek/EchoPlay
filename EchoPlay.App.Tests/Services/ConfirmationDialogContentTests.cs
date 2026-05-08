using EchoPlay.App.Services;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests fuer den Build-Helfer von <see cref="ConfirmationDialogContent"/>.
    /// SafeResourceLoader liefert in Test-Hosts den Fallback "(missing: key)" oder
    /// den Schluessel selbst, was hier ausreicht — geprueft wird die Strukturierung,
    /// nicht der konkrete Lokalisierungs-Wert.
    /// </summary>
    public sealed class ConfirmationDialogContentTests
    {
        [Fact]
        public void Build_PassesTitleAndMessageThrough()
        {
            ConfirmationDialogContent content = ConfirmationDialogContent.Build("Loeschen?", "Wirklich loeschen?");

            Assert.Equal("Loeschen?", content.Title);
            Assert.Equal("Wirklich loeschen?", content.Message);
        }

        [Fact]
        public void Build_PrimaryButtonText_IsNonEmpty()
        {
            ConfirmationDialogContent content = ConfirmationDialogContent.Build("T", "M");

            Assert.False(string.IsNullOrEmpty(content.PrimaryButtonText));
        }

        [Fact]
        public void Build_CloseButtonText_IsNonEmpty()
        {
            ConfirmationDialogContent content = ConfirmationDialogContent.Build("T", "M");

            Assert.False(string.IsNullOrEmpty(content.CloseButtonText));
        }
    }
}

using EchoPlay.App.Services;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für den Build-Helfer von <see cref="ErrorDialogContent"/>.
    /// </summary>
    public sealed class ErrorDialogContentTests
    {
        [Fact]
        public void Build_PassesTitleAndMessageThrough()
        {
            ErrorDialogContent content = ErrorDialogContent.Build("Fehler", "Etwas ist schiefgelaufen.");

            Assert.Equal("Fehler", content.Title);
            Assert.Equal("Etwas ist schiefgelaufen.", content.Message);
        }

        [Fact]
        public void Build_DefaultCloseButtonText_IsOk()
        {
            ErrorDialogContent content = ErrorDialogContent.Build("T", "M");

            Assert.Equal("OK", content.CloseButtonText);
        }

        [Fact]
        public void Record_CustomCloseButtonText_OverridesDefault()
        {
            ErrorDialogContent content = new("T", "M", "Schliessen");

            Assert.Equal("Schliessen", content.CloseButtonText);
        }
    }
}

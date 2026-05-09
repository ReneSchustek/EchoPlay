using EchoPlay.App.Services;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests fuer den Defense-in-Depth-Pfad in <see cref="ErrorDialogService"/>:
    /// bei Pre-MainWindow-Szenarien (XamlRoot null) darf der Service nicht crashen.
    /// </summary>
    public sealed class ErrorDialogServiceTests
    {
        [Fact]
        public async Task ShowAsync_NullXamlRoot_DoesNotThrow()
        {
            ErrorDialogService service = new(static () => null);

            await service.ShowAsync("Fehler", "Test-Nachricht", TestContext.Current.CancellationToken);

            // Wenn ShowAsync ohne Exception zurueckkehrt, ist der Trace-Fallback gegriffen.
            // Dialog-Anzeige selbst ist UI-Code und im Test-Host nicht erreichbar.
            Assert.True(true);
        }

        [Fact]
        public void Constructor_NullProvider_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<System.ArgumentNullException>(() => new ErrorDialogService(null!));
        }
    }
}

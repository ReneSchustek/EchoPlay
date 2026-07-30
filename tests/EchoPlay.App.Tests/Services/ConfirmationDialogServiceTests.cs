using EchoPlay.App.Services;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für den Defense-in-Depth-Pfad in <see cref="ConfirmationDialogService"/>:
    /// bei Pre-MainWindow-Szenarien (XamlRoot null) darf der Service nicht crashen —
    /// vorher stand dort ein null-forgiving <c>App.MainWindow!</c>.
    /// </summary>
    public sealed class ConfirmationDialogServiceTests
    {
        [Fact]
        public async Task ConfirmAsync_NullXamlRoot_LiefertFalseStattNRE()
        {
            ConfirmationDialogService service = new(static () => null);

            bool confirmed = await service.ConfirmAsync(
                "Löschen?", "Wirklich löschen?", TestContext.Current.CancellationToken);

            // Eine Rückfrage, die niemand sehen konnte, ist keine Zustimmung. Alle Aufrufer
            // brechen bei false ab — das ist die sichere Seite.
            Assert.False(confirmed);
        }

        [Fact]
        public void Constructor_NullProvider_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<System.ArgumentNullException>(() => new ConfirmationDialogService(null!));
        }
    }
}

using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Models;
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
            ConfirmationDialogService service = new(static () => null, new FakeDialogSuppressionService());

            bool confirmed = await service.ConfirmAsync(
                "Löschen?", "Wirklich löschen?", DialogKey.SeriesDeleteFromDisk, TestContext.Current.CancellationToken);

            // Eine Rückfrage, die niemand sehen konnte, ist keine Zustimmung. Alle Aufrufer
            // brechen bei false ab — das ist die sichere Seite.
            Assert.False(confirmed);
        }

        [Fact]
        public async Task ConfirmAsync_AusgeblendeteRueckfrage_LiefertJaOhneDialog()
        {
            // Gemerkt wird nur ein „Ja". Die ausgeblendete Rückfrage ist deshalb
            // gleichbedeutend mit Zustimmung — und sie darf keinen Dialog mehr bauen.
            FakeDialogSuppressionService suppression = new();
            suppression.Preset(DialogKey.SeriesRemoveFromLibrary);

            int xamlRootLookups = 0;
            ConfirmationDialogService service = new(
                () => { xamlRootLookups++; return null; },
                suppression);

            bool confirmed = await service.ConfirmAsync(
                "Entfernen?", "Serie entfernen?", DialogKey.SeriesRemoveFromLibrary,
                TestContext.Current.CancellationToken);

            Assert.True(confirmed);
            Assert.Equal(0, xamlRootLookups);
        }

        [Fact]
        public void Constructor_NullProvider_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<System.ArgumentNullException>(
                () => new ConfirmationDialogService(null!, new FakeDialogSuppressionService()));
        }

        [Fact]
        public void Constructor_NullSuppression_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<System.ArgumentNullException>(
                () => new ConfirmationDialogService(static () => null, null!));
        }
    }
}

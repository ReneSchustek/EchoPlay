using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Models;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für den Defense-in-Depth-Pfad in <see cref="ErrorDialogService"/>:
    /// bei Pre-MainWindow-Szenarien (XamlRoot null) darf der Service nicht crashen.
    /// </summary>
    public sealed class ErrorDialogServiceTests
    {
        [Fact]
        public async Task ShowAsync_NullXamlRoot_DoesNotThrow()
        {
            ErrorDialogService service = new(static () => null, new FakeDialogSuppressionService());

            await service.ShowAsync("Fehler", "Test-Nachricht", DialogKey.ImportFailed, TestContext.Current.CancellationToken);

            // Wenn ShowAsync ohne Exception zurückkehrt, ist der Trace-Fallback gegriffen.
            // Dialog-Anzeige selbst ist UI-Code und im Test-Host nicht erreichbar.
            Assert.True(true);
        }

        [Fact]
        public async Task ShowAsync_AusgeblendeterHinweis_BautKeinenDialog()
        {
            FakeDialogSuppressionService suppression = new();
            suppression.Preset(DialogKey.OfflineModeSearchHint);

            int xamlRootLookups = 0;
            ErrorDialogService service = new(
                () => { xamlRootLookups++; return null; },
                suppression);

            await service.ShowAsync(
                "Offline", "Keine Suche im Offline-Modus", DialogKey.OfflineModeSearchHint,
                TestContext.Current.CancellationToken);

            Assert.Equal(0, xamlRootLookups);
        }

        [Fact]
        public async Task ShowAlwaysAsync_FragtDenMerkzettelGarNichtErstAb()
        {
            // Letzte Verteidigungslinie: unbehandelte Fehler und der fehlgeschlagene Start.
            // Ein einmal gesetztes Häkchen würde dort jede künftige Störung verschlucken.
            FakeDialogSuppressionService suppression = new();
            ErrorDialogService service = new(static () => null, suppression);

            await service.ShowAlwaysAsync(
                "Unerwarteter Fehler", "Etwas ist schiefgegangen", TestContext.Current.CancellationToken);

            Assert.Equal(0, suppression.IsSuppressedCallCount);
        }

        [Fact]
        public void Constructor_NullProvider_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<System.ArgumentNullException>(
                () => new ErrorDialogService(null!, new FakeDialogSuppressionService()));
        }

        [Fact]
        public void Constructor_NullSuppression_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<System.ArgumentNullException>(
                () => new ErrorDialogService(static () => null, null!));
        }
    }
}

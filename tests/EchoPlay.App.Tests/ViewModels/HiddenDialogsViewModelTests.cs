using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Models;
using Microsoft.UI.Xaml;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für die Liste der ausgeblendeten Hinweise in den Einstellungen.
    /// </summary>
    public sealed class HiddenDialogsViewModelTests
    {
        private static (HiddenDialogsViewModel ViewModel, FakeDialogSuppressionService Suppression) Build()
        {
            FakeDialogSuppressionService suppression = new();
            HiddenDialogsViewModel viewModel = new(
                suppression,
                new FakeLocalizationService(),
                new FakeLoggerFactory());

            return (viewModel, suppression);
        }

        [Fact]
        public async Task OhneEintraege_ZeigtDenLeerenHinweisStattEinerLeerenListe()
        {
            (HiddenDialogsViewModel viewModel, _) = Build();

            await viewModel.LoadAsync();

            Assert.Empty(viewModel.Items);
            Assert.False(viewModel.HasEntries);
            Assert.Equal(Visibility.Visible, viewModel.EmptyHintVisibility);
            Assert.Equal(Visibility.Collapsed, viewModel.ListVisibility);
        }

        [Fact]
        public async Task MitEintraegen_ZeigtDieListe()
        {
            (HiddenDialogsViewModel viewModel, FakeDialogSuppressionService suppression) = Build();
            suppression.Preset(DialogKey.SeriesRemoveFromLibrary);
            suppression.Preset(DialogKey.ImportFailed);

            await viewModel.LoadAsync();

            Assert.Equal(2, viewModel.Items.Count);
            Assert.True(viewModel.HasEntries);
            Assert.Equal(Visibility.Visible, viewModel.ListVisibility);
            Assert.Equal(Visibility.Collapsed, viewModel.EmptyHintVisibility);
        }

        [Fact]
        public async Task JedeZeileTraegtDenLokalisiertenNamen()
        {
            (HiddenDialogsViewModel viewModel, FakeDialogSuppressionService suppression) = Build();
            suppression.Preset(DialogKey.LibraryReinit);

            await viewModel.LoadAsync();

            // Der Fake liefert den Schlüssel unverändert zurück — geprüft wird also, dass
            // der Name überhaupt über die Sprachdatei geht und nicht hart im Code steht.
            Assert.Equal("DialogName_LibraryReinit", viewModel.Items[0].DisplayName);
        }

        [Fact]
        public async Task Schreibaktionen_TragenDenWarnhinweis()
        {
            (HiddenDialogsViewModel viewModel, FakeDialogSuppressionService suppression) = Build();
            suppression.Preset(DialogKey.TagManagerRename);
            suppression.Preset(DialogKey.ImportFailed);

            await viewModel.LoadAsync();

            HiddenDialogItemViewModel rename = viewModel.Items.Single(item => item.Key == DialogKey.TagManagerRename);
            HiddenDialogItemViewModel importFailed = viewModel.Items.Single(item => item.Key == DialogKey.ImportFailed);

            Assert.True(rename.IsIrreversible);
            Assert.Equal(Visibility.Visible, rename.IrreversibleVisibility);
            Assert.False(importFailed.IsIrreversible);
            Assert.Equal(Visibility.Collapsed, importFailed.IrreversibleVisibility);
        }

        [Fact]
        public async Task RestoreAsync_NimmtGenauEineZeileAusDerListe()
        {
            (HiddenDialogsViewModel viewModel, FakeDialogSuppressionService suppression) = Build();
            suppression.Preset(DialogKey.LibraryReinit);
            suppression.Preset(DialogKey.ImportFailed);
            await viewModel.LoadAsync();

            await viewModel.RestoreAsync(DialogKey.LibraryReinit);

            HiddenDialogItemViewModel only = Assert.Single(viewModel.Items);
            Assert.Equal(DialogKey.ImportFailed, only.Key);
            Assert.Equal(1, suppression.RestoreCallCount);
        }

        [Fact]
        public async Task RestoreAllAsync_LeertDieListe()
        {
            (HiddenDialogsViewModel viewModel, FakeDialogSuppressionService suppression) = Build();
            suppression.Preset(DialogKey.LibraryReinit);
            suppression.Preset(DialogKey.ImportFailed);
            await viewModel.LoadAsync();

            await viewModel.RestoreAllAsync();

            Assert.Empty(viewModel.Items);
            Assert.Equal(Visibility.Visible, viewModel.EmptyHintVisibility);
            Assert.Equal(1, suppression.RestoreAllCallCount);
        }

        [Fact]
        public async Task NachDemLaden_IstNichtsMehrInArbeit()
        {
            (HiddenDialogsViewModel viewModel, _) = Build();

            await viewModel.LoadAsync();

            Assert.False(viewModel.IsBusy);
            Assert.True(viewModel.IsNotBusy);
        }
    }
}

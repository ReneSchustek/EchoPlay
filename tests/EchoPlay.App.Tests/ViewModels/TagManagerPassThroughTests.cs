using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für die Durchreiche-Schicht des Tag-Managers. Die Seite bindet auf das obere
    /// Ansichtsmodell, die Daten liegen in vier Unter-Ansichtsmodellen; eine Eigenschaft,
    /// die auf das falsche davon zeigt, fällt sonst erst am laufenden Programm auf.
    /// Die Abläufe (Laden, Speichern, Online-Abfrage) stehen in
    /// <see cref="TagManagerViewModelTests"/>.
    /// </summary>
    public sealed class TagManagerPassThroughTests
    {
        private static TagManagerViewModel BuildViewModel(FakeTagService? tagService = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ITagLookupService>(_ => new FakeTagLookupService());
            ServiceProvider provider = services.BuildServiceProvider();

            ITagLookupCoordinator coordinator = new TagLookupCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(), new FakeLoggerFactory());

            return new TagManagerViewModel(
                tagService ?? new FakeTagService(),
                coordinator,
                new FakeFileRenameService(),
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                new FakeOnlineAccessGuard());
        }

        [Fact]
        public async Task Dateiliste_ZeigtSuchtextFilterUndAuswahl()
        {
            IReadOnlyList<(string, AudioTag)> dateien =
            [
                (@"D:\test\001 - Der Superhund.mp3", new AudioTag { Title = "Der Superhund" }),
                (@"D:\test\002 - Der Schatz.mp3", new AudioTag { Title = "Der Schatz" })
            ];

            TagManagerViewModel viewModel = BuildViewModel(new FakeTagService(dateien));
            await viewModel.LoadFolderAsync(@"D:\test");

            Assert.True(viewModel.HasFiles);
            Assert.Equal(2, viewModel.Files.Count);

            viewModel.SearchText = "Superhund";

            Assert.Equal("Superhund", viewModel.FileListVM.SearchText);
            _ = Assert.Single(viewModel.Files);

            viewModel.ModifiedOnly = true;

            Assert.True(viewModel.FileListVM.ModifiedOnly);
            Assert.Empty(viewModel.Files);
            Assert.Equal(Visibility.Visible, viewModel.NoResultsVisibility);

            viewModel.ResetFiltersCommand.Execute(null);

            Assert.Equal(string.Empty, viewModel.SearchText);
            Assert.False(viewModel.ModifiedOnly);
            Assert.Equal(2, viewModel.Files.Count);
        }

        [Fact]
        public async Task Auswahl_ReichtDieMarkiertenDateienDurch()
        {
            IReadOnlyList<(string, AudioTag)> dateien =
            [
                (@"D:\test\001.mp3", new AudioTag { Title = "Eins" }),
                (@"D:\test\002.mp3", new AudioTag { Title = "Zwei" })
            ];

            TagManagerViewModel viewModel = BuildViewModel(new FakeTagService(dateien));
            await viewModel.LoadFolderAsync(@"D:\test");

            viewModel.SetSelectedFiles([viewModel.Files[0], viewModel.Files[1]]);

            Assert.Equal(2, viewModel.SelectedFiles.Count);
            Assert.True(viewModel.HasSelectedFile);
            Assert.Same(viewModel.FileListVM.SelectedFile, viewModel.SelectedFile);
        }

        [Fact]
        public void Editorfelder_SchreibenInDasUnterAnsichtsmodell()
        {
            TagManagerViewModel viewModel = BuildViewModel();

            viewModel.Title = "Der Superhund";
            viewModel.Album = "TKKG 1";
            viewModel.Artist = "TKKG";
            viewModel.AlbumArtist = "Various";
            viewModel.Year = "1981";
            viewModel.TrackNumber = "1";
            viewModel.TrackCount = "12";
            viewModel.Genre = "Hörspiel";

            Assert.Equal("Der Superhund", viewModel.EditorVM.Title);
            Assert.Equal("TKKG 1", viewModel.EditorVM.Album);
            Assert.Equal("TKKG", viewModel.EditorVM.Artist);
            Assert.Equal("Various", viewModel.EditorVM.AlbumArtist);
            Assert.Equal("1981", viewModel.EditorVM.Year);
            Assert.Equal("1", viewModel.EditorVM.TrackNumber);
            Assert.Equal("12", viewModel.EditorVM.TrackCount);
            Assert.Equal("Hörspiel", viewModel.EditorVM.Genre);
            Assert.False(viewModel.HasPendingBatchTag);
        }

        [Fact]
        public void Cover_ZeigtDenZustandDesUnterAnsichtsmodells()
        {
            TagManagerViewModel viewModel = BuildViewModel();

            Assert.Null(viewModel.CoverImage);
            Assert.Equal(viewModel.CoverVM.CoverVisibility, viewModel.CoverVisibility);
        }

        [Fact]
        public void Umbenennen_ReichtMusterUndVorschauDurch()
        {
            TagManagerViewModel viewModel = BuildViewModel();

            viewModel.RenamePattern = "{track} - {title}";

            Assert.Equal("{track} - {title}", viewModel.RenameVM.RenamePattern);
            Assert.Empty(viewModel.RenamePreview);
            Assert.Equal(Visibility.Collapsed, viewModel.RenamePreviewVisibility);
        }

        [Fact]
        public void Anzeigen_BleibenOhneVorgangUnsichtbar()
        {
            // Lade-, Such- und Fortschrittsanzeige gehören zum Ruhezustand der Seite:
            // Sie dürfen nicht stehen bleiben, wenn gerade nichts läuft.
            TagManagerViewModel viewModel = BuildViewModel();

            Assert.False(viewModel.IsLoading);
            Assert.Equal(Visibility.Collapsed, viewModel.IsLoadingVisibility);
            Assert.False(viewModel.IsLookingUp);
            Assert.Equal(Visibility.Collapsed, viewModel.IsLookingUpVisibility);
            Assert.Equal(Visibility.Collapsed, viewModel.AutoLookupStatusVisibility);
            Assert.Equal(Visibility.Collapsed, viewModel.BatchProgressVisibility);
            Assert.False(viewModel.HasUnsavedChanges);
        }
    }
}

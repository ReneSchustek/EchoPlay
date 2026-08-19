using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.TagManager.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Befehle und Statusanzeigen des Tag-Managers im Ausgangszustand.
    /// </summary>
    /// <remarks>
    /// Die Seite bindet an elf Befehle. Fehlt einer, bleibt die zugehörige Schaltfläche
    /// wirkungslos — ohne Meldung, ohne Eintrag im Protokoll. Genauso die beiden
    /// Statuszeilen: Sie dürfen nur erscheinen, wenn wirklich etwas läuft, sonst steht
    /// dauerhaft eine leere Zeile über der Dateiliste.
    /// </remarks>
    public sealed class TagManagerCommandsTests
    {
        [Fact]
        public void EveryCommandThePageBindsTo_Exists()
        {
            TagManagerViewModel sut = Build();

            // Ein fehlender Befehl fällt in der Oberfläche nicht auf: Die Schaltfläche
            // bleibt sichtbar und tut nichts.
            Assert.NotNull(sut.SaveCommand);
            Assert.NotNull(sut.SaveAllCommand);
            Assert.NotNull(sut.RemoveAllTagsCommand);
            Assert.NotNull(sut.LookupOnlineCommand);
            Assert.NotNull(sut.AutoLookupCommand);
            Assert.NotNull(sut.ApplyToAllCommand);
            Assert.NotNull(sut.RemoveCoverCommand);
            Assert.NotNull(sut.LoadCoverCommand);
            Assert.NotNull(sut.ApplyCoverToAllCommand);
            Assert.NotNull(sut.PreviewRenameCommand);
            Assert.NotNull(sut.ExecuteRenameCommand);
        }

        [Fact]
        public void WithoutASelectedFile_TheFileBoundCommandsStayDisabled()
        {
            TagManagerViewModel sut = Build();

            // Ohne gewählte Datei gibt es nichts zu speichern und nichts nachzuschlagen.
            // Eine bedienbare Schaltfläche wäre ein Versprechen ins Leere.
            Assert.False(sut.SaveCommand.CanExecute(null));
            Assert.False(sut.RemoveAllTagsCommand.CanExecute(null));
            Assert.False(sut.LookupOnlineCommand.CanExecute(null));
        }

        [Fact]
        public void StatusLines_WithoutAnythingRunning_StayHidden()
        {
            TagManagerViewModel sut = Build();

            Assert.Equal(Visibility.Collapsed, sut.AutoLookupStatusVisibility);
            Assert.Equal(Visibility.Collapsed, sut.BatchProgressVisibility);
            Assert.Equal(string.Empty, sut.AutoLookupStatusText);
            Assert.Equal(string.Empty, sut.BatchProgressText);
        }

        [Fact]
        public void ApplyLookupCandidate_WithoutAnyLookup_StaysQuiet()
        {
            TagManagerViewModel sut = Build();

            // Die Trefferliste kann leer sein, wenn der Anwender vor der Antwort klickt.
            sut.ApplyLookupCandidate(0);
        }

        [Fact]
        public void SetSelectedFiles_WithoutSelection_LeavesTheEditorEmpty()
        {
            TagManagerViewModel sut = Build();

            sut.SetSelectedFiles([]);

            Assert.Empty(sut.SelectedFiles);
            Assert.False(sut.HasUnsavedChanges);
        }

        [Fact]
        public void HasUnsavedChanges_OnAFreshPage_IsFalse()
        {
            TagManagerViewModel sut = Build();

            // Der Verlassen-Hinweis darf nicht erscheinen, solange nichts geändert wurde.
            Assert.False(sut.HasUnsavedChanges);
            Assert.Empty(sut.FileListVM.Files);
        }

        private static TagManagerViewModel Build()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ITagLookupService>(_ => new FakeTagLookupService());
            ServiceProvider provider = services.BuildServiceProvider();

            ITagLookupCoordinator coordinator = new TagLookupCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(), new FakeLoggerFactory());

            return new TagManagerViewModel(
                new FakeTagService(),
                coordinator,
                new FakeFileRenameService(),
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                new FakeOnlineAccessGuard());
        }
    }
}

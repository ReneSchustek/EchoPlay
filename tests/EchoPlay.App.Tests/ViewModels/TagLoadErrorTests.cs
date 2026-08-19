using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.TagManager.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft, was der Tag-Manager meldet, wenn eine Datei sich nicht lesen lässt.
    /// </summary>
    /// <remarks>
    /// Eine beschädigte Kennzeichnung ist der häufigste Grund, warum jemand den
    /// Tag-Manager überhaupt öffnet. Verschluckt er den Fehler, bleibt der Editor leer
    /// und der Anwender hält die Datei für in Ordnung.
    /// </remarks>
    public sealed class TagLoadErrorTests
    {
        private const string FilePath = @"D:\Media\TKKG\Folge 001\01 - Auftakt.mp3";

        [Fact]
        public async Task LoadFolderAsync_WhenTheFolderCannotBeRead_ShowsTheReason()
        {
            FakeErrorDialogService errorDialog = new();
            FakeTagService tagService = new(readFolderFailure: new IOException("Laufwerk nicht bereit"));
            TagManagerViewModel sut = Build(tagService, errorDialog);

            await sut.LoadFolderAsync(Path.Combine(Path.GetTempPath(), "echoplay-gibt-es-nicht"));

            // Der Ordner kann inzwischen umbenannt oder das Laufwerk getrennt sein.
            (string Title, string Message) shown = Assert.Single(errorDialog.ShownDialogs);
            Assert.Equal("Laufwerk nicht bereit", shown.Message);
        }

        [Fact]
        public async Task LoadFileTags_WhenTheFileCannotBeRead_ShowsTheReason()
        {
            FakeTagService tagService = new() { ReadFailure = new IOException("Datei beschädigt") };
            FakeErrorDialogService errorDialog = new();
            TagManagerViewModel sut = Build(tagService, errorDialog);

            sut.SetSelectedFiles([new TagFileItemViewModel(FilePath)]);
            await sut.WaitForFileLoadCompleteAsync();

            (string Title, string Message) shown = Assert.Single(errorDialog.ShownDialogs);
            Assert.Equal("Datei beschädigt", shown.Message);
        }

        [Fact]
        public async Task LoadFileTags_WhenTheFileCannotBeRead_ReleasesTheLoadingState()
        {
            FakeTagService tagService = new() { ReadFailure = new IOException("Datei beschädigt") };
            TagManagerViewModel sut = Build(tagService);

            sut.SetSelectedFiles([new TagFileItemViewModel(FilePath)]);
            await sut.WaitForFileLoadCompleteAsync();

            // Bliebe die Ladeanzeige stehen, wäre die Seite nach einer kaputten Datei
            // unbedienbar.
            Assert.False(sut.IsLoading);
        }

        [Fact]
        public async Task LoadMultipleFileTags_WhenOneFileCannotBeRead_ShowsTheReason()
        {
            FakeTagService tagService = new() { ReadFailure = new IOException("Datei beschädigt") };
            FakeErrorDialogService errorDialog = new();
            TagManagerViewModel sut = Build(tagService, errorDialog);

            sut.SetSelectedFiles(
            [
                new TagFileItemViewModel(FilePath),
                new TagFileItemViewModel(@"D:\Media\TKKG\Folge 001\02 - Mitte.mp3"),
            ]);
            await sut.WaitForFileLoadCompleteAsync();

            (string Title, string Message) shown = Assert.Single(errorDialog.ShownDialogs);
            Assert.Equal("Datei beschädigt", shown.Message);
        }

        private static TagManagerViewModel Build(
            FakeTagService? tagService = null, FakeErrorDialogService? errorDialog = null)
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
                errorDialog ?? new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                new FakeOnlineAccessGuard());
        }
    }
}

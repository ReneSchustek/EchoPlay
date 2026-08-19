using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Cover-Aktionen des Tag-Managers: das Cover einer Datei entfernen und ein
    /// Cover auf alle Dateien des Ordners schreiben.
    /// </summary>
    /// <remarks>
    /// Ein geladenes Cover lässt sich im Testlauf nicht setzen: <c>SetFromFile</c> erzeugt
    /// ein Bildobjekt, und das gehört an den Dispatcher. Geprüft ist deshalb der Weg ohne
    /// Bild — er entscheidet, ob eine Rückfrage überhaupt erscheint — und das Entfernen,
    /// das ohne Bildobjekt auskommt.
    /// </remarks>
    public sealed class TagCoverActionsTests
    {
        private const string FolderPath = @"D:\Media\TKKG";
        private const string FirstFile = @"D:\Media\TKKG\001.mp3";
        private const string SecondFile = @"D:\Media\TKKG\002.mp3";

        [Fact]
        public async Task CoverEntfernen_LeertDieAnzeigeUndSchreibtDieDatei()
        {
            Harness harness = Harness.Build();
            harness.SelectFirst();

            await harness.Actions.RemoveCoverAsync();

            Assert.Equal(1, harness.TagService.WriteCoverCallCount);
            Assert.Null(harness.TagService.GetWrittenCover(FirstFile));
            Assert.False(harness.Cover.HasCover);
        }

        [Fact]
        public async Task CoverEntfernen_OhneAuswahlTutNichts()
        {
            Harness harness = Harness.Build();

            await harness.Actions.RemoveCoverAsync();

            Assert.Equal(0, harness.TagService.WriteCoverCallCount);
        }

        [Fact]
        public async Task CoverAufAlle_OhneCoverFragtGarNicht()
        {
            // Ohne geladenes Bild gäbe es nichts zu schreiben — die Rückfrage wäre eine
            // Zumutung ohne Zweck.
            Harness harness = Harness.Build();

            await harness.Actions.ApplyCoverToAllAsync();

            Assert.Equal(0, harness.Confirmation.CallCount);
            Assert.Equal(0, harness.TagService.WriteCoverCallCount);
        }

        [Fact]
        public async Task CoverEntfernen_MeldetEinenFehlschlag()
        {
            Harness harness = Harness.Build(writeCoverFailure: new System.IO.IOException("Datei gesperrt"));
            harness.SelectFirst();

            await harness.Actions.RemoveCoverAsync();

            Assert.Equal("Datei gesperrt", Assert.Single(harness.ErrorDialog.ShownDialogs).Message);
        }

        /// <summary>Baut die Cover-Aktionen mit Dateiliste und Cover-Ansicht.</summary>
        private sealed class Harness
        {
            public TagCoverActions Actions { get; private set; } = null!;

            public required TagFileListViewModel FileList { get; init; }

            public required TagCoverViewModel Cover { get; init; }

            public required FakeTagService TagService { get; init; }

            public required FakeConfirmationDialogService Confirmation { get; init; }

            public required FakeErrorDialogService ErrorDialog { get; init; }

            public void SelectFirst() => FileList.SetSelectedFiles([FileList.Files[0]]);

            public static Harness Build(bool confirmResult = true, System.Exception? writeCoverFailure = null)
            {
                FakeTagService tagService = new(
                [
                    (FirstFile, new AudioTag { Title = "Erste" }),
                    (SecondFile, new AudioTag { Title = "Zweite" })
                ])
                {
                    WriteCoverFailure = writeCoverFailure
                };
                FakeConfirmationDialogService confirmation = new(confirmResult);
                FakeErrorDialogService errorDialog = new();

                ServiceCollection services = new();
                _ = services.AddScoped<ITagLookupService>(_ => new FakeTagLookupService());
                ServiceProvider provider = services.BuildServiceProvider();

                TagManagerActionsContext context = new(
                    TagService: tagService,
                    LookupCoordinator: new TagLookupCoordinator(
                        provider.GetRequiredService<IServiceScopeFactory>(), new FakeLoggerFactory()),
                    FileRenameService: new FakeFileRenameService(),
                    ErrorDialogService: errorDialog,
                    ConfirmationDialogService: confirmation,
                    OnlineAccessGuard: new FakeOnlineAccessGuard());

                TagFileListViewModel fileList = new();
                fileList.SetFiles(
                    [
                        new TagFileItemViewModel(FirstFile, FolderPath),
                        new TagFileItemViewModel(SecondFile, FolderPath)
                    ],
                    FolderPath);

                TagCoverViewModel cover = new(() => { });

                Harness harness = new()
                {
                    FileList = fileList,
                    Cover = cover,
                    TagService = tagService,
                    Confirmation = confirmation,
                    ErrorDialog = errorDialog,
                };

                harness.Actions = new TagCoverActions(
                    context,
                    fileList,
                    cover,
                    setIsLoading: _ => { },
                    setBatchProgress: _ => { },
                    setHasUnsavedChanges: _ => { });

                return harness;
            }
        }
    }
}

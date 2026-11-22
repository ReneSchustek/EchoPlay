using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.TagManager.Models;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft das Speichern im Tag-Manager: eine Datei, mehrere Dateien und das Entfernen
    /// aller Angaben.
    /// </summary>
    /// <remarks>
    /// Zwei Zusagen tragen diese Klasse. Erstens: Was der Anwender nicht bestätigt hat,
    /// wird nicht geschrieben; „alle speichern" und „alle Angaben entfernen" fragen vorher.
    /// Zweitens: Bei Mehrfachauswahl werden nur die geänderten Felder auf die anderen
    /// Dateien geschrieben; sonst überschriebe ein Sammel-Speichern jeden Einzeltitel.
    /// </remarks>
    public sealed class TagSaveActionsTests
    {
        private const string FolderPath = @"D:\Media\TKKG";
        private const string FirstFile = @"D:\Media\TKKG\001.mp3";
        private const string SecondFile = @"D:\Media\TKKG\002.mp3";

        [Fact]
        public async Task SingleFile_WritesAllFields()
        {
            Harness harness = Harness.Build();
            harness.SelectSingle();
            harness.Editor.Title = "Der Superhund";
            harness.Editor.Artist = "TKKG";

            await harness.Actions.SaveAsync();

            AudioTag? written = harness.TagService.GetWrittenTag(FirstFile);
            Assert.NotNull(written);
            Assert.Equal("Der Superhund", written!.Title);
            Assert.Equal("TKKG", written.Artist);
            Assert.False(harness.FileList.Files[0].IsModified);
            Assert.Equal([false], harness.UnsavedStates);
        }

        [Fact]
        public async Task SingleFile_ReportsAFailedWrite()
        {
            // Eine schreibgeschützte Datei darf nicht stillschweigend durchgehen, sonst
            // hält der Anwender die Kennzeichnung für gespeichert.
            Harness harness = Harness.Build(writeFailure: new IOException("Datei gesperrt"));
            harness.SelectSingle();
            harness.MarkModified(0);

            await harness.Actions.SaveAsync();

            (string Title, string Message) dialog = Assert.Single(harness.ErrorDialog.ShownDialogs);
            Assert.Equal("Datei gesperrt", dialog.Message);
            Assert.True(harness.FileList.Files[0].IsModified);
        }

        [Fact]
        public async Task MultiSelection_WritesOnlyTheChangedFields()
        {
            // Die zweite Datei behält ihren eigenen Titel; übernommen wird nur, was der
            // Anwender im Formular angefasst hat.
            Harness harness = Harness.Build(
                folderFiles:
                [
                    (FirstFile, new AudioTag { Title = "Erste", Artist = "Alt" }),
                    (SecondFile, new AudioTag { Title = "Zweite", Artist = "Alt" })
                ]);
            harness.SelectBoth();
            harness.Editor.Artist = "TKKG";

            await harness.Actions.SaveAsync();

            Assert.Equal("TKKG", harness.TagService.GetWrittenTag(FirstFile)?.Artist);
            Assert.Equal("Zweite", harness.TagService.GetWrittenTag(SecondFile)?.Title);
            Assert.Equal("TKKG", harness.TagService.GetWrittenTag(SecondFile)?.Artist);
        }

        [Fact]
        public async Task MultiSelection_WithoutChange_WritesNothing()
        {
            Harness harness = Harness.Build();
            harness.SelectBoth();

            await harness.Actions.SaveAsync();

            Assert.Equal(0, harness.TagService.WriteCallCount);
        }

        [Fact]
        public async Task SaveAll_AsksFirstAndWritesTheChangedFiles()
        {
            Harness harness = Harness.Build();
            harness.MarkModified(0);
            harness.MarkModified(1);
            harness.Editor.Genre = "Hörspiel";

            await harness.Actions.SaveAllAsync();

            Assert.Equal(1, harness.Confirmation.CallCount);
            Assert.Equal(2, harness.TagService.WriteCallCount);
            Assert.All(harness.FileList.Files, file => Assert.False(file.IsModified));
        }

        [Fact]
        public async Task SaveAll_AfterDecline_DoesNothing()
        {
            Harness harness = Harness.Build(confirmResult: false);
            harness.MarkModified(0);

            await harness.Actions.SaveAllAsync();

            Assert.Equal(1, harness.Confirmation.CallCount);
            Assert.Equal(0, harness.TagService.WriteCallCount);
            Assert.True(harness.FileList.Files[0].IsModified);
        }

        [Fact]
        public async Task SaveAll_WithoutChangedFiles_DoesNotAsk()
        {
            Harness harness = Harness.Build();

            await harness.Actions.SaveAllAsync();

            Assert.Equal(0, harness.Confirmation.CallCount);
            Assert.Equal(0, harness.TagService.WriteCallCount);
        }

        [Fact]
        public async Task RemoveTags_ClearsFieldsAndCoverAfterConfirmation()
        {
            Harness harness = Harness.Build();
            harness.SelectSingle();
            harness.Editor.Title = "Der Superhund";

            await harness.Actions.RemoveAllTagsAsync();

            Assert.Equal(1, harness.TagService.RemoveAllCallCount);
            Assert.Null(harness.Editor.Title);
            Assert.False(harness.Cover.HasCover);
            Assert.False(harness.FileList.Files[0].IsModified);
        }

        [Fact]
        public async Task RemoveTags_AfterDecline_DoesNothing()
        {
            Harness harness = Harness.Build(confirmResult: false);
            harness.SelectSingle();
            harness.Editor.Title = "Der Superhund";

            await harness.Actions.RemoveAllTagsAsync();

            Assert.Equal(0, harness.TagService.RemoveAllCallCount);
            Assert.Equal("Der Superhund", harness.Editor.Title);
        }

        [Fact]
        public async Task RemoveTags_WithoutSelection_DoesNothing()
        {
            Harness harness = Harness.Build();

            await harness.Actions.RemoveAllTagsAsync();

            Assert.Equal(0, harness.Confirmation.CallCount);
            Assert.Equal(0, harness.TagService.RemoveAllCallCount);
        }

        [Fact]
        public async Task RemoveTags_ReportsAFailure()
        {
            Harness harness = Harness.Build(removeAllFailure: new IOException("Kein Zugriff"));
            harness.SelectSingle();

            await harness.Actions.RemoveAllTagsAsync();

            Assert.Equal("Kein Zugriff", Assert.Single(harness.ErrorDialog.ShownDialogs).Message);
        }

        /// <summary>
        /// Baut die Speicher-Aktionen mit den drei beteiligten Unter-Ansichtsmodellen.
        /// Zusammengefasst, weil jeder Test auf mehrere davon zugreift.
        /// </summary>
        private sealed class Harness
        {
            public TagSaveActions Actions { get; private set; } = null!;

            public required TagFileListViewModel FileList { get; init; }

            public required TagEditorFieldsViewModel Editor { get; init; }

            public required TagCoverViewModel Cover { get; init; }

            public required FakeTagService TagService { get; init; }

            public required FakeErrorDialogService ErrorDialog { get; init; }

            public required FakeConfirmationDialogService Confirmation { get; init; }

            public List<bool> UnsavedStates { get; } = [];

            /// <summary>Wählt die erste Datei aus (der Einzel-Speicherweg).</summary>
            public void SelectSingle() => FileList.SetSelectedFiles([FileList.Files[0]]);

            /// <summary>Wählt beide Dateien aus (der Sammel-Speicherweg).</summary>
            public void SelectBoth() => FileList.SetSelectedFiles([FileList.Files[0], FileList.Files[1]]);

            /// <summary>Markiert eine Datei als geändert, wie es der Editor tut.</summary>
            public void MarkModified(int index) => FileList.Files[index].IsModified = true;

            /// <summary>Die beiden Dateien des Ordners mit je eigenem Titel.</summary>
            private static IReadOnlyList<(string, AudioTag)> TwoTaggedFiles()
                =>
                [
                    (FirstFile, new AudioTag { Title = "Erste" }),
                    (SecondFile, new AudioTag { Title = "Zweite" })
                ];

            public static Harness Build(
                IReadOnlyList<(string, AudioTag)>? folderFiles = null,
                bool confirmResult = true,
                Exception? writeFailure = null,
                Exception? removeAllFailure = null)
            {
                FakeTagService tagService = new(folderFiles ?? TwoTaggedFiles())
                {
                    WriteFailure = writeFailure,
                    RemoveAllFailure = removeAllFailure
                };

                FakeErrorDialogService errorDialog = new();
                FakeConfirmationDialogService confirmation = new(confirmResult);

                TagManagerActionsContext context = TagActionsTestContext.Create(tagService, errorDialog, confirmation);

                TagFileListViewModel fileList = TagActionsTestContext.FileList(FolderPath, FirstFile, SecondFile);

                TagEditorFieldsViewModel editor = new(() => { });
                TagCoverViewModel cover = new(() => { });

                Harness harness = new()
                {
                    FileList = fileList,
                    Editor = editor,
                    Cover = cover,
                    TagService = tagService,
                    ErrorDialog = errorDialog,
                    Confirmation = confirmation,
                };

                harness.Actions = new TagSaveActions(
                    context, fileList, editor, cover,
                    setIsLoading: _ => { },
                    setBatchProgress: _ => { },
                    setHasUnsavedChanges: state => harness.UnsavedStates.Add(state));

                return harness;
            }
        }
    }
}

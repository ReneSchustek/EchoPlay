using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft das Umbenennen im Tag-Manager: Vorschau, Rückfrage, Ausführung und die
    /// Meldung, wenn nicht alle Dateien durchkamen.
    /// </summary>
    /// <remarks>
    /// Der Vorgang lässt sich nicht rückgängig machen — das steht so im Dialog. Damit sind
    /// drei Dinge Zusagen und keine Nebensachen: Eine Ablehnung benennt nichts um, ein
    /// Teilerfolg wird gemeldet statt verschwiegen, und der Ladezustand fällt auch im
    /// Fehlerfall zurück, sonst bleibt die Ansicht gesperrt.
    /// <para>
    /// Eigene Datei statt Ergänzung von <c>TagSubActionsTests</c>: Die Datei liegt bereits
    /// über der Grenze aus <c>testing.md</c>.
    /// </para>
    /// </remarks>
    public sealed class TagRenameActionsTests
    {
        private const string FolderPath = @"D:\Media\Die drei Fragezeichen\001 Der Super-Papagei";

        // ── Vorschau ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task PreviewRename_WithFolder_FillsThePreview()
        {
            Harness harness = Harness.Build(
                folderFiles: TwoFiles(),
                preview: [PreviewItem("alt.mp3", "neu.mp3")]);
            harness.OpenFolder();

            await harness.Actions.PreviewRenameAsync();

            _ = Assert.Single(harness.Rename.PreviewItems);
            Assert.Equal(1, harness.RenameService.BuildPreviewCallCount);
        }

        [Fact]
        public async Task PreviewRename_PassesThePatternFromTheView()
        {
            Harness harness = Harness.Build(folderFiles: TwoFiles());
            harness.OpenFolder();
            harness.Rename.RenamePattern = "{track} - {title}";

            await harness.Actions.PreviewRenameAsync();

            // Das Muster kommt aus dem Eingabefeld. Käme ein anderes beim Dienst an,
            // zeigte die Vorschau Namen, die beim Umbenennen niemand vergibt.
            Assert.Equal("{track} - {title}", harness.RenameService.LastPattern);
        }

        [Fact]
        public async Task PreviewRename_ReleasesTheLoadingState()
        {
            Harness harness = Harness.Build(folderFiles: TwoFiles());
            harness.OpenFolder();

            await harness.Actions.PreviewRenameAsync();

            Assert.Equal([true, false], harness.LoadingStates);
        }

        [Fact]
        public async Task PreviewRename_WhenReadingFails_ShowsAnError()
        {
            Harness harness = Harness.Build(
                folderFiles: TwoFiles(),
                readFolderFailure: new IOException("Datei gesperrt"));
            harness.OpenFolder();

            await harness.Actions.PreviewRenameAsync();

            (string Title, string Message) shown = Assert.Single(harness.ErrorDialog.ShownDialogs);
            Assert.Equal("Datei gesperrt", shown.Message);
        }

        [Fact]
        public async Task PreviewRename_WhenReadingFails_StillReleasesTheLoadingState()
        {
            Harness harness = Harness.Build(
                folderFiles: TwoFiles(),
                readFolderFailure: new IOException("Datei gesperrt"));
            harness.OpenFolder();

            await harness.Actions.PreviewRenameAsync();

            // Bliebe der Ladezustand stehen, wäre die Ansicht nach einem einzigen
            // Lesefehler dauerhaft gesperrt — ohne dass ein Neustart naheläge.
            Assert.Equal([true, false], harness.LoadingStates);
        }

        // ── Ausführung ───────────────────────────────────────────────────────────

        [Fact]
        public async Task ExecuteRename_WhenUserDeclines_RenamesNothing()
        {
            Harness harness = Harness.Build(
                folderFiles: TwoFiles(),
                preview: [PreviewItem("alt.mp3", "neu.mp3")],
                confirmResult: false);
            harness.OpenFolder();

            await harness.Actions.ExecuteRenameAsync();

            // Der Dialog sagt zu, dass sich der Vorgang nicht rückgängig machen lässt.
            // Wer ablehnt, behält jeden Dateinamen.
            Assert.Equal(0, harness.RenameService.RenameCallCount);
            Assert.False(harness.ReloadedFolder);
        }

        [Theory]
        [InlineData(1, "1 Datei wird nach dem Muster „{title}\" umbenannt. Dieser Vorgang kann nicht rückgängig gemacht werden.")]
        [InlineData(2, "2 Dateien werden nach dem Muster „{title}\" umbenannt. Dieser Vorgang kann nicht rückgängig gemacht werden.")]
        public async Task ExecuteRename_AsksWithTheMatchingPluralForm(int previewCount, string expected)
        {
            List<RenamePreviewItem> preview = [];
            for (int i = 1; i <= previewCount; i++)
            {
                preview.Add(PreviewItem($"alt{i}.mp3", $"neu{i}.mp3"));
            }

            Harness harness = Harness.Build(
                folderFiles: TwoFiles(), preview: preview, confirmResult: false);
            harness.OpenFolder();
            harness.Rename.RenamePattern = "{title}";
            harness.Rename.SetPreviewItems(preview);

            await harness.Actions.ExecuteRenameAsync();

            // Die Rückfrage nennt eine Anzahl. Bei genau einer Datei muss die Einzahlform
            // stehen, sonst liest sich der Dialog falsch.
            Assert.Equal(expected, harness.Confirmation.LastMessage);
        }

        [Fact]
        public async Task ExecuteRename_WhenConfirmed_RenamesAndReloadsTheFolder()
        {
            Harness harness = Harness.Build(
                folderFiles: TwoFiles(),
                preview: [PreviewItem("alt.mp3", "neu.mp3")],
                renamedCount: 1);
            harness.OpenFolder();
            harness.Rename.SetPreviewItems([PreviewItem("alt.mp3", "neu.mp3")]);

            await harness.Actions.ExecuteRenameAsync();

            // Nach dem Umbenennen zeigt die Liste noch die alten Namen. Ohne das
            // Nachladen stünden dort Dateien, die es unter diesem Namen nicht mehr gibt.
            Assert.Equal(1, harness.RenameService.RenameCallCount);
            Assert.True(harness.ReloadedFolder);
            Assert.Equal(FolderPath, harness.ReloadedPath);
        }

        [Fact]
        public async Task ExecuteRename_WhenAllFilesWereRenamed_ReportsNothing()
        {
            List<RenamePreviewItem> preview = [PreviewItem("a.mp3", "1.mp3"), PreviewItem("b.mp3", "2.mp3")];
            Harness harness = Harness.Build(
                folderFiles: TwoFiles(), preview: preview, renamedCount: 2);
            harness.OpenFolder();
            harness.Rename.SetPreviewItems(preview);

            await harness.Actions.ExecuteRenameAsync();

            Assert.Empty(harness.ErrorDialog.ShownDialogs);
        }

        [Fact]
        public async Task ExecuteRename_WhenNotAllFilesWereRenamed_ReportsIt()
        {
            List<RenamePreviewItem> preview =
            [
                PreviewItem("a.mp3", "1.mp3"),
                PreviewItem("b.mp3", "2.mp3"),
                PreviewItem("c.mp3", "3.mp3"),
            ];
            Harness harness = Harness.Build(
                folderFiles: TwoFiles(), preview: preview, renamedCount: 2);
            harness.OpenFolder();
            harness.Rename.SetPreviewItems(preview);

            await harness.Actions.ExecuteRenameAsync();

            // Zwei von drei ist kein Erfolg. Bliebe die Meldung aus, hielte der Anwender
            // eine halb umbenannte Ablage für fertig und suchte den Rest nie.
            (string Title, string Message) shown = Assert.Single(harness.ErrorDialog.ShownDialogs);
            Assert.Contains("2 von 3", shown.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ExecuteRename_WhenRenamingFails_ShowsAnError()
        {
            List<RenamePreviewItem> preview = [PreviewItem("a.mp3", "1.mp3")];
            Harness harness = Harness.Build(
                folderFiles: TwoFiles(),
                preview: preview,
                renameFailure: new UnauthorizedAccessException("Kein Zugriff"));
            harness.OpenFolder();
            harness.Rename.SetPreviewItems(preview);

            await harness.Actions.ExecuteRenameAsync();

            (string Title, string Message) shown = Assert.Single(harness.ErrorDialog.ShownDialogs);
            Assert.Equal("Kein Zugriff", shown.Message);
        }

        [Fact]
        public async Task ExecuteRename_WhenRenamingFails_StillReleasesTheLoadingState()
        {
            List<RenamePreviewItem> preview = [PreviewItem("a.mp3", "1.mp3")];
            Harness harness = Harness.Build(
                folderFiles: TwoFiles(),
                preview: preview,
                renameFailure: new UnauthorizedAccessException("Kein Zugriff"));
            harness.OpenFolder();
            harness.Rename.SetPreviewItems(preview);

            await harness.Actions.ExecuteRenameAsync();

            Assert.Equal([true, false], harness.LoadingStates);
        }

        [Fact]
        public async Task ExecuteRename_WhenUserDeclines_TouchesTheLoadingStateNotAtAll()
        {
            Harness harness = Harness.Build(
                folderFiles: TwoFiles(),
                preview: [PreviewItem("a.mp3", "1.mp3")],
                confirmResult: false);
            harness.OpenFolder();

            await harness.Actions.ExecuteRenameAsync();

            // Ohne Zusage passiert nichts — auch kein kurzes Aufblitzen des Ladebalkens.
            Assert.Empty(harness.LoadingStates);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static IReadOnlyList<(string, AudioTag)> TwoFiles() =>
        [
            ($@"{FolderPath}\alt1.mp3", new AudioTag { Title = "Kapitel 1" }),
            ($@"{FolderPath}\alt2.mp3", new AudioTag { Title = "Kapitel 2" }),
        ];

        private static RenamePreviewItem PreviewItem(string oldName, string newName) =>
            new()
            {
                OldName = oldName,
                NewName = newName,
                FilePath = $@"{FolderPath}\{oldName}",
            };

        /// <summary>
        /// Baut die Aktionen samt Umfeld. Zusammengefasst, weil die Tests auf mehrere der
        /// beteiligten Nachbauten und auf beide Rückrufe zugreifen.
        /// </summary>
        private sealed class Harness
        {
            public TagRenameActions Actions { get; private set; } = null!;

            public required TagFileListViewModel FileList { get; init; }

            public required TagRenameViewModel Rename { get; init; }

            public required FakeFileRenameService RenameService { get; init; }

            public required FakeErrorDialogService ErrorDialog { get; init; }

            public required FakeConfirmationDialogService Confirmation { get; init; }

            public List<bool> LoadingStates { get; } = [];

            public bool ReloadedFolder { get; private set; }

            public string? ReloadedPath { get; private set; }

            /// <summary>Setzt den geöffneten Ordner — sonst steigen beide Aktionen sofort aus.</summary>
            public void OpenFolder() => FileList.SetFiles([], FolderPath);

            public static Harness Build(
                IReadOnlyList<(string, AudioTag)>? folderFiles = null,
                IReadOnlyList<RenamePreviewItem>? preview = null,
                int renamedCount = 0,
                bool confirmResult = true,
                Exception? readFolderFailure = null,
                Exception? renameFailure = null)
            {
                FakeFileRenameService renameService = new(preview, renamedCount, renameFailure);
                FakeErrorDialogService errorDialog = new();
                FakeConfirmationDialogService confirmation = new(confirmResult);

                ServiceCollection services = new();
                _ = services.AddScoped<ITagLookupService>(_ => new FakeTagLookupService());
                ServiceProvider provider = services.BuildServiceProvider();

                TagManagerActionsContext context = new(
                    TagService: new FakeTagService(folderFiles, readFolderFailure),
                    LookupCoordinator: new TagLookupCoordinator(
                        provider.GetRequiredService<IServiceScopeFactory>(), new FakeLoggerFactory()),
                    FileRenameService: renameService,
                    ErrorDialogService: errorDialog,
                    ConfirmationDialogService: confirmation,
                    OnlineAccessGuard: new FakeOnlineAccessGuard());

                TagFileListViewModel fileList = new();
                TagRenameViewModel rename = new();

                Harness harness = new()
                {
                    FileList = fileList,
                    Rename = rename,
                    RenameService = renameService,
                    ErrorDialog = errorDialog,
                    Confirmation = confirmation,
                };

                harness.Actions = new TagRenameActions(
                    context,
                    fileList,
                    rename,
                    setIsLoading: state => harness.LoadingStates.Add(state),
                    refreshCommandStates: () => { },
                    reloadFolderAsync: path =>
                    {
                        harness.ReloadedFolder = true;
                        harness.ReloadedPath = path;
                        return Task.CompletedTask;
                    });

                return harness;
            }
        }
    }
}

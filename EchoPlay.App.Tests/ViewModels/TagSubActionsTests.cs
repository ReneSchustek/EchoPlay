using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für die Sub-Actions des Tag-Managers. Pro Sub-Action mindestens zwei
    /// Tests inklusive Call-Counter-Assertion, um die Zerschneidung des früheren
    /// 707-Zeilen-Monolithen gegen Regressionen abzusichern.
    /// </summary>
    public sealed class TagSubActionsTests
    {
        // ── Gemeinsames Setup ────────────────────────────────────────────────────

        private static TagManagerActionsContext BuildContext(FakeTagService tagService)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ITagLookupService>(_ => new FakeTagLookupService());
            ServiceProvider provider = services.BuildServiceProvider();
            ITagLookupCoordinator coordinator = new TagLookupCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>());

            return new TagManagerActionsContext(
                TagService: tagService,
                LookupCoordinator: coordinator,
                FileRenameService: new FakeFileRenameService(),
                ErrorDialogService: new FakeErrorDialogService(),
                ConfirmationDialogService: new FakeConfirmationDialogService(),
                OnlineAccessGuard: new FakeOnlineAccessGuard());
        }

        private static (TagFileListViewModel fileList, TagEditorFieldsViewModel editor,
            TagCoverViewModel cover, TagRenameViewModel rename) BuildSubVms()
        {
            TagFileListViewModel fileList = new();
            TagEditorFieldsViewModel editor = new(() => { });
            TagCoverViewModel cover = new(() => { });
            TagRenameViewModel rename = new();
            return (fileList, editor, cover, rename);
        }

        // ── TagLoadActions ───────────────────────────────────────────────────────

        [Fact]
        public async Task TagLoadActions_LoadFolderAsync_IncrementsCallCount()
        {
            IReadOnlyList<(string, AudioTag)> folderFiles =
            [
                (@"D:\test\track1.mp3", new AudioTag { Title = "Kapitel 1" })
            ];
            FakeTagService tagService = new(folderFiles);
            TagManagerActionsContext ctx = BuildContext(tagService);
            (TagFileListViewModel fileList, TagEditorFieldsViewModel editor, TagCoverViewModel cover, TagRenameViewModel rename) = BuildSubVms();

            TagLoadActions sut = new(ctx, fileList, editor, cover, rename,
                setIsLoading: _ => { }, setHasUnsavedChanges: _ => { }, refreshCommandStates: () => { });

            await sut.LoadFolderAsync(@"D:\test");

            Assert.Equal(1, sut.LoadFolderCallCount);
            _ = Assert.Single(fileList.Files);
        }

        [Fact]
        public async Task TagLoadActions_LoadFileTagsAsync_PopulatesEditor()
        {
            IReadOnlyList<(string, AudioTag)> folderFiles =
            [
                (@"D:\test\track1.mp3", new AudioTag { Title = "Kapitel 1", Album = "Album" })
            ];
            FakeTagService tagService = new(folderFiles);
            TagManagerActionsContext ctx = BuildContext(tagService);
            (TagFileListViewModel fileList, TagEditorFieldsViewModel editor, TagCoverViewModel cover, TagRenameViewModel rename) = BuildSubVms();

            TagLoadActions sut = new(ctx, fileList, editor, cover, rename,
                setIsLoading: _ => { }, setHasUnsavedChanges: _ => { }, refreshCommandStates: () => { });

            await sut.LoadFolderAsync(@"D:\test");
            await sut.LoadFileTagsAsync(fileList.Files[0]);

            Assert.Equal(1, sut.LoadFileTagsCallCount);
            Assert.Equal("Kapitel 1", editor.Title);
            Assert.Equal("Album", editor.Album);
        }

        // ── TagSaveActions ───────────────────────────────────────────────────────

        [Fact]
        public async Task TagSaveActions_SaveAsync_NoSelection_DoesNotWrite()
        {
            FakeTagService tagService = new();
            TagManagerActionsContext ctx = BuildContext(tagService);
            (TagFileListViewModel fileList, TagEditorFieldsViewModel editor, TagCoverViewModel cover, _) = BuildSubVms();

            TagSaveActions sut = new(ctx, fileList, editor, cover,
                setIsLoading: _ => { }, setBatchProgress: _ => { }, setHasUnsavedChanges: _ => { });

            await sut.SaveAsync();

            Assert.Equal(1, sut.SaveCallCount);
            Assert.Equal(0, tagService.WriteCallCount);
        }

        [Fact]
        public async Task TagSaveActions_SaveAsync_WithSelection_WritesTag()
        {
            IReadOnlyList<(string, AudioTag)> folderFiles =
            [
                (@"D:\test\track1.mp3", new AudioTag { Title = "Original" })
            ];
            FakeTagService tagService = new(folderFiles);
            TagManagerActionsContext ctx = BuildContext(tagService);
            (TagFileListViewModel fileList, TagEditorFieldsViewModel editor, TagCoverViewModel cover, TagRenameViewModel rename) = BuildSubVms();

            TagLoadActions loader = new(ctx, fileList, editor, cover, rename,
                setIsLoading: _ => { }, setHasUnsavedChanges: _ => { }, refreshCommandStates: () => { });
            await loader.LoadFolderAsync(@"D:\test");
            fileList.SetSelectedFiles([fileList.Files[0]]);
            await loader.WaitForFileLoadCompleteAsync();
            editor.Title = "Neuer Titel";

            TagSaveActions sut = new(ctx, fileList, editor, cover,
                setIsLoading: _ => { }, setBatchProgress: _ => { }, setHasUnsavedChanges: _ => { });

            await sut.SaveAsync();

            Assert.Equal(1, tagService.WriteCallCount);
            AudioTag? written = tagService.GetWrittenTag(@"D:\test\track1.mp3");
            Assert.Equal("Neuer Titel", written?.Title);
        }

        // ── TagLookupActions ─────────────────────────────────────────────────────

        [Fact]
        public void TagLookupActions_ApplyLookupResult_IncrementsHasUnsavedChangesCallback()
        {
            FakeTagService tagService = new();
            TagManagerActionsContext ctx = BuildContext(tagService);
            (TagFileListViewModel fileList, TagEditorFieldsViewModel editor, _, TagRenameViewModel rename) = BuildSubVms();

            bool unsavedChangesRaised = false;
            TagLookupActions sut = new(ctx, fileList, editor, rename,
                setIsLoading: _ => { }, setIsLookingUp: _ => { }, setAutoLookupStatus: _ => { },
                setBatchProgress: _ => { }, setHasUnsavedChanges: v => unsavedChangesRaised = v,
                refreshCommandStates: () => { }, previewRenameAsync: () => Task.CompletedTask);

            sut.ApplyLookupResult(new TagLookupResult { Title = "T", Album = "A", Year = 2024 });

            Assert.True(unsavedChangesRaised);
            Assert.Equal("T", editor.Title);
            Assert.Equal("A", editor.Album);
        }

        [Fact]
        public async Task TagLookupActions_LookupOnlineAsync_NoSelection_SkipsIncrement()
        {
            FakeTagService tagService = new();
            TagManagerActionsContext ctx = BuildContext(tagService);
            (TagFileListViewModel fileList, TagEditorFieldsViewModel editor, _, TagRenameViewModel rename) = BuildSubVms();

            TagLookupActions sut = new(ctx, fileList, editor, rename,
                setIsLoading: _ => { }, setIsLookingUp: _ => { }, setAutoLookupStatus: _ => { },
                setBatchProgress: _ => { }, setHasUnsavedChanges: _ => { },
                refreshCommandStates: () => { }, previewRenameAsync: () => Task.CompletedTask);

            await sut.LookupOnlineAsync();

            Assert.Equal(1, sut.LookupOnlineCallCount);
        }

        // ── TagCoverActions ──────────────────────────────────────────────────────

        [Fact]
        public async Task TagCoverActions_RemoveCoverAsync_NoSelection_SkipsWrite()
        {
            FakeTagService tagService = new();
            TagManagerActionsContext ctx = BuildContext(tagService);
            (TagFileListViewModel fileList, _, TagCoverViewModel cover, _) = BuildSubVms();

            TagCoverActions sut = new(ctx, fileList, cover,
                setIsLoading: _ => { }, setBatchProgress: _ => { }, setHasUnsavedChanges: _ => { });

            await sut.RemoveCoverAsync();

            Assert.Equal(1, sut.RemoveCoverCallCount);
            Assert.Equal(0, tagService.WriteCoverCallCount);
        }

        [Fact]
        public async Task TagCoverActions_ApplyCoverToAllAsync_WithoutCover_SkipsBatch()
        {
            FakeTagService tagService = new();
            TagManagerActionsContext ctx = BuildContext(tagService);
            (TagFileListViewModel fileList, _, TagCoverViewModel cover, _) = BuildSubVms();

            TagCoverActions sut = new(ctx, fileList, cover,
                setIsLoading: _ => { }, setBatchProgress: _ => { }, setHasUnsavedChanges: _ => { });

            await sut.ApplyCoverToAllAsync();

            Assert.Equal(1, sut.ApplyCoverToAllCallCount);
            Assert.Equal(0, tagService.WriteCoverCallCount);
        }

        // ── TagRenameActions ─────────────────────────────────────────────────────

        [Fact]
        public async Task TagRenameActions_PreviewRenameAsync_EmptyFolder_SkipsPreview()
        {
            FakeTagService tagService = new();
            TagManagerActionsContext ctx = BuildContext(tagService);
            (TagFileListViewModel fileList, _, _, TagRenameViewModel rename) = BuildSubVms();

            TagRenameActions sut = new(ctx, fileList, rename,
                setIsLoading: _ => { }, refreshCommandStates: () => { },
                reloadFolderAsync: _ => Task.CompletedTask);

            await sut.PreviewRenameAsync();

            Assert.Equal(1, sut.PreviewRenameCallCount);
            Assert.Empty(rename.PreviewItems);
        }

        [Fact]
        public async Task TagRenameActions_ExecuteRenameAsync_EmptyFolder_SkipsExecution()
        {
            FakeTagService tagService = new();
            TagManagerActionsContext ctx = BuildContext(tagService);
            (TagFileListViewModel fileList, _, _, TagRenameViewModel rename) = BuildSubVms();

            bool reloadCalled = false;
            TagRenameActions sut = new(ctx, fileList, rename,
                setIsLoading: _ => { }, refreshCommandStates: () => { },
                reloadFolderAsync: _ => { reloadCalled = true; return Task.CompletedTask; });

            await sut.ExecuteRenameAsync();

            Assert.Equal(1, sut.ExecuteRenameCallCount);
            Assert.False(reloadCalled);
        }

        // ── TagBatchRunner (Merge-Logik) ─────────────────────────────────────────

        [Fact]
        public void TagBatchRunner_MergeEditedIntoExisting_PrefersEdited_OverExisting()
        {
            AudioTag edited = new() { Title = "Neu", Artist = null };
            AudioTag existing = new() { Title = "Alt", Artist = "Künstler" };

            AudioTag merged = TagBatchRunner.MergeEditedIntoExisting(edited, existing);

            Assert.Equal("Neu", merged.Title);
            Assert.Equal("Künstler", merged.Artist);
        }

        [Fact]
        public void TagBatchRunner_MergeSharedIntoExisting_KeepsTitleAndTrackNumber()
        {
            AudioTag shared = new() { Album = "Neues Album", Title = "Überschrieben" };
            AudioTag existing = new() { Title = "Original", TrackNumber = 3, Album = "Altes Album" };

            AudioTag merged = TagBatchRunner.MergeSharedIntoExisting(shared, existing);

            Assert.Equal("Original", merged.Title);
            Assert.Equal<uint?>(3, merged.TrackNumber);
            Assert.Equal("Neues Album", merged.Album);
        }
    }
}

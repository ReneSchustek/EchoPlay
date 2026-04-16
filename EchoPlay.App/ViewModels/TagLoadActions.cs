using EchoPlay.App.Helpers;
using EchoPlay.TagManager.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-Actions: Ordner- und Dateiladen im Tag-Manager. Setzt die Sub-VMs
    /// (Dateiliste, Editor, Cover, Rename) beim Laden zurück und befüllt sie.
    /// </summary>
    internal sealed class TagLoadActions
    {
        private readonly TagManagerActionsContext _ctx;
        private readonly TagFileListViewModel _fileListVM;
        private readonly TagEditorFieldsViewModel _editorVM;
        private readonly TagCoverViewModel _coverVM;
        private readonly TagRenameViewModel _renameVM;
        private readonly Action<bool> _setIsLoading;
        private readonly Action<bool> _setHasUnsavedChanges;
        private readonly Action _refreshCommandStates;

        private TaskCompletionSource<bool>? _fileLoadCompletedSource;

        /// <summary>Public Call-Counter für Tests.</summary>
        public int LoadFolderCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int LoadFileTagsCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int LoadMultipleFileTagsCallCount { get; private set; }

        public TagLoadActions(
            TagManagerActionsContext context,
            TagFileListViewModel fileListVM,
            TagEditorFieldsViewModel editorVM,
            TagCoverViewModel coverVM,
            TagRenameViewModel renameVM,
            Action<bool> setIsLoading,
            Action<bool> setHasUnsavedChanges,
            Action refreshCommandStates)
        {
            _ctx = context;
            _fileListVM = fileListVM;
            _editorVM = editorVM;
            _coverVM = coverVM;
            _renameVM = renameVM;
            _setIsLoading = setIsLoading;
            _setHasUnsavedChanges = setHasUnsavedChanges;
            _refreshCommandStates = refreshCommandStates;
        }

        /// <summary>Wartet auf den Abschluss des laufenden Datei-Ladevorgangs (für deterministische Tests).</summary>
        internal Task WaitForFileLoadCompleteAsync()
            => _fileLoadCompletedSource?.Task ?? Task.CompletedTask;

        /// <summary>Lädt alle Audiodateien eines Ordners und setzt die Sub-VMs zurück.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Ordner-Enumeration: IO-Fehler (UnauthorizedAccess, PathTooLong, IOException) einzelner Unterordner/Dateien werden als Nutzer-Fehlermeldung angezeigt, damit der Tag-Manager nicht abstuerzt.")]
        public async Task LoadFolderAsync(string folderPath)
        {
            LoadFolderCallCount++;

            _setIsLoading(true);
            _fileListVM.Clear();
            _editorVM.ClearPendingBatchTag();
            _editorVM.Clear();
            _coverVM.Clear();
            _renameVM.SetPreviewItems([]);

            try
            {
                IReadOnlyList<(string FilePath, AudioTag Tag)> results = await _ctx.TagService.ReadFolderAsync(folderPath);

                List<TagFileItemViewModel> items = new(results.Count);
                foreach ((string path, AudioTag _) in results)
                {
                    items.Add(new TagFileItemViewModel(path, folderPath));
                }

                _fileListVM.SetFiles(items, folderPath);
                _setHasUnsavedChanges(false);
                _refreshCommandStates();
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerFolderLoadErrorTitle"), ex.Message);
            }
            finally
            {
                _setIsLoading(false);
            }
        }

        /// <summary>Lädt die Tags einer einzelnen Datei in den Editor und das Cover-Sub-VM.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Tag-Read einer einzelnen Audiodatei: TagLib-Fehler (CorruptFileException, UnsupportedFormat) oder IO-Fehler werden als Nutzer-Fehlermeldung angezeigt, der Editor bleibt bedienbar.")]
        public async Task LoadFileTagsAsync(TagFileItemViewModel file)
        {
            LoadFileTagsCallCount++;

            _fileLoadCompletedSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _setIsLoading(true);
            _editorVM.Clear();
            _coverVM.Clear();

            try
            {
                AudioTag tag = await _ctx.TagService.ReadAsync(file.FilePath);
                _editorVM.PopulateFromTag(tag);
                _coverVM.LoadFromTag(tag);
                _setHasUnsavedChanges(false);
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerTagLoadErrorTitle"), ex.Message);
            }
            finally
            {
                _setIsLoading(false);
                _ = _fileLoadCompletedSource?.TrySetResult(true);
            }
        }

        /// <summary>Lädt die Tags mehrerer Dateien und zeigt gemeinsame Werte an.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Tag-Read fuer Multi-Auswahl: TagLib-/IO-Fehler einer einzelnen Datei duerfen den Batch-Read der restlichen Dateien nicht abbrechen.")]
        public async Task LoadMultipleFileTagsAsync(IReadOnlyList<TagFileItemViewModel> files)
        {
            LoadMultipleFileTagsCallCount++;

            _fileLoadCompletedSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _setIsLoading(true);
            _editorVM.Clear();
            _coverVM.Clear();

            try
            {
                List<AudioTag> tags = new(files.Count);
                foreach (TagFileItemViewModel file in files)
                {
                    AudioTag tag = await _ctx.TagService.ReadAsync(file.FilePath);
                    tags.Add(tag);
                }

                _editorVM.PopulateFromMultipleTags(tags);
                _coverVM.Clear();
                _setHasUnsavedChanges(false);
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerTagLoadErrorTitle"), ex.Message);
            }
            finally
            {
                _setIsLoading(false);
                _ = _fileLoadCompletedSource?.TrySetResult(true);
            }
        }
    }
}

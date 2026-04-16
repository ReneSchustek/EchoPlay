using EchoPlay.App.Models;
using EchoPlay.TagManager.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Koordinator für die Async-Aktionen des Tag-Managers. Hält fünf Sub-Actions
    /// (<see cref="TagLoadActions"/>, <see cref="TagSaveActions"/>,
    /// <see cref="TagLookupActions"/>, <see cref="TagCoverActions"/>,
    /// <see cref="TagRenameActions"/>) und delegiert alle public Methoden. Das Top-VM
    /// <see cref="TagManagerViewModel"/> hält nur noch Commands, Zustands-Properties und
    /// die Pass-Through-Schicht. Folgt dem Muster aus dem Dashboard-Refactor.
    /// </summary>
    internal sealed class TagManagerActions
    {
        private readonly TagLoadActions _loadActions;
        private readonly TagSaveActions _saveActions;
        private readonly TagLookupActions _lookupActions;
        private readonly TagCoverActions _coverActions;
        private readonly TagRenameActions _renameActions;

        /// <summary>
        /// Initialisiert den Koordinator und verdrahtet die fünf Sub-Actions sowie die
        /// Workflow-Callbacks zwischen Rename-Preview und Folder-Reload.
        /// </summary>
        public TagManagerActions(
            TagManagerActionsContext context,
            TagFileListViewModel fileListVM,
            TagEditorFieldsViewModel editorVM,
            TagCoverViewModel coverVM,
            TagRenameViewModel renameVM,
            Action<bool> setIsLoading,
            Action<bool> setIsLookingUp,
            Action<string> setAutoLookupStatus,
            Action<string> setBatchProgress,
            Action<bool> setHasUnsavedChanges,
            Action refreshCommandStates)
        {
            _loadActions = new TagLoadActions(
                context, fileListVM, editorVM, coverVM, renameVM,
                setIsLoading, setHasUnsavedChanges, refreshCommandStates);

            _saveActions = new TagSaveActions(
                context, fileListVM, editorVM, coverVM,
                setIsLoading, setBatchProgress, setHasUnsavedChanges);

            _renameActions = new TagRenameActions(
                context, fileListVM, renameVM,
                setIsLoading, refreshCommandStates,
                reloadFolderAsync: _loadActions.LoadFolderAsync);

            _lookupActions = new TagLookupActions(
                context, fileListVM, editorVM, renameVM,
                setIsLoading, setIsLookingUp, setAutoLookupStatus, setBatchProgress,
                setHasUnsavedChanges, refreshCommandStates,
                previewRenameAsync: _renameActions.PreviewRenameAsync);

            _coverActions = new TagCoverActions(
                context, fileListVM, coverVM,
                setIsLoading, setBatchProgress, setHasUnsavedChanges);
        }

        /// <summary>Lookup-Ergebnisse für die Page – weitergereicht von <see cref="TagLookupActions"/>.</summary>
        public event EventHandler<IReadOnlyList<TagLookupCandidate>>? LookupResultsReady
        {
            add    => _lookupActions.LookupResultsReady += value;
            remove => _lookupActions.LookupResultsReady -= value;
        }

        /// <inheritdoc cref="LookupResultsReady"/>
        public event EventHandler<TagLookupCandidate>? AutoLookupApplied
        {
            add    => _lookupActions.AutoLookupApplied += value;
            remove => _lookupActions.AutoLookupApplied -= value;
        }

        /// <inheritdoc cref="LookupResultsReady"/>
        public event EventHandler? RenamePreviewReady
        {
            add    => _lookupActions.RenamePreviewReady += value;
            remove => _lookupActions.RenamePreviewReady -= value;
        }

        /// <inheritdoc cref="TagLoadActions.LoadFolderAsync"/>
        public Task LoadFolderAsync(string folderPath) => _loadActions.LoadFolderAsync(folderPath);

        /// <inheritdoc cref="TagLoadActions.LoadFileTagsAsync"/>
        public Task LoadFileTagsAsync(TagFileItemViewModel file) => _loadActions.LoadFileTagsAsync(file);

        /// <inheritdoc cref="TagLoadActions.LoadMultipleFileTagsAsync"/>
        public Task LoadMultipleFileTagsAsync(IReadOnlyList<TagFileItemViewModel> files)
            => _loadActions.LoadMultipleFileTagsAsync(files);

        /// <inheritdoc cref="TagSaveActions.SaveAsync"/>
        public Task SaveAsync() => _saveActions.SaveAsync();

        /// <inheritdoc cref="TagSaveActions.SaveAllAsync"/>
        public Task SaveAllAsync() => _saveActions.SaveAllAsync();

        /// <inheritdoc cref="TagSaveActions.RemoveAllTagsAsync"/>
        public Task RemoveAllTagsAsync() => _saveActions.RemoveAllTagsAsync();

        /// <inheritdoc cref="TagLookupActions.LookupOnlineAsync"/>
        public Task LookupOnlineAsync() => _lookupActions.LookupOnlineAsync();

        /// <inheritdoc cref="TagLookupActions.AutoLookupAsync"/>
        public Task AutoLookupAsync() => _lookupActions.AutoLookupAsync();

        /// <inheritdoc cref="TagLookupActions.ApplyLookupCandidate"/>
        public void ApplyLookupCandidate(int index) => _lookupActions.ApplyLookupCandidate(index);

        /// <inheritdoc cref="TagLookupActions.ApplyLookupResult"/>
        public void ApplyLookupResult(TagLookupResult result) => _lookupActions.ApplyLookupResult(result);

        /// <inheritdoc cref="TagLookupActions.ApplyToAllAsync"/>
        public Task ApplyToAllAsync() => _lookupActions.ApplyToAllAsync();

        /// <inheritdoc cref="TagCoverActions.RemoveCoverAsync"/>
        public Task RemoveCoverAsync() => _coverActions.RemoveCoverAsync();

        /// <inheritdoc cref="TagCoverActions.ApplyCoverToAllAsync"/>
        public Task ApplyCoverToAllAsync() => _coverActions.ApplyCoverToAllAsync();

        /// <inheritdoc cref="TagRenameActions.PreviewRenameAsync"/>
        public Task PreviewRenameAsync() => _renameActions.PreviewRenameAsync();

        /// <inheritdoc cref="TagRenameActions.ExecuteRenameAsync"/>
        public Task ExecuteRenameAsync() => _renameActions.ExecuteRenameAsync();

        /// <summary>Wartet auf den Abschluss des laufenden Auto-Lookups (für deterministische Tests).</summary>
        internal Task WaitForAutoLookupCompleteAsync() => _lookupActions.WaitForAutoLookupCompleteAsync();

        /// <summary>Wartet auf den Abschluss des laufenden Datei-Ladevorgangs (für deterministische Tests).</summary>
        internal Task WaitForFileLoadCompleteAsync() => _loadActions.WaitForFileLoadCompleteAsync();
    }
}

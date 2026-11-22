using EchoPlay.App.Models;
using EchoPlay.TagManager.Models;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Koordinator für die Async-Aktionen des Tag-Managers. Hält fünf Sub-Actions
    /// (<see cref="TagLoadActions"/>, <see cref="TagSaveActions"/>,
    /// <see cref="TagLookupActions"/>, <see cref="TagCoverActions"/>,
    /// <see cref="TagRenameActions"/>) und delegiert alle public Methoden. Das Top-VM
    /// <see cref="TagManagerViewModel"/> hält Commands, Zustands-Properties und die
    /// Pass-Through-Schicht; dasselbe Muster wie bei <see cref="OnlineLibraryActions"/>.
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
        /// <param name="context">Die Dienste des Tag-Managers.</param>
        /// <param name="parts">Die vier Teil-ViewModels.</param>
        /// <param name="callbacks">Die Rückrufe in den Zustand des Tag-Managers.</param>
        public TagManagerActions(TagManagerActionsContext context, TagManagerParts parts, TagManagerCallbacks callbacks)
        {
            ArgumentNullException.ThrowIfNull(parts);
            ArgumentNullException.ThrowIfNull(callbacks);
            _loadActions = new TagLoadActions(context, parts, callbacks);

            _saveActions = new TagSaveActions(
                context, parts.FileList, parts.Editor, parts.Cover,
                callbacks.SetIsLoading, callbacks.SetBatchProgress, callbacks.SetHasUnsavedChanges);

            _renameActions = new TagRenameActions(
                context, parts.FileList, parts.Rename,
                callbacks.SetIsLoading, callbacks.RefreshCommandStates,
                reloadFolderAsync: _loadActions.LoadFolderAsync);

            _lookupActions = new TagLookupActions(context, parts, callbacks, previewRenameAsync: _renameActions.PreviewRenameAsync);

            _coverActions = new TagCoverActions(
                context, parts.FileList, parts.Cover,
                callbacks.SetIsLoading, callbacks.SetBatchProgress, callbacks.SetHasUnsavedChanges);
        }

        /// <summary>Lookup-Ergebnisse für die Page, weitergereicht von <see cref="TagLookupActions"/>.</summary>
        public event EventHandler<IReadOnlyList<TagLookupCandidate>>? LookupResultsReady
        {
            add => _lookupActions.LookupResultsReady += value;
            remove => _lookupActions.LookupResultsReady -= value;
        }

        /// <inheritdoc cref="LookupResultsReady"/>
        public event EventHandler<TagLookupCandidate>? AutoLookupApplied
        {
            add => _lookupActions.AutoLookupApplied += value;
            remove => _lookupActions.AutoLookupApplied -= value;
        }

        /// <inheritdoc cref="LookupResultsReady"/>
        public event EventHandler? RenamePreviewReady
        {
            add => _lookupActions.RenamePreviewReady += value;
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

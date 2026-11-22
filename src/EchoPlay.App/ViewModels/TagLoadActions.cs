using EchoPlay.App.Helpers;
using EchoPlay.Core.Models;
using EchoPlay.TagManager.Models;

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

        /// <summary>Richtet die Ladeaktionen ein.</summary>
        /// <param name="context">Die Dienste des Tag-Managers.</param>
        /// <param name="parts">Dateiliste, Eingabefelder, Cover und Umbenennungs-Vorschau.</param>
        /// <param name="callbacks">Ladeanzeige, Markierung ungespeicherter Änderungen und Befehle.</param>
        public TagLoadActions(TagManagerActionsContext context, TagManagerParts parts, TagManagerCallbacks callbacks)
        {
            ArgumentNullException.ThrowIfNull(parts);
            ArgumentNullException.ThrowIfNull(callbacks);
            _ctx = context;
            _fileListVM = parts.FileList;
            _editorVM = parts.Editor;
            _coverVM = parts.Cover;
            _renameVM = parts.Rename;
            _setIsLoading = callbacks.SetIsLoading;
            _setHasUnsavedChanges = callbacks.SetHasUnsavedChanges;
            _refreshCommandStates = callbacks.RefreshCommandStates;
        }

        /// <summary>Wartet auf den Abschluss des laufenden Datei-Ladevorgangs (für deterministische Tests).</summary>
        internal Task WaitForFileLoadCompleteAsync()
            => _fileLoadCompletedSource?.Task ?? Task.CompletedTask;

        /// <summary>Lädt alle Audiodateien eines Ordners und setzt die Sub-VMs zurück.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Ordner-Enumeration: IO-Fehler (UnauthorizedAccess, PathTooLong, IOException) werden als Nutzer-Fehlermeldung angezeigt, damit der Tag-Manager nicht abstürzt.")]
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
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerFolderLoadErrorTitle"), ex.Message, DialogKey.TagManagerFolderLoadError);
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
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerTagLoadErrorTitle"), ex.Message, DialogKey.TagManagerTagLoadError);
            }
            finally
            {
                _setIsLoading(false);
                _ = _fileLoadCompletedSource?.TrySetResult(true);
            }
        }

        /// <summary>
        /// Lädt die Tags mehrerer Dateien und zeigt gemeinsame Werte an. Eine unlesbare Datei
        /// fällt aus dem Vergleich heraus und steht am Ende gesammelt in einem Dialog.
        /// </summary>
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
                (List<AudioTag> tags, List<string> failures) = await ReadAllTagsAsync(files);
                if (tags.Count > 0)
                {
                    _editorVM.PopulateFromMultipleTags(tags);
                }

                _setHasUnsavedChanges(false);

                if (failures.Count > 0)
                {
                    await _ctx.ErrorDialogService.ShowAsync(
                        SafeResourceLoader.Get("TagManagerTagLoadErrorTitle"),
                        string.Join(Environment.NewLine, failures),
                        DialogKey.TagManagerTagLoadError);
                }
            }
            finally
            {
                _setIsLoading(false);
                _ = _fileLoadCompletedSource?.TrySetResult(true);
            }
        }

        /// <summary>
        /// Liest die Tags aller Dateien; jede unlesbare Datei landet mit Grund in der Fehlerliste.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Tag-Read für Multi-Auswahl: TagLib-/IO-Fehler einer einzelnen Datei dürfen den Batch-Read der restlichen Dateien nicht abbrechen.")]
        private async Task<(List<AudioTag> Tags, List<string> Failures)> ReadAllTagsAsync(IReadOnlyList<TagFileItemViewModel> files)
        {
            List<AudioTag> tags = new(files.Count);
            List<string> failures = [];
            foreach (TagFileItemViewModel file in files)
            {
                try
                {
                    tags.Add(await _ctx.TagService.ReadAsync(file.FilePath));
                }
                catch (Exception ex)
                {
                    failures.Add($"{file.FileName}: {ex.Message}");
                }
            }

            return (tags, failures);
        }
    }
}

using EchoPlay.App.Helpers;
using EchoPlay.TagManager.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-Actions: Speichern und Entfernen von Tags. Beherrscht Einzel- und
    /// Mehrfachauswahl sowie das Löschen aller Tags.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1863:Use 'CompositeFormat'", Justification = "Format-Strings werden zur Laufzeit aus 'SafeResourceLoader.Get(...)' (Resources.resw) geladen und sind zum Kompilierzeitpunkt unbekannt.")]
    internal sealed class TagSaveActions
    {
        private readonly TagManagerActionsContext _ctx;
        private readonly TagFileListViewModel _fileListVM;
        private readonly TagEditorFieldsViewModel _editorVM;
        private readonly TagCoverViewModel _coverVM;
        private readonly Action<bool> _setIsLoading;
        private readonly Action<string> _setBatchProgress;
        private readonly Action<bool> _setHasUnsavedChanges;

        /// <summary>Public Call-Counter für Tests.</summary>
        public int SaveCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int SaveAllCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int RemoveAllTagsCallCount { get; private set; }

        public TagSaveActions(
            TagManagerActionsContext context,
            TagFileListViewModel fileListVM,
            TagEditorFieldsViewModel editorVM,
            TagCoverViewModel coverVM,
            Action<bool> setIsLoading,
            Action<string> setBatchProgress,
            Action<bool> setHasUnsavedChanges)
        {
            _ctx = context;
            _fileListVM = fileListVM;
            _editorVM = editorVM;
            _coverVM = coverVM;
            _setIsLoading = setIsLoading;
            _setBatchProgress = setBatchProgress;
            _setHasUnsavedChanges = setHasUnsavedChanges;
        }

        /// <summary>
        /// Speichert die aktuell angezeigten Tags. Bei Einzelauswahl werden alle Felder
        /// geschrieben; bei Mehrfachauswahl nur die vom Nutzer geänderten auf alle selektierten Dateien.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Tag-Speichern: TagLib-/IO-Fehler (gesperrte Datei, Read-Only, korruptes Format) werden als Nutzer-Fehlermeldung angezeigt, damit die App nicht reißt.")]
        public async Task SaveAsync()
        {
            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("TagSave");
            SaveCallCount++;

            // Einzelauswahl: alle Felder schreiben
            if (_fileListVM.SelectedFile is not null)
            {
                try
                {
                    AudioTag tag = _editorVM.BuildAudioTagFromFields();
                    await _ctx.TagService.WriteAsync(_fileListVM.SelectedFile.FilePath, tag);

                    _fileListVM.SelectedFile.IsModified = false;
                    _setHasUnsavedChanges(false);
                }
                catch (Exception ex)
                {
                    await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerSaveErrorTitle"), ex.Message);
                }

                return;
            }

            // Mehrfachauswahl: nur geänderte Felder auf alle selektierten Dateien schreiben
            if (_fileListVM.SelectedFiles.Count <= 1 || _editorVM.EditedFieldCount == 0)
            {
                return;
            }

            AudioTag editedTag = _editorVM.BuildEditedFieldsTag();
            await TagBatchRunner.RunBatchAsync(
                _fileListVM.SelectedFiles,
                async file =>
                {
                    AudioTag existingTag = await _ctx.TagService.ReadAsync(file.FilePath);
                    AudioTag mergedTag = TagBatchRunner.MergeEditedIntoExisting(editedTag, existingTag);
                    await _ctx.TagService.WriteAsync(file.FilePath, mergedTag);
                    file.IsModified = false;
                },
                SafeResourceLoader.Get("TagManagerSaveErrorTitle"),
                _ctx.ErrorDialogService, _setIsLoading, _setBatchProgress);

            _setHasUnsavedChanges(false);
        }

        /// <summary>Speichert alle modifizierten Dateien auf einmal nach Nutzerbestätigung.</summary>
        public async Task SaveAllAsync()
        {
            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("TagSaveAll");
            SaveAllCallCount++;

            List<TagFileItemViewModel> modifiedFiles = _fileListVM.Files.Where(f => f.IsModified).ToList();

            if (modifiedFiles.Count == 0)
            {
                return;
            }

            bool confirmed = await _ctx.ConfirmationDialogService.ConfirmAsync(
                SafeResourceLoader.Get("TagManagerSaveAllConfirmTitle"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    SafeResourceLoader.Get("TagManagerSaveAllConfirmMessage"),
                    modifiedFiles.Count));

            if (!confirmed)
            {
                return;
            }

            AudioTag sharedTag = _editorVM.BuildSharedTagFromFields();
            await TagBatchRunner.RunBatchAsync(
                modifiedFiles,
                async file =>
                {
                    AudioTag existingTag = await _ctx.TagService.ReadAsync(file.FilePath);
                    AudioTag mergedTag = TagBatchRunner.MergeSharedIntoExisting(sharedTag, existingTag);
                    await _ctx.TagService.WriteAsync(file.FilePath, mergedTag);
                    file.IsModified = false;
                },
                SafeResourceLoader.Get("TagManagerSaveErrorTitle"),
                _ctx.ErrorDialogService, _setIsLoading, _setBatchProgress);

            _setHasUnsavedChanges(false);
        }

        /// <summary>Entfernt alle Tags der ausgewählten Datei nach Nutzerbestätigung.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Tag-Löschen: TagLib-/IO-Fehler werden als Nutzer-Fehlermeldung angezeigt, damit die App nicht reißt.")]
        public async Task RemoveAllTagsAsync()
        {
            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("TagRemoveAll");
            RemoveAllTagsCallCount++;

            if (_fileListVM.SelectedFile is null)
            {
                return;
            }

            bool confirmed = await _ctx.ConfirmationDialogService.ConfirmAsync(
                SafeResourceLoader.Get("TagManagerRemoveAllTitle"),
                string.Format(CultureInfo.CurrentCulture, SafeResourceLoader.Get("TagManagerRemoveAllMessage"), _fileListVM.SelectedFile.FileName));

            if (!confirmed)
            {
                return;
            }

            try
            {
                await _ctx.TagService.RemoveAllTagsAsync(_fileListVM.SelectedFile.FilePath);
                _editorVM.Clear();
                _coverVM.Clear();
                _setHasUnsavedChanges(false);
                _fileListVM.SelectedFile.IsModified = false;
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerRemoveTagsErrorTitle"), ex.Message);
            }
        }
    }
}

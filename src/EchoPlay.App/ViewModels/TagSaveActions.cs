using EchoPlay.App.Helpers;
using EchoPlay.Core.Models;
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

        /// <summary>Richtet die Speicheraktionen ein.</summary>
        /// <param name="context">Die Dienste des Tag-Managers.</param>
        /// <param name="fileListVM">Die Dateiliste mit Auswahl.</param>
        /// <param name="editorVM">Die Eingabefelder.</param>
        /// <param name="coverVM">Das angezeigte Cover.</param>
        /// <param name="setIsLoading">Schaltet die Ladeanzeige.</param>
        /// <param name="setBatchProgress">Setzt den Fortschrittstext.</param>
        /// <param name="setHasUnsavedChanges">Setzt die Markierung ungespeicherter Änderungen.</param>
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
        public async Task SaveAsync()
        {
            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("TagSave");
            SaveCallCount++;

            // Einzelauswahl: alle Felder schreiben
            if (_fileListVM.SelectedFile is not null)
            {
                await SaveSingleFileAsync(_fileListVM.SelectedFile);
                return;
            }

            // Mehrfachauswahl: nur geänderte Felder auf alle selektierten Dateien schreiben
            if (_fileListVM.SelectedFiles.Count <= 1 || _editorVM.EditedFieldCount == 0)
            {
                return;
            }

            AudioTag editedTag = _editorVM.BuildEditedFieldsTag();
            int failed = await MergeIntoFilesAsync(
                _fileListVM.SelectedFiles,
                existingTag => TagBatchRunner.MergeEditedIntoExisting(editedTag, existingTag));

            _setHasUnsavedChanges(failed > 0);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Tag-Speichern: TagLib-/IO-Fehler (gesperrte Datei, Read-Only, korruptes Format) werden als Nutzer-Fehlermeldung angezeigt, damit die App nicht reißt.")]
        private async Task SaveSingleFileAsync(TagFileItemViewModel file)
        {
            try
            {
                AudioTag tag = _editorVM.BuildAudioTagFromFields();
                await _ctx.TagService.WriteAsync(file.FilePath, tag);

                file.IsModified = false;
                _setHasUnsavedChanges(false);
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerSaveErrorTitle"), ex.Message, DialogKey.TagManagerSaveError);
            }
        }

        /// <summary>
        /// Liest jede Datei, mischt die Felder aus dem Editor hinein und schreibt sie zurück.
        /// </summary>
        /// <returns>Die Zahl der Dateien, die nicht geschrieben werden konnten.</returns>
        private Task<int> MergeIntoFilesAsync(
            IReadOnlyList<TagFileItemViewModel> files,
            Func<AudioTag, AudioTag> mergeIntoExisting) =>
            TagBatchRunner.RunBatchAsync(
                files,
                async file =>
                {
                    AudioTag existingTag = await _ctx.TagService.ReadAsync(file.FilePath);
                    await _ctx.TagService.WriteAsync(file.FilePath, mergeIntoExisting(existingTag));
                    file.IsModified = false;
                },
                SafeResourceLoader.Get("TagManagerSaveErrorTitle"),
                DialogKey.TagManagerSaveError,
                _ctx.ErrorDialogService, _setIsLoading, _setBatchProgress);

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

            if (!await ConfirmSaveAllAsync(modifiedFiles.Count))
            {
                return;
            }

            AudioTag sharedTag = _editorVM.BuildSharedTagFromFields();
            int failed = await MergeIntoFilesAsync(
                modifiedFiles,
                existingTag => TagBatchRunner.MergeSharedIntoExisting(sharedTag, existingTag));

            _setHasUnsavedChanges(failed > 0);
        }

        private Task<bool> ConfirmSaveAllAsync(int fileCount) =>
            _ctx.ConfirmationDialogService.ConfirmAsync(
                SafeResourceLoader.Get("TagManagerSaveAllConfirmTitle"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    PluralText.Pattern(
                        fileCount,
                        "TagManagerSaveAllConfirmMessageSingular",
                        "TagManagerSaveAllConfirmMessagePlural",
                        "{0} geänderte Datei wird gespeichert.",
                        "{0} geänderte Dateien werden gespeichert."),
                    fileCount),
                DialogKey.TagManagerSaveAll);

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

            string removeAllPattern = SafeResourceLoader.Get("TagManagerRemoveAllMessage");
            bool confirmed = await _ctx.ConfirmationDialogService.ConfirmAsync(
                SafeResourceLoader.Get("TagManagerRemoveAllTitle"),
                string.Format(CultureInfo.CurrentCulture, removeAllPattern, _fileListVM.SelectedFile.FileName),
                DialogKey.TagManagerRemoveAll);

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
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerRemoveTagsErrorTitle"), ex.Message, DialogKey.TagManagerRemoveTagsError);
            }
        }
    }
}

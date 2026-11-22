using EchoPlay.App.Helpers;
using EchoPlay.Core.Models;
using System.Globalization;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-Actions: Cover-Operationen im Tag-Manager (Einzel-Entfernen, Batch-Apply).
    /// </summary>
    internal sealed class TagCoverActions
    {
        private readonly TagManagerActionsContext _ctx;
        private readonly TagFileListViewModel _fileListVM;
        private readonly TagCoverViewModel _coverVM;
        private readonly Action<bool> _setIsLoading;
        private readonly Action<string> _setBatchProgress;
        private readonly Action<bool> _setHasUnsavedChanges;

        /// <summary>Public Call-Counter für Tests.</summary>
        public int RemoveCoverCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int ApplyCoverToAllCallCount { get; private set; }

        /// <summary>Richtet die Cover-Aktionen ein.</summary>
        /// <param name="context">Die Dienste des Tag-Managers.</param>
        /// <param name="fileListVM">Die Dateiliste mit Auswahl.</param>
        /// <param name="coverVM">Das angezeigte Cover.</param>
        /// <param name="setIsLoading">Schaltet die Ladeanzeige.</param>
        /// <param name="setBatchProgress">Setzt den Fortschrittstext.</param>
        /// <param name="setHasUnsavedChanges">Setzt die Markierung ungespeicherter Änderungen.</param>
        public TagCoverActions(
            TagManagerActionsContext context,
            TagFileListViewModel fileListVM,
            TagCoverViewModel coverVM,
            Action<bool> setIsLoading,
            Action<string> setBatchProgress,
            Action<bool> setHasUnsavedChanges)
        {
            _ctx = context;
            _fileListVM = fileListVM;
            _coverVM = coverVM;
            _setIsLoading = setIsLoading;
            _setBatchProgress = setBatchProgress;
            _setHasUnsavedChanges = setHasUnsavedChanges;
        }

        /// <summary>Entfernt das Cover der aktuell ausgewählten Datei.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Entfernen per TagLib: TagLib-/IO-Fehler werden als Nutzer-Fehlermeldung angezeigt, damit die App nicht reißt.")]
        public async Task RemoveCoverAsync()
        {
            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("TagRemoveCover");
            RemoveCoverCallCount++;

            if (_fileListVM.SelectedFile is null)
            {
                return;
            }

            try
            {
                await _ctx.TagService.WriteCoverAsync(_fileListVM.SelectedFile.FilePath, null);
                _coverVM.Clear();
                _setHasUnsavedChanges(false);
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerCoverRemoveErrorTitle"), ex.Message, DialogKey.TagManagerCoverRemoveError);
            }
        }

        /// <summary>Schreibt das aktuelle Cover auf alle Dateien im Ordner nach Nutzerbestätigung.</summary>
        public async Task ApplyCoverToAllAsync()
        {
            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("TagApplyCoverAll");
            ApplyCoverToAllCallCount++;

            if (_coverVM.CoverImageData is null || _fileListVM.Files.Count == 0)
            {
                return;
            }

            if (!await ConfirmApplyCoverToAllAsync(_fileListVM.Files.Count))
            {
                return;
            }

            byte[] imageData = _coverVM.CoverImageData;
            string mimeType = _coverVM.CoverMimeType ?? "image/jpeg";

            _ = await TagBatchRunner.RunBatchAsync(
                _fileListVM.Files,
                file => _ctx.TagService.WriteCoverAsync(file.FilePath, imageData, mimeType),
                SafeResourceLoader.Get("TagManagerCoverApplyAllErrorTitle"),
                DialogKey.TagManagerCoverApplyAllError,
                _ctx.ErrorDialogService, _setIsLoading, _setBatchProgress);
        }

        private Task<bool> ConfirmApplyCoverToAllAsync(int fileCount) =>
            _ctx.ConfirmationDialogService.ConfirmAsync(
                SafeResourceLoader.Get("TagManagerCoverApplyAllConfirmTitle"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    PluralText.Pattern(
                        fileCount,
                        "TagManagerCoverApplyAllConfirmMessageSingular",
                        "TagManagerCoverApplyAllConfirmMessagePlural",
                        "Das aktuelle Cover wird in {0} Datei geschrieben.",
                        "Das aktuelle Cover wird in {0} Dateien geschrieben."),
                    fileCount),
                DialogKey.TagManagerCoverApplyAll);
    }
}

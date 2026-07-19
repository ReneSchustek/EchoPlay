using EchoPlay.App.Helpers;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-Actions: Cover-Operationen im Tag-Manager (Einzel-Entfernen, Batch-Apply).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1863:Use 'CompositeFormat'", Justification = "Format-Strings werden zur Laufzeit aus 'SafeResourceLoader.Get(...)' (Resources.resw) geladen und sind zum Kompilierzeitpunkt unbekannt.")]
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
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerCoverRemoveErrorTitle"), ex.Message);
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

            bool confirmed = await _ctx.ConfirmationDialogService.ConfirmAsync(
                SafeResourceLoader.Get("TagManagerCoverApplyAllConfirmTitle"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    SafeResourceLoader.Get("TagManagerCoverApplyAllConfirmMessage"),
                    _fileListVM.Files.Count));

            if (!confirmed)
            {
                return;
            }

            byte[] imageData = _coverVM.CoverImageData;
            string mimeType = _coverVM.CoverMimeType ?? "image/jpeg";

            await TagBatchRunner.RunBatchAsync(
                _fileListVM.Files,
                file => _ctx.TagService.WriteCoverAsync(file.FilePath, imageData, mimeType),
                SafeResourceLoader.Get("TagManagerCoverApplyAllErrorTitle"),
                _ctx.ErrorDialogService, _setIsLoading, _setBatchProgress);
        }
    }
}

using EchoPlay.App.Helpers;
using EchoPlay.Core.Models;
using EchoPlay.TagManager.Models;
using System.Globalization;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-Actions: Umbenennungs-Vorschau und -Ausführung im Tag-Manager.
    /// Nach erfolgreicher Umbenennung wird der Ordner über den übergebenen
    /// Callback neu geladen, damit die Dateinamen frisch angezeigt werden.
    /// </summary>
    internal sealed class TagRenameActions
    {
        private readonly TagManagerActionsContext _ctx;
        private readonly TagFileListViewModel _fileListVM;
        private readonly TagRenameViewModel _renameVM;
        private readonly Action<bool> _setIsLoading;
        private readonly Action _refreshCommandStates;
        private readonly Func<string, Task> _reloadFolderAsync;

        /// <summary>Public Call-Counter für Tests.</summary>
        public int PreviewRenameCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int ExecuteRenameCallCount { get; private set; }

        /// <summary>Richtet die Umbenennungsaktionen ein.</summary>
        /// <param name="context">Die Dienste des Tag-Managers.</param>
        /// <param name="fileListVM">Die Dateiliste mit dem aktuellen Ordner.</param>
        /// <param name="renameVM">Muster und Vorschau des Umbenennens.</param>
        /// <param name="setIsLoading">Schaltet die Ladeanzeige.</param>
        /// <param name="refreshCommandStates">Aktualisiert die Befehle nach der Vorschau.</param>
        /// <param name="reloadFolderAsync">Lädt den Ordner nach dem Umbenennen neu.</param>
        public TagRenameActions(
            TagManagerActionsContext context,
            TagFileListViewModel fileListVM,
            TagRenameViewModel renameVM,
            Action<bool> setIsLoading,
            Action refreshCommandStates,
            Func<string, Task> reloadFolderAsync)
        {
            _ctx = context;
            _fileListVM = fileListVM;
            _renameVM = renameVM;
            _setIsLoading = setIsLoading;
            _refreshCommandStates = refreshCommandStates;
            _reloadFolderAsync = reloadFolderAsync;
        }

        /// <summary>Berechnet die Umbenennungs-Vorschau aus dem aktuellen Ordner und Muster.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Rename-Vorschau: TagLib-Lesefehler oder IO-Fehler werden als Nutzer-Fehlermeldung angezeigt, damit der Command nicht reißt.")]
        public async Task PreviewRenameAsync()
        {
            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("TagRenamePreview");
            PreviewRenameCallCount++;

            if (string.IsNullOrEmpty(_fileListVM.CurrentFolderPath))
            {
                return;
            }

            _setIsLoading(true);
            try
            {
                IReadOnlyList<(string FilePath, AudioTag Tag)> filesWithTags =
                    await _ctx.TagService.ReadFolderAsync(_fileListVM.CurrentFolderPath);

                _renameVM.SetPreviewItems(_ctx.FileRenameService.BuildPreview(filesWithTags, _renameVM.RenamePattern));
                _refreshCommandStates();
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerPreviewErrorTitle"), ex.Message, DialogKey.TagManagerPreviewError);
            }
            finally
            {
                _setIsLoading(false);
            }
        }

        /// <summary>Führt die Umbenennung aller Dateien nach Nutzerbestätigung durch.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Rename-Ausführung: IO-Fehler einzelner Dateien (File.Move, UnauthorizedAccess, PathTooLong) werden geloggt und die verbleibenden Dateien werden dennoch umbenannt.")]
        public async Task ExecuteRenameAsync()
        {
            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("TagRenameExecute");
            ExecuteRenameCallCount++;

            if (string.IsNullOrEmpty(_fileListVM.CurrentFolderPath))
            {
                return;
            }

            int previewCount = _renameVM.PreviewItems.Count;
            if (!await ConfirmRenameAsync(previewCount))
            {
                return;
            }

            _setIsLoading(true);
            try
            {
                await RenameFolderAsync(_fileListVM.CurrentFolderPath, previewCount);
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerRenameErrorTitle"), ex.Message, DialogKey.TagManagerRenameError);
            }
            finally
            {
                _setIsLoading(false);
            }
        }

        private async Task RenameFolderAsync(string folderPath, int previewCount)
        {
            IReadOnlyList<(string FilePath, AudioTag Tag)> filesWithTags =
                await _ctx.TagService.ReadFolderAsync(folderPath);

            int renamedCount = await _ctx.FileRenameService.RenameAsync(filesWithTags, _renameVM.RenamePattern);

            // Ordner neu laden, denn die Dateinamen haben sich verändert
            await _reloadFolderAsync(folderPath);

            if (renamedCount < previewCount)
            {
                await ShowPartialRenameAsync(renamedCount, previewCount);
            }
        }

        /// <summary>Fragt nach, bevor umbenannt wird; der Vorgang lässt sich nicht rückgängig machen.</summary>
        private Task<bool> ConfirmRenameAsync(int previewCount) => _ctx.ConfirmationDialogService.ConfirmAsync(
            SafeResourceLoader.Get("TagManagerRenameConfirmTitle"),
            string.Format(
                CultureInfo.CurrentCulture,
                PluralText.Pattern(
                    previewCount,
                    "TagManagerRenameConfirmMessageSingular",
                    "TagManagerRenameConfirmMessagePlural",
                    "{0} Datei wird nach dem Muster „{1}\" umbenannt. Dieser Vorgang kann nicht rückgängig gemacht werden.",
                    "{0} Dateien werden nach dem Muster „{1}\" umbenannt. Dieser Vorgang kann nicht rückgängig gemacht werden."),
                previewCount, _renameVM.RenamePattern),
            DialogKey.TagManagerRename);

        /// <summary>Meldet, dass nur ein Teil der Dateien umbenannt wurde; die Einzelheiten stehen im Protokoll.</summary>
        private Task ShowPartialRenameAsync(int renamedCount, int previewCount) => _ctx.ErrorDialogService.ShowAsync(
            SafeResourceLoader.Get("TagManagerRenamePartialErrorTitle"),
            string.Format(
                CultureInfo.CurrentCulture,
                PluralText.Pattern(
                    previewCount,
                    "TagManagerRenamePartialErrorMessageSingular",
                    "TagManagerRenamePartialErrorMessagePlural",
                    "{0} von {1} Datei wurde umbenannt. Details im Protokoll.",
                    "{0} von {1} Dateien wurden umbenannt. Details im Protokoll."),
                renamedCount, previewCount),
            DialogKey.TagManagerRenamePartialError);
    }
}

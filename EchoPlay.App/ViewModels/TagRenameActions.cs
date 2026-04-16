using EchoPlay.App.Helpers;
using EchoPlay.TagManager.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-Actions: Umbenennungs-Vorschau und -Ausführung im Tag-Manager.
    /// Nach erfolgreicher Umbenennung wird der Ordner über den übergebenen
    /// Callback neu geladen, damit die Dateinamen frisch angezeigt werden.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1863:Use 'CompositeFormat'", Justification = "Format-Strings werden zur Laufzeit aus 'SafeResourceLoader.Get(...)' (Resources.resw) geladen und sind zum Kompilierzeitpunkt unbekannt.")]
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Rename-Vorschau: TagLib-Lesefehler oder IO-Fehler werden als Nutzer-Fehlermeldung angezeigt, damit der Command nicht reisst.")]
        public async Task PreviewRenameAsync()
        {
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
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerPreviewErrorTitle"), ex.Message);
            }
            finally
            {
                _setIsLoading(false);
            }
        }

        /// <summary>Führt die Umbenennung aller Dateien nach Nutzerbestätigung durch.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Rename-Ausfuehrung: IO-Fehler einzelner Dateien (File.Move, UnauthorizedAccess, PathTooLong) werden geloggt und die verbleibenden Dateien werden dennoch umbenannt.")]
        public async Task ExecuteRenameAsync()
        {
            ExecuteRenameCallCount++;

            if (string.IsNullOrEmpty(_fileListVM.CurrentFolderPath))
            {
                return;
            }

            int previewCount = _renameVM.PreviewItems.Count;

            bool confirmed = await _ctx.ConfirmationDialogService.ConfirmAsync(
                SafeResourceLoader.Get("TagManagerRenameConfirmTitle"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    SafeResourceLoader.Get("TagManagerRenameConfirmMessage"),
                    previewCount, _renameVM.RenamePattern));

            if (!confirmed)
            {
                return;
            }

            _setIsLoading(true);
            try
            {
                IReadOnlyList<(string FilePath, AudioTag Tag)> filesWithTags =
                    await _ctx.TagService.ReadFolderAsync(_fileListVM.CurrentFolderPath);

                int renamedCount = await _ctx.FileRenameService.RenameAsync(filesWithTags, _renameVM.RenamePattern);

                // Ordner neu laden – Dateinamen haben sich verändert
                await _reloadFolderAsync(_fileListVM.CurrentFolderPath);

                if (renamedCount < previewCount)
                {
                    await _ctx.ErrorDialogService.ShowAsync(
                        SafeResourceLoader.Get("TagManagerRenamePartialErrorTitle"),
                        string.Format(
                            CultureInfo.CurrentCulture,
                            SafeResourceLoader.Get("TagManagerRenamePartialErrorMessage"),
                            renamedCount, previewCount));
                }
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerRenameErrorTitle"), ex.Message);
            }
            finally
            {
                _setIsLoading(false);
            }
        }
    }
}

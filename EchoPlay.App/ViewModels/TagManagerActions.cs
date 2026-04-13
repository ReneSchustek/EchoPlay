using EchoPlay.App.Helpers;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Interner Orchestrator für die Async-Aktionen des Tag-Managers.
    /// Enthält alle Operationen, die Services (<see cref="ITagService"/>, Dialoge, Lookup)
    /// mit den Sub-VMs verdrahten: Ordnerladen, Tag-Laden, Speichern (einzeln und batch),
    /// MusicBrainz-Lookup, Cover-Batch-Apply, Umbenennen. Das Top-VM
    /// <see cref="TagManagerViewModel"/> hält nur noch Commands, Zustands-Properties und
    /// die Pass-Through-Schicht. Die Aufteilung folgt dem Muster aus dem Dashboard-Refactor
    /// (<see cref="DashboardDataLoader"/>).
    /// </summary>
    internal sealed class TagManagerActions
    {
        // SafeResourceLoader statt statischem ResourceLoader, weil der statische Initializer
        // WinUI-COM-Infrastruktur im Testhost initialisiert und beim Cleanup einen Crash auslöst.

        private readonly TagManagerActionsContext _ctx;

        private readonly TagFileListViewModel _fileListVM;
        private readonly TagEditorFieldsViewModel _editorVM;
        private readonly TagCoverViewModel _coverVM;
        private readonly TagRenameViewModel _renameVM;

        private readonly Action<bool> _setIsLoading;
        private readonly Action<bool> _setIsLookingUp;
        private readonly Action<string> _setAutoLookupStatus;
        private readonly Action<string> _setBatchProgress;
        private readonly Action<bool> _setHasUnsavedChanges;
        private readonly Action _refreshCommandStates;

        // Letzte Lookup-Trefferliste – wird vorgehalten, damit die Page nur Indizes
        // zurückgeben muss und keine TagManager-Typen kennen muss.
        private IReadOnlyList<TagLookupResult> _lastLookupResults = [];
        private TaskCompletionSource<bool>? _autoLookupCompletedSource;
        private TaskCompletionSource<bool>? _fileLoadCompletedSource;

        /// <summary>
        /// Initialisiert den Orchestrator mit dem Service-Context, Sub-VMs und Zustands-Callbacks.
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
            _ctx = context;

            _fileListVM = fileListVM;
            _editorVM   = editorVM;
            _coverVM    = coverVM;
            _renameVM   = renameVM;

            _setIsLoading          = setIsLoading;
            _setIsLookingUp        = setIsLookingUp;
            _setAutoLookupStatus   = setAutoLookupStatus;
            _setBatchProgress      = setBatchProgress;
            _setHasUnsavedChanges  = setHasUnsavedChanges;
            _refreshCommandStates  = refreshCommandStates;
        }

        /// <summary>Wartet auf den Abschluss des laufenden Auto-Lookups (für deterministische Tests).</summary>
        internal Task WaitForAutoLookupCompleteAsync()
            => _autoLookupCompletedSource?.Task ?? Task.CompletedTask;

        /// <summary>Wartet auf den Abschluss des laufenden Datei-Ladevorgangs (für deterministische Tests).</summary>
        internal Task WaitForFileLoadCompleteAsync()
            => _fileLoadCompletedSource?.Task ?? Task.CompletedTask;

        /// <summary>Events für die Page – werden vom Top-VM an den Nutzer weitergereicht.</summary>
        public event EventHandler<IReadOnlyList<TagLookupCandidate>>? LookupResultsReady;

        /// <inheritdoc cref="LookupResultsReady"/>
        public event EventHandler<TagLookupCandidate>? AutoLookupApplied;

        /// <inheritdoc cref="LookupResultsReady"/>
        public event EventHandler? RenamePreviewReady;

        /// <summary>
        /// Wendet das Lookup-Ergebnis mit dem angegebenen Index aus der zuletzt gelieferten
        /// Trefferliste auf die Editor-Felder an.
        /// </summary>
        public void ApplyLookupCandidate(int index)
        {
            if (index < 0 || index >= _lastLookupResults.Count)
            {
                return;
            }

            ApplyLookupResult(_lastLookupResults[index]);
        }

        /// <summary>
        /// Übernimmt ein Lookup-Ergebnis direkt. Vom Tests-Projekt aus erreichbar.
        /// </summary>
        public void ApplyLookupResult(TagLookupResult result)
        {
            _editorVM.ApplyLookupResult(result);
            _setHasUnsavedChanges(true);
            _refreshCommandStates();
        }

        // ── Laden ──────────────────────────────────────────────────────────────

        /// <summary>Lädt alle Audiodateien eines Ordners und setzt die Sub-VMs zurück.</summary>
        public async Task LoadFolderAsync(string folderPath)
        {
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
        public async Task LoadFileTagsAsync(TagFileItemViewModel file)
        {
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
        public async Task LoadMultipleFileTagsAsync(IReadOnlyList<TagFileItemViewModel> files)
        {
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

        // ── Speichern ──────────────────────────────────────────────────────────

        /// <summary>
        /// Speichert die aktuell angezeigten Tags. Bei Einzelauswahl werden alle Felder
        /// geschrieben; bei Mehrfachauswahl nur die vom Nutzer geänderten auf alle selektierten Dateien.
        /// </summary>
        public async Task SaveAsync()
        {
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
            await RunBatchAsync(_fileListVM.SelectedFiles, async file =>
            {
                AudioTag existingTag = await _ctx.TagService.ReadAsync(file.FilePath);
                AudioTag mergedTag   = MergeEditedIntoExisting(editedTag, existingTag);
                await _ctx.TagService.WriteAsync(file.FilePath, mergedTag);
                file.IsModified = false;
            }, SafeResourceLoader.Get("TagManagerSaveErrorTitle"));

            _setHasUnsavedChanges(false);
        }

        /// <summary>Speichert alle modifizierten Dateien auf einmal nach Nutzerbestätigung.</summary>
        public async Task SaveAllAsync()
        {
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
            await RunBatchAsync(modifiedFiles, async file =>
            {
                AudioTag existingTag = await _ctx.TagService.ReadAsync(file.FilePath);
                AudioTag mergedTag   = MergeSharedIntoExisting(sharedTag, existingTag);
                await _ctx.TagService.WriteAsync(file.FilePath, mergedTag);
                file.IsModified = false;
            }, SafeResourceLoader.Get("TagManagerSaveErrorTitle"));

            _setHasUnsavedChanges(false);
        }

        /// <summary>Entfernt alle Tags der ausgewählten Datei nach Nutzerbestätigung.</summary>
        public async Task RemoveAllTagsAsync()
        {
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

        // ── Lookup ─────────────────────────────────────────────────────────────

        /// <summary>Führt einen manuellen Online-Lookup anhand des Titels oder Dateinamens aus.</summary>
        public async Task LookupOnlineAsync()
        {
            if (!_fileListVM.HasSelectedFile)
            {
                return;
            }

            using IDisposable? onlineScope = await _ctx.OnlineAccessGuard.RequestOnlineAccessAsync();
            if (onlineScope is null)
            {
                return;
            }

            TagFileItemViewModel firstSelected = _fileListVM.SelectedFile ?? _fileListVM.SelectedFiles[0];
            string query = !string.IsNullOrWhiteSpace(_editorVM.Title) && _editorVM.Title != "(verschieden)"
                ? _editorVM.Title
                : Path.GetFileNameWithoutExtension(firstSelected.FilePath);

            _setIsLookingUp(true);
            try
            {
                IReadOnlyList<TagLookupResult> results = await _ctx.LookupCoordinator.SearchAsync(query);
                _lastLookupResults = results;
                LookupResultsReady?.Invoke(this, ToCandidates(results));
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerLookupErrorTitle"), ex.Message);
            }
            finally
            {
                _setIsLookingUp(false);
            }
        }

        /// <summary>
        /// Führt einen automatischen Lookup aus dem Ordnerkontext aus. Bei eindeutigem Treffer
        /// werden die Tags direkt übernommen; ansonsten wird ein Auswahl-Dialog angefordert.
        /// </summary>
        public async Task AutoLookupAsync()
        {
            _autoLookupCompletedSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            string query = _ctx.LookupCoordinator.BuildAutoLookupQuery(_fileListVM.CurrentFolderPath);
            if (string.IsNullOrWhiteSpace(query))
            {
                _ = _autoLookupCompletedSource.TrySetResult(true);
                return;
            }

            using IDisposable? onlineScope = await _ctx.OnlineAccessGuard.RequestOnlineAccessAsync();
            if (onlineScope is null)
            {
                _ = _autoLookupCompletedSource.TrySetResult(true);
                return;
            }

            _setAutoLookupStatus(string.Format(
                CultureInfo.CurrentCulture,
                SafeResourceLoader.Get("TagManagerAutoLookupSearchingText"),
                query));
            _setIsLookingUp(true);

            try
            {
                IReadOnlyList<TagLookupResult> results = await _ctx.LookupCoordinator.SearchAsync(query);

                int loadedTrackCount   = _fileListVM.Files.Count;
                TagLookupResult? match = _ctx.LookupCoordinator.SelectBestMatch(results, loadedTrackCount);

                if (match is not null && match.TrackCount.HasValue
                    && match.TrackCount.Value == (uint)loadedTrackCount)
                {
                    ApplyLookupResult(match);
                    _setAutoLookupStatus(string.Empty);
                    AutoLookupApplied?.Invoke(this, ToCandidate(match, 0));
                }
                else
                {
                    _setAutoLookupStatus(string.Empty);

                    List<TagLookupResult> sorted = [.. results
                        .OrderByDescending(r => r.TrackCount.HasValue && r.TrackCount.Value == (uint)loadedTrackCount)
                        .ThenByDescending(r => r.TrackCount.HasValue)];

                    _lastLookupResults = sorted;
                    LookupResultsReady?.Invoke(this, ToCandidates(sorted));
                }
            }
            catch (Exception ex)
            {
                _setAutoLookupStatus(string.Empty);
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerAutoLookupErrorTitle"), ex.Message);
            }
            finally
            {
                _setIsLookingUp(false);
                _ = _autoLookupCompletedSource?.TrySetResult(true);
            }
        }

        /// <summary>Wendet die zwischengespeicherten Lookup-Tags auf alle Dateien im Ordner an.</summary>
        public async Task ApplyToAllAsync()
        {
            AudioTag? pendingTag = _editorVM.PendingBatchTag;
            if (pendingTag is null || _fileListVM.Files.Count == 0)
            {
                return;
            }

            bool confirmed = await _ctx.ConfirmationDialogService.ConfirmAsync(
                SafeResourceLoader.Get("TagManagerApplyToAllConfirmTitle"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    SafeResourceLoader.Get("TagManagerApplyToAllConfirmMessage"),
                    _fileListVM.Files.Count));

            if (!confirmed)
            {
                return;
            }

            await RunBatchAsync(_fileListVM.Files, async file =>
            {
                AudioTag existingTag = await _ctx.TagService.ReadAsync(file.FilePath);
                AudioTag mergedTag   = MergeSharedIntoExisting(pendingTag, existingTag);
                await _ctx.TagService.WriteAsync(file.FilePath, mergedTag);
                file.IsModified = false;
            }, SafeResourceLoader.Get("TagManagerApplyToAllErrorTitle"));

            _editorVM.ClearPendingBatchTag();
            _setHasUnsavedChanges(false);
            _refreshCommandStates();

            // Workflow-Verkettung: nach „Alle taggen" automatisch Rename-Vorschau aktualisieren
            if (!string.IsNullOrWhiteSpace(_renameVM.RenamePattern) && !string.IsNullOrEmpty(_fileListVM.CurrentFolderPath))
            {
                await PreviewRenameAsync();
                RenamePreviewReady?.Invoke(this, EventArgs.Empty);
            }
        }

        // ── Cover ──────────────────────────────────────────────────────────────

        /// <summary>Entfernt das Cover der aktuell ausgewählten Datei.</summary>
        public async Task RemoveCoverAsync()
        {
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
            string mimeType  = _coverVM.CoverMimeType ?? "image/jpeg";

            await RunBatchAsync(_fileListVM.Files,
                file => _ctx.TagService.WriteCoverAsync(file.FilePath, imageData, mimeType),
                SafeResourceLoader.Get("TagManagerCoverApplyAllErrorTitle"));
        }

        // ── Rename ─────────────────────────────────────────────────────────────

        /// <summary>Berechnet die Umbenennungs-Vorschau aus dem aktuellen Ordner und Muster.</summary>
        public async Task PreviewRenameAsync()
        {
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
        public async Task ExecuteRenameAsync()
        {
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
                await LoadFolderAsync(_fileListVM.CurrentFolderPath);

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

        // ── Interne Helfer ─────────────────────────────────────────────────────

        /// <summary>
        /// Führt eine Batch-Operation mit Fortschrittsanzeige und Fehlerdialog aus.
        /// Setzt <c>IsLoading</c>, aktualisiert <c>BatchProgressText</c> und räumt im Finally auf.
        /// </summary>
        private async Task RunBatchAsync(
            IReadOnlyList<TagFileItemViewModel> files,
            Func<TagFileItemViewModel, Task> perFile,
            string errorTitle)
        {
            _setIsLoading(true);
            int processed = 0;

            try
            {
                foreach (TagFileItemViewModel file in files)
                {
                    processed++;
                    _setBatchProgress(string.Format(
                        CultureInfo.CurrentCulture,
                        SafeResourceLoader.Get("TagManagerBatchProgressText"),
                        processed, files.Count));

                    await perFile(file);
                }
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(errorTitle, ex.Message);
            }
            finally
            {
                _setBatchProgress(string.Empty);
                _setIsLoading(false);
            }
        }

        /// <summary>
        /// Verschmilzt nur die vom Nutzer geänderten Felder in die bestehenden Tags einer Datei.
        /// </summary>
        private static AudioTag MergeEditedIntoExisting(AudioTag edited, AudioTag existing)
        {
            return new AudioTag
            {
                Title       = edited.Title       ?? existing.Title,
                Album       = edited.Album       ?? existing.Album,
                Artist      = edited.Artist      ?? existing.Artist,
                AlbumArtist = edited.AlbumArtist ?? existing.AlbumArtist,
                Genre       = edited.Genre       ?? existing.Genre,
                Year        = edited.Year        ?? existing.Year,
                TrackNumber = edited.TrackNumber ?? existing.TrackNumber,
                TrackCount  = edited.TrackCount  ?? existing.TrackCount
            };
        }

        /// <summary>
        /// Verschmilzt gemeinsame Tags in die bestehenden Tags einer Datei.
        /// Title und TrackNumber stammen aus der Datei, alle anderen Felder aus dem Batch-Tag.
        /// </summary>
        private static AudioTag MergeSharedIntoExisting(AudioTag shared, AudioTag existing)
        {
            return new AudioTag
            {
                Title       = existing.Title,
                TrackNumber = existing.TrackNumber,
                Album       = shared.Album       ?? existing.Album,
                Artist      = shared.Artist      ?? existing.Artist,
                AlbumArtist = shared.AlbumArtist ?? existing.AlbumArtist,
                Genre       = shared.Genre       ?? existing.Genre,
                Year        = shared.Year        ?? existing.Year,
                TrackCount  = shared.TrackCount  ?? existing.TrackCount
            };
        }

        private static TagLookupCandidate ToCandidate(TagLookupResult result, int index) =>
            new(index, result.Title, result.Artist, result.Album, result.Year, result.TrackCount, result.Genre, result.Source);

        private static IReadOnlyList<TagLookupCandidate> ToCandidates(IReadOnlyList<TagLookupResult> results)
        {
            List<TagLookupCandidate> candidates = new(results.Count);
            for (int i = 0; i < results.Count; i++)
            {
                candidates.Add(ToCandidate(results[i], i));
            }
            return candidates;
        }
    }
}

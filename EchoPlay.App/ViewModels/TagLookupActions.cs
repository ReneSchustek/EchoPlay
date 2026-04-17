using EchoPlay.App.Helpers;
using EchoPlay.App.Models;
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
    /// Sub-Actions: MusicBrainz-Lookup (manuell und automatisch) sowie das Anwenden
    /// eines Lookup-Ergebnisses auf alle Dateien im Ordner.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1863:Use 'CompositeFormat'", Justification = "Format-Strings werden zur Laufzeit aus 'SafeResourceLoader.Get(...)' (Resources.resw) geladen und sind zum Kompilierzeitpunkt unbekannt.")]
    internal sealed class TagLookupActions
    {
        private readonly TagManagerActionsContext _ctx;
        private readonly TagFileListViewModel _fileListVM;
        private readonly TagEditorFieldsViewModel _editorVM;
        private readonly TagRenameViewModel _renameVM;
        private readonly Action<bool> _setIsLoading;
        private readonly Action<bool> _setIsLookingUp;
        private readonly Action<string> _setAutoLookupStatus;
        private readonly Action<string> _setBatchProgress;
        private readonly Action<bool> _setHasUnsavedChanges;
        private readonly Action _refreshCommandStates;
        private readonly Func<Task> _previewRenameAsync;

        private IReadOnlyList<TagLookupResult> _lastLookupResults = [];
        private TaskCompletionSource<bool>? _autoLookupCompletedSource;

        /// <summary>Public Call-Counter für Tests.</summary>
        public int LookupOnlineCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int AutoLookupCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int ApplyToAllCallCount { get; private set; }

        /// <summary>Events für die Page – werden vom Top-VM an den Nutzer weitergereicht.</summary>
        public event EventHandler<IReadOnlyList<TagLookupCandidate>>? LookupResultsReady;

        /// <inheritdoc cref="LookupResultsReady"/>
        public event EventHandler<TagLookupCandidate>? AutoLookupApplied;

        /// <inheritdoc cref="LookupResultsReady"/>
        public event EventHandler? RenamePreviewReady;

        public TagLookupActions(
            TagManagerActionsContext context,
            TagFileListViewModel fileListVM,
            TagEditorFieldsViewModel editorVM,
            TagRenameViewModel renameVM,
            Action<bool> setIsLoading,
            Action<bool> setIsLookingUp,
            Action<string> setAutoLookupStatus,
            Action<string> setBatchProgress,
            Action<bool> setHasUnsavedChanges,
            Action refreshCommandStates,
            Func<Task> previewRenameAsync)
        {
            _ctx = context;
            _fileListVM = fileListVM;
            _editorVM = editorVM;
            _renameVM = renameVM;
            _setIsLoading = setIsLoading;
            _setIsLookingUp = setIsLookingUp;
            _setAutoLookupStatus = setAutoLookupStatus;
            _setBatchProgress = setBatchProgress;
            _setHasUnsavedChanges = setHasUnsavedChanges;
            _refreshCommandStates = refreshCommandStates;
            _previewRenameAsync = previewRenameAsync;
        }

        /// <summary>Wartet auf den Abschluss des laufenden Auto-Lookups (für deterministische Tests).</summary>
        internal Task WaitForAutoLookupCompleteAsync()
            => _autoLookupCompletedSource?.Task ?? Task.CompletedTask;

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

        /// <summary>Übernimmt ein Lookup-Ergebnis direkt. Vom Tests-Projekt aus erreichbar.</summary>
        public void ApplyLookupResult(TagLookupResult result)
        {
            _editorVM.ApplyLookupResult(result);
            _setHasUnsavedChanges(true);
            _refreshCommandStates();
        }

        /// <summary>Führt einen manuellen Online-Lookup anhand des Titels oder Dateinamens aus.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "MusicBrainz-Lookup: HTTP-/Parser-/Timeout-Fehler werden als Nutzer-Fehlermeldung angezeigt und der Lookup-Command kehrt zurueck.")]
        public async Task LookupOnlineAsync()
        {
            LookupOnlineCallCount++;

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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Auto-Lookup (MusicBrainz + Cover Art Archive): HTTP-/Parser-/Timeout-Fehler werden als Nutzer-Status angezeigt und der Command kehrt zurueck.")]
        public async Task AutoLookupAsync()
        {
            AutoLookupCallCount++;

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

                int loadedTrackCount = _fileListVM.Files.Count;
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
            ApplyToAllCallCount++;

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

            await TagBatchRunner.RunBatchAsync(
                _fileListVM.Files,
                async file =>
                {
                    AudioTag existingTag = await _ctx.TagService.ReadAsync(file.FilePath);
                    AudioTag mergedTag = TagBatchRunner.MergeSharedIntoExisting(pendingTag, existingTag);
                    await _ctx.TagService.WriteAsync(file.FilePath, mergedTag);
                    file.IsModified = false;
                },
                SafeResourceLoader.Get("TagManagerApplyToAllErrorTitle"),
                _ctx.ErrorDialogService, _setIsLoading, _setBatchProgress);

            _editorVM.ClearPendingBatchTag();
            _setHasUnsavedChanges(false);
            _refreshCommandStates();

            // Workflow-Verkettung: nach „Alle taggen" automatisch Rename-Vorschau aktualisieren
            if (!string.IsNullOrWhiteSpace(_renameVM.RenamePattern) && !string.IsNullOrEmpty(_fileListVM.CurrentFolderPath))
            {
                await _previewRenameAsync();
                RenamePreviewReady?.Invoke(this, EventArgs.Empty);
            }
        }

        private static TagLookupCandidate ToCandidate(TagLookupResult result, int index) =>
            new(index, result.Title, result.Artist, result.Album, result.Year, result.TrackCount, result.Genre, result.Source);

        private static List<TagLookupCandidate> ToCandidates(IReadOnlyList<TagLookupResult> results)
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

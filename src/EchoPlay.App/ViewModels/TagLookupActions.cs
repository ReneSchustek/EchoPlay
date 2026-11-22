using EchoPlay.App.Helpers;
using EchoPlay.App.Models;
using EchoPlay.Core.Models;
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

        /// <summary>Events für die Page; werden vom Top-VM an den Nutzer weitergereicht.</summary>
        public event EventHandler<IReadOnlyList<TagLookupCandidate>>? LookupResultsReady;

        /// <inheritdoc cref="LookupResultsReady"/>
        public event EventHandler<TagLookupCandidate>? AutoLookupApplied;

        /// <inheritdoc cref="LookupResultsReady"/>
        public event EventHandler? RenamePreviewReady;

        /// <summary>Richtet die Abfrage bei MusicBrainz ein.</summary>
        /// <param name="context">Die Dienste des Tag-Managers.</param>
        /// <param name="fileListVM">Die Dateiliste mit Auswahl und Ordner.</param>
        /// <param name="editorVM">Die Eingabefelder.</param>
        /// <param name="renameVM">Das Umbenennungsmuster für die anschließende Vorschau.</param>
        /// <param name="setIsLoading">Schaltet die Ladeanzeige.</param>
        /// <param name="setIsLookingUp">Schaltet die Anzeige der laufenden Abfrage.</param>
        /// <param name="setAutoLookupStatus">Setzt den Statustext der automatischen Abfrage.</param>
        /// <param name="setBatchProgress">Setzt den Fortschrittstext.</param>
        /// <param name="setHasUnsavedChanges">Setzt die Markierung ungespeicherter Änderungen.</param>
        /// <param name="refreshCommandStates">Aktualisiert die Befehle.</param>
        /// <param name="previewRenameAsync">Baut die Vorschau des Umbenennens neu.</param>
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "MusicBrainz-Lookup: HTTP-/Parser-/Timeout-Fehler werden als Nutzer-Fehlermeldung angezeigt und der Lookup-Command kehrt zurück.")]
        public async Task LookupOnlineAsync()
        {
            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("TagLookupOnline");
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

            string query = BuildManualLookupQuery();
            _setIsLookingUp(true);
            try
            {
                IReadOnlyList<TagLookupResult> results = await _ctx.LookupCoordinator.SearchAsync(query);
                _lastLookupResults = results;
                LookupResultsReady?.Invoke(this, ToCandidates(results));
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerLookupErrorTitle"), ex.Message, DialogKey.TagManagerLookupError);
            }
            finally
            {
                _setIsLookingUp(false);
            }
        }

        /// <summary>
        /// Der Suchbegriff: der Titel aus dem Editor, wenn er eindeutig ist, sonst der Dateiname.
        /// </summary>
        private string BuildManualLookupQuery()
        {
            TagFileItemViewModel firstSelected = _fileListVM.SelectedFile ?? _fileListVM.SelectedFiles[0];
            return !string.IsNullOrWhiteSpace(_editorVM.Title) && !TagEditorFieldsViewModel.IsMixedValue(_editorVM.Title)
                ? _editorVM.Title
                : Path.GetFileNameWithoutExtension(firstSelected.FilePath);
        }

        /// <summary>
        /// Führt einen automatischen Lookup aus dem Ordnerkontext aus. Bei eindeutigem Treffer
        /// werden die Tags direkt übernommen; ansonsten wird ein Auswahl-Dialog angefordert.
        /// </summary>
        public async Task AutoLookupAsync()
        {
            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("TagAutoLookup");
            AutoLookupCallCount++;

            _autoLookupCompletedSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                await RunAutoLookupAsync();
            }
            finally
            {
                _ = _autoLookupCompletedSource?.TrySetResult(true);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Auto-Lookup (MusicBrainz + Cover Art Archive): HTTP-/Parser-/Timeout-Fehler werden als Nutzer-Status angezeigt und der Command kehrt zurück.")]
        private async Task RunAutoLookupAsync()
        {
            string query = _ctx.LookupCoordinator.BuildAutoLookupQuery(_fileListVM.CurrentFolderPath);
            using IDisposable? onlineScope = string.IsNullOrWhiteSpace(query)
                ? null
                : await _ctx.OnlineAccessGuard.RequestOnlineAccessAsync();
            if (onlineScope is null)
            {
                return;
            }

            string searchingPattern = SafeResourceLoader.Get("TagManagerAutoLookupSearchingText");
            _setAutoLookupStatus(string.Format(CultureInfo.CurrentCulture, searchingPattern, query));
            _setIsLookingUp(true);

            try
            {
                IReadOnlyList<TagLookupResult> results = await _ctx.LookupCoordinator.SearchAsync(query);
                _setAutoLookupStatus(string.Empty);
                ApplyOrOfferResults(results);
            }
            catch (Exception ex)
            {
                _setAutoLookupStatus(string.Empty);
                await _ctx.ErrorDialogService.ShowAsync(SafeResourceLoader.Get("TagManagerAutoLookupErrorTitle"), ex.Message, DialogKey.TagManagerAutoLookupError);
            }
            finally
            {
                _setIsLookingUp(false);
            }
        }

        /// <summary>
        /// Übernimmt einen eindeutigen Treffer direkt, wenn seine Spurzahl zum Ordner passt;
        /// sonst gehen alle Treffer zur Auswahl, passende Spurzahl zuerst.
        /// </summary>
        private void ApplyOrOfferResults(IReadOnlyList<TagLookupResult> results)
        {
            int loadedTrackCount = _fileListVM.Files.Count;
            TagLookupResult? match = _ctx.LookupCoordinator.SelectBestMatch(results, loadedTrackCount);
            if (match is { TrackCount: uint trackCount } && trackCount == (uint)loadedTrackCount)
            {
                ApplyLookupResult(match);
                AutoLookupApplied?.Invoke(this, ToCandidate(match, 0));
                return;
            }

            List<TagLookupResult> sorted = [.. results
                .OrderByDescending(r => r.TrackCount.HasValue && r.TrackCount.Value == (uint)loadedTrackCount)
                .ThenByDescending(r => r.TrackCount.HasValue)];

            _lastLookupResults = sorted;
            LookupResultsReady?.Invoke(this, ToCandidates(sorted));
        }

        /// <summary>Wendet die zwischengespeicherten Lookup-Tags auf alle Dateien im Ordner an.</summary>
        public async Task ApplyToAllAsync()
        {
            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("TagApplyAll");
            ApplyToAllCallCount++;

            AudioTag? pendingTag = _editorVM.PendingBatchTag;
            if (pendingTag is null || _fileListVM.Files.Count == 0)
            {
                return;
            }

            if (!await ConfirmApplyToAllAsync(_fileListVM.Files.Count))
            {
                return;
            }

            int failed = await TagBatchRunner.RunBatchAsync(
                _fileListVM.Files,
                file => ApplySharedTagAsync(pendingTag, file),
                SafeResourceLoader.Get("TagManagerApplyToAllErrorTitle"),
                DialogKey.TagManagerApplyToAllError,
                _ctx.ErrorDialogService, _setIsLoading, _setBatchProgress);

            _editorVM.ClearPendingBatchTag();
            _setHasUnsavedChanges(failed > 0);
            _refreshCommandStates();

            // Workflow-Verkettung: nach „Alle taggen" automatisch Rename-Vorschau aktualisieren
            if (!string.IsNullOrWhiteSpace(_renameVM.RenamePattern) && !string.IsNullOrEmpty(_fileListVM.CurrentFolderPath))
            {
                await _previewRenameAsync();
                RenamePreviewReady?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Fragt nach, bevor die gemeinsamen Felder in allen Dateien überschrieben werden.</summary>
        private Task<bool> ConfirmApplyToAllAsync(int fileCount) => _ctx.ConfirmationDialogService.ConfirmAsync(
            SafeResourceLoader.Get("TagManagerApplyToAllConfirmTitle"),
            string.Format(
                CultureInfo.CurrentCulture,
                PluralText.Pattern(
                    fileCount,
                    "TagManagerApplyToAllConfirmMessageSingular",
                    "TagManagerApplyToAllConfirmMessagePlural",
                    "Album, Künstler, Jahr, Genre und Spurenzahl werden für {0} Datei überschrieben. Titel und Tracknummer bleiben erhalten.",
                    "Album, Künstler, Jahr, Genre und Spurenzahl werden für {0} Dateien überschrieben. Titel und Tracknummer bleiben erhalten."),
                fileCount),
            DialogKey.TagManagerApplyToAll);

        /// <summary>Schreibt die gemeinsamen Felder in eine Datei; Titel und Tracknummer bleiben.</summary>
        private async Task ApplySharedTagAsync(AudioTag sharedTag, TagFileItemViewModel file)
        {
            AudioTag existingTag = await _ctx.TagService.ReadAsync(file.FilePath);
            AudioTag mergedTag = TagBatchRunner.MergeSharedIntoExisting(sharedTag, existingTag);
            await _ctx.TagService.WriteAsync(file.FilePath, mergedTag);
            file.IsModified = false;
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

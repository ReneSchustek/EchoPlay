using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für den Tag-Manager.
    /// Koordiniert vier Sub-VMs (Dateiliste, Editor-Felder, Cover, Rename) und einen internen
    /// <see cref="TagManagerActions"/>-Orchestrator, der die gesamte Async-Aktions-Schicht
    /// (Laden, Speichern, MusicBrainz-Lookup, Umbenennen, Batch-Apply) kapselt.
    /// Das Top-VM hält nur noch Commands, Zustands-Properties (IsLoading, HasUnsavedChanges …)
    /// und die Pass-Through-Schicht für die unveränderte Page-XAML.
    /// </summary>
    public sealed class TagManagerViewModel : ObservableObject
    {
        private readonly TagManagerActions _actions;

        // Typed references sind nötig, um SetEnabled() aufrufen zu können
        private readonly RelayCommand _saveCommand;
        private readonly RelayCommand _saveAllCommand;
        private readonly RelayCommand _removeAllTagsCommand;
        private readonly RelayCommand _lookupOnlineCommand;
        private readonly RelayCommand _autoLookupCommand;
        private readonly RelayCommand _applyToAllCommand;
        private readonly RelayCommand _removeCoverCommand;
        private readonly RelayCommand _loadCoverCommand;
        private readonly RelayCommand _applyCoverToAllCommand;
        private readonly RelayCommand _previewRenameCommand;
        private readonly RelayCommand _executeRenameCommand;

        private bool _isLoading;
        private bool _isLookingUp;
        private bool _hasUnsavedChanges;
        private string _autoLookupStatusText = string.Empty;
        private string _batchProgressText = string.Empty;

        /// <summary>
        /// Initialisiert das ViewModel mit allen benötigten Diensten und erzeugt die vier Sub-VMs
        /// sowie den Aktions-Orchestrator.
        /// </summary>
        /// <param name="tagService">Liest und schreibt Metadaten in Audiodateien.</param>
        /// <param name="lookupCoordinator">App-Service für den Online-Lookup und die Query-Logik.</param>
        /// <param name="fileRenameService">Erstellt Umbenennungs-Vorschau und führt Umbenennung durch.</param>
        /// <param name="errorDialogService">Zeigt Fehlermeldungen als Dialog an.</param>
        /// <param name="confirmationDialogService">Zeigt Bestätigungsdialoge an.</param>
        /// <param name="onlineAccessGuard">Prüft den Offline-Modus und zeigt bei Bedarf einen Bestätigungsdialog.</param>
        public TagManagerViewModel(
            ITagService tagService,
            ITagLookupCoordinator lookupCoordinator,
            IFileRenameService fileRenameService,
            IErrorDialogService errorDialogService,
            IConfirmationDialogService confirmationDialogService,
            IOnlineAccessGuard onlineAccessGuard)
        {
            FileListVM = new TagFileListViewModel();
            EditorVM = new TagEditorFieldsViewModel(() => HasUnsavedChanges = true);
            CoverVM = new TagCoverViewModel(() => HasUnsavedChanges = true);
            RenameVM = new TagRenameViewModel();

            TagManagerActionsContext actionsContext = new(
                tagService, lookupCoordinator, fileRenameService,
                errorDialogService, confirmationDialogService, onlineAccessGuard);

            _actions = new TagManagerActions(
                actionsContext,
                FileListVM, EditorVM, CoverVM, RenameVM,
                setIsLoading: v => IsLoading = v,
                setIsLookingUp: v => IsLookingUp = v,
                setAutoLookupStatus: v => AutoLookupStatusText = v,
                setBatchProgress: v => BatchProgressText = v,
                setHasUnsavedChanges: v => HasUnsavedChanges = v,
                refreshCommandStates: RefreshCommandStates);

            // Sub-VM-Events an die Page weiterreichen
            _actions.LookupResultsReady += (s, e) => LookupResultsReady?.Invoke(this, e);
            _actions.AutoLookupApplied += (s, e) => AutoLookupApplied?.Invoke(this, e);
            _actions.RenamePreviewReady += (s, e) => RenamePreviewReady?.Invoke(this, e);

            // PropertyChanged durchreichen, damit die XAML-Bindings auf den Top-VM-Pass-Through-Properties
            // weiterhin aktualisiert werden, obwohl der Wert in einem Sub-VM liegt.
            FileListVM.PropertyChanged += OnSubVmPropertyChanged;
            EditorVM.PropertyChanged += OnSubVmPropertyChanged;
            CoverVM.PropertyChanged += OnSubVmPropertyChanged;
            RenameVM.PropertyChanged += OnSubVmPropertyChanged;

            // Auswahländerung in der Dateiliste → Tag-Felder neu laden
            FileListVM.SelectionChanged += OnFileSelectionChanged;

            _saveCommand = new RelayCommand(() => _ = _actions.SaveAsync());
            _saveAllCommand = new RelayCommand(() => _ = _actions.SaveAllAsync());
            _removeAllTagsCommand = new RelayCommand(() => _ = _actions.RemoveAllTagsAsync());
            _lookupOnlineCommand = new RelayCommand(() => _ = _actions.LookupOnlineAsync());
            _autoLookupCommand = new RelayCommand(() => _ = _actions.AutoLookupAsync());
            _applyToAllCommand = new RelayCommand(() => _ = _actions.ApplyToAllAsync());
            _removeCoverCommand = new RelayCommand(() => _ = _actions.RemoveCoverAsync());
            _loadCoverCommand = new RelayCommand(() => LoadCoverRequested?.Invoke(this, EventArgs.Empty));
            _applyCoverToAllCommand = new RelayCommand(() => _ = _actions.ApplyCoverToAllAsync());
            _previewRenameCommand = new RelayCommand(() => _ = _actions.PreviewRenameAsync());
            _executeRenameCommand = new RelayCommand(() => _ = _actions.ExecuteRenameAsync());

            SaveCommand = _saveCommand;
            SaveAllCommand = _saveAllCommand;
            RemoveAllTagsCommand = _removeAllTagsCommand;
            LookupOnlineCommand = _lookupOnlineCommand;
            AutoLookupCommand = _autoLookupCommand;
            ApplyToAllCommand = _applyToAllCommand;
            RemoveCoverCommand = _removeCoverCommand;
            LoadCoverCommand = _loadCoverCommand;
            ApplyCoverToAllCommand = _applyCoverToAllCommand;
            PreviewRenameCommand = _previewRenameCommand;
            ExecuteRenameCommand = _executeRenameCommand;

            RefreshCommandStates();
        }

        // ── Sub-VMs ─────────────────────────────────────────────────────────────

        /// <summary>Sub-VM für die Dateiliste und Ordnerauswahl.</summary>
        public TagFileListViewModel FileListVM { get; }

        /// <summary>Sub-VM für die Tag-Editor-Felder (Titel, Album, Interpret …).</summary>
        public TagEditorFieldsViewModel EditorVM { get; }

        /// <summary>Sub-VM für die Cover-Anzeige und -Verwaltung.</summary>
        public TagCoverViewModel CoverVM { get; }

        /// <summary>Sub-VM für Umbenennungs-Muster und -Vorschau.</summary>
        public TagRenameViewModel RenameVM { get; }

        // ── Events (Top-VM → Page) ──────────────────────────────────────────────

        /// <summary>
        /// Wird ausgelöst, wenn der Online-Lookup fertig ist. Die Page zeigt einen ContentDialog.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "EventHandler<IReadOnlyList<TagLookupCandidate>> liefert die Trefferliste direkt an die Page, die sie in einem ContentDialog anzeigt; ein zusaetzlicher EventArgs-Wrapper brachte keinen Mehrwert.")]
        public event EventHandler<IReadOnlyList<TagLookupCandidate>>? LookupResultsReady;

        /// <summary>
        /// Wird ausgelöst, wenn der Auto-Lookup einen eindeutigen Treffer gefunden hat.
        /// Die Page zeigt eine Bestätigungs-InfoBar.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "EventHandler<TagLookupCandidate> liefert den eindeutigen Auto-Lookup-Treffer direkt an die Page fuer die InfoBar; ein zusaetzlicher EventArgs-Wrapper brachte keinen Mehrwert.")]
        public event EventHandler<TagLookupCandidate>? AutoLookupApplied;

        /// <summary>Wird ausgelöst, wenn der Nutzer ein Cover per Datei-Picker laden möchte.</summary>
        public event EventHandler? LoadCoverRequested;

        /// <summary>Wird ausgelöst, wenn die Rename-Vorschau nach „Alle taggen" bereit ist.</summary>
        public event EventHandler? RenamePreviewReady;

        // ── Pass-Through-Eigenschaften: Dateiliste ──────────────────────────────

        /// <inheritdoc cref="TagFileListViewModel.Files"/>
        public ObservableCollection<TagFileItemViewModel> Files => FileListVM.Files;

        /// <inheritdoc cref="TagFileListViewModel.SelectedFile"/>
        public TagFileItemViewModel? SelectedFile => FileListVM.SelectedFile;

        /// <inheritdoc cref="TagFileListViewModel.SelectedFiles"/>
        public IReadOnlyList<TagFileItemViewModel> SelectedFiles => FileListVM.SelectedFiles;

        /// <inheritdoc cref="TagFileListViewModel.HasFiles"/>
        public bool HasFiles => FileListVM.HasFiles;

        /// <inheritdoc cref="TagFileListViewModel.HasSelectedFile"/>
        public bool HasSelectedFile => FileListVM.HasSelectedFile;

        // ── Pass-Through-Eigenschaften: Editor-Felder ───────────────────────────

        /// <inheritdoc cref="TagEditorFieldsViewModel.Title"/>
        public string? Title { get => EditorVM.Title; set => EditorVM.Title = value; }

        /// <inheritdoc cref="TagEditorFieldsViewModel.Album"/>
        public string? Album { get => EditorVM.Album; set => EditorVM.Album = value; }

        /// <inheritdoc cref="TagEditorFieldsViewModel.Artist"/>
        public string? Artist { get => EditorVM.Artist; set => EditorVM.Artist = value; }

        /// <inheritdoc cref="TagEditorFieldsViewModel.AlbumArtist"/>
        public string? AlbumArtist { get => EditorVM.AlbumArtist; set => EditorVM.AlbumArtist = value; }

        /// <inheritdoc cref="TagEditorFieldsViewModel.Year"/>
        public string? Year { get => EditorVM.Year; set => EditorVM.Year = value; }

        /// <inheritdoc cref="TagEditorFieldsViewModel.TrackNumber"/>
        public string? TrackNumber { get => EditorVM.TrackNumber; set => EditorVM.TrackNumber = value; }

        /// <inheritdoc cref="TagEditorFieldsViewModel.TrackCount"/>
        public string? TrackCount { get => EditorVM.TrackCount; set => EditorVM.TrackCount = value; }

        /// <inheritdoc cref="TagEditorFieldsViewModel.Genre"/>
        public string? Genre { get => EditorVM.Genre; set => EditorVM.Genre = value; }

        /// <inheritdoc cref="TagEditorFieldsViewModel.HasPendingBatchTag"/>
        public bool HasPendingBatchTag => EditorVM.HasPendingBatchTag;

        // ── Pass-Through-Eigenschaften: Cover ───────────────────────────────────

        /// <inheritdoc cref="TagCoverViewModel.CoverImage"/>
        public BitmapImage? CoverImage => CoverVM.CoverImage;

        /// <inheritdoc cref="TagCoverViewModel.CoverVisibility"/>
        public Visibility CoverVisibility => CoverVM.CoverVisibility;

        // ── Pass-Through-Eigenschaften: Rename ──────────────────────────────────

        /// <inheritdoc cref="TagRenameViewModel.RenamePattern"/>
        public string RenamePattern { get => RenameVM.RenamePattern; set => RenameVM.RenamePattern = value; }

        /// <inheritdoc cref="TagRenameViewModel.RenamePreview"/>
        public IReadOnlyList<RenamePreviewDisplay> RenamePreview => RenameVM.RenamePreview;

        /// <inheritdoc cref="TagRenameViewModel.RenamePreviewVisibility"/>
        public Visibility RenamePreviewVisibility => RenameVM.RenamePreviewVisibility;

        // ── Zustand am Top-VM ───────────────────────────────────────────────────

        /// <summary>Gibt an, ob gerade Tags geladen, gespeichert oder eine Batch-Operation läuft.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    OnPropertyChanged(nameof(IsLoadingVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Lade-Indikators.</summary>
        public Visibility IsLoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Gibt an, ob gerade eine MusicBrainz-Suche läuft.</summary>
        public bool IsLookingUp
        {
            get => _isLookingUp;
            private set
            {
                if (SetProperty(ref _isLookingUp, value))
                {
                    OnPropertyChanged(nameof(IsLookingUpVisibility));
                    RefreshCommandStates();
                }
            }
        }

        /// <summary>Sichtbarkeit des Lookup-Indikators.</summary>
        public Visibility IsLookingUpVisibility => IsLookingUp ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Statustext des Auto-Lookups – zeigt die verwendete Suchanfrage während der Suche,
        /// danach eine leere Zeichenkette.
        /// </summary>
        public string AutoLookupStatusText
        {
            get => _autoLookupStatusText;
            private set
            {
                if (SetProperty(ref _autoLookupStatusText, value))
                {
                    OnPropertyChanged(nameof(AutoLookupStatusVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Auto-Lookup-Statustexts.</summary>
        public Visibility AutoLookupStatusVisibility =>
            string.IsNullOrEmpty(_autoLookupStatusText) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Fortschrittstext für Batch-Operationen.</summary>
        public string BatchProgressText
        {
            get => _batchProgressText;
            private set
            {
                if (SetProperty(ref _batchProgressText, value))
                {
                    OnPropertyChanged(nameof(BatchProgressVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Batch-Fortschrittstexts.</summary>
        public Visibility BatchProgressVisibility =>
            string.IsNullOrEmpty(_batchProgressText) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>
        /// Gibt an, ob ungespeicherte Änderungen vorhanden sind. Wird bei jeder Feld- oder
        /// Cover-Änderung auf <c>true</c> gesetzt und nach dem Speichern zurückgesetzt.
        /// </summary>
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                if (SetProperty(ref _hasUnsavedChanges, value))
                {
                    RefreshCommandStates();
                }
            }
        }

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>Speichert die aktuell angezeigten Tags in die ausgewählte Datei.</summary>
        public ICommand SaveCommand { get; }

        /// <summary>Speichert alle modifizierten Dateien auf einmal.</summary>
        public ICommand SaveAllCommand { get; }

        /// <summary>Entfernt alle Tags der ausgewählten Datei nach Bestätigung.</summary>
        public ICommand RemoveAllTagsCommand { get; }

        /// <summary>Sucht online nach Tags bei MusicBrainz.</summary>
        public ICommand LookupOnlineCommand { get; }

        /// <summary>Baut die Suchanfrage automatisch aus dem Ordnerkontext.</summary>
        public ICommand AutoLookupCommand { get; }

        /// <summary>Wendet die gemeinsamen Tags aus dem letzten Lookup auf alle Dateien an.</summary>
        public ICommand ApplyToAllCommand { get; }

        /// <summary>Entfernt das Cover-Bild aus der ausgewählten Datei.</summary>
        public ICommand RemoveCoverCommand { get; }

        /// <summary>Öffnet einen Datei-Picker zum Laden eines Cover-Bilds.</summary>
        public ICommand LoadCoverCommand { get; }

        /// <summary>Schreibt das aktuelle Cover-Bild auf alle Dateien im Ordner.</summary>
        public ICommand ApplyCoverToAllCommand { get; }

        /// <summary>Berechnet die Umbenennungs-Vorschau.</summary>
        public ICommand PreviewRenameCommand { get; }

        /// <summary>Führt die Umbenennung nach Bestätigung durch.</summary>
        public ICommand ExecuteRenameCommand { get; }

        // ── Öffentliche Methoden (Delegation an den Orchestrator) ──────────────

        /// <inheritdoc cref="TagManagerActions.LoadFolderAsync"/>
        public Task LoadFolderAsync(string folderPath) => _actions.LoadFolderAsync(folderPath);

        /// <summary>Wird vom Page-Code-Behind bei SelectionChanged aufgerufen.</summary>
        public void SetSelectedFiles(IReadOnlyList<TagFileItemViewModel> selectedItems)
            => FileListVM.SetSelectedFiles(selectedItems);

        /// <inheritdoc cref="TagManagerActions.ApplyLookupCandidate"/>
        public void ApplyLookupCandidate(int index) => _actions.ApplyLookupCandidate(index);

        /// <summary>
        /// Übernimmt ein Lookup-Ergebnis direkt in die Editor-Felder. <c>internal</c>, weil
        /// das Tests-Projekt diese Methode zur direkten Validierung der Formularbefüllung nutzt.
        /// </summary>
        internal void ApplyLookupResult(TagLookupResult result) => _actions.ApplyLookupResult(result);

        /// <summary>Wartet auf den Abschluss des laufenden Auto-Lookups (für deterministische Tests).</summary>
        internal Task WaitForAutoLookupCompleteAsync() => _actions.WaitForAutoLookupCompleteAsync();

        /// <summary>Wartet auf den Abschluss des laufenden Datei-Ladevorgangs (für deterministische Tests).</summary>
        internal Task WaitForFileLoadCompleteAsync() => _actions.WaitForFileLoadCompleteAsync();

        /// <summary>Setzt das Cover aus einem vom Nutzer geladenen Bild.</summary>
        public void SetCoverFromFile(byte[] imageData, string mimeType)
            => CoverVM.SetFromFile(imageData, mimeType);

        // ── Statische Helfer (für Tests beibehalten) ────────────────────────────

        /// <summary>
        /// Baut die Suchanfrage aus dem Ordnerkontext – delegiert an den statischen Core-Helper.
        /// Bleibt als Shim, damit bestehende Unit-Tests weiter kompilieren.
        /// </summary>
        internal static string BuildAutoLookupQuery(string? folderPath)
            => TagLookupCoordinator.BuildAutoLookupQueryCore(folderPath);

        /// <summary>
        /// Wählt den besten Treffer aus den Lookup-Ergebnissen – delegiert an den statischen Core-Helper.
        /// Bleibt als Shim, damit bestehende Unit-Tests weiter kompilieren.
        /// </summary>
        internal static TagLookupResult? SelectBestMatch(IReadOnlyList<TagLookupResult> results, int loadedTrackCount)
            => TagLookupCoordinator.SelectBestMatchCore(results, loadedTrackCount);

        // ── Sub-VM-Verdrahtung ─────────────────────────────────────────────────

        private void OnSubVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(e.PropertyName);
            RefreshCommandStates();
        }

        private void OnFileSelectionChanged(IReadOnlyList<TagFileItemViewModel> selectedItems)
        {
            if (selectedItems.Count == 1)
            {
                _ = _actions.LoadFileTagsAsync(selectedItems[0]);
            }
            else if (selectedItems.Count > 1)
            {
                _ = _actions.LoadMultipleFileTagsAsync(selectedItems);
            }
            else
            {
                EditorVM.Clear();
                CoverVM.Clear();
            }

            RefreshCommandStates();
        }

        /// <summary>
        /// Aktualisiert den aktivierten/deaktivierten Zustand aller Commands anhand des aktuellen Zustands.
        /// </summary>
        private void RefreshCommandStates()
        {
            bool hasSelection = FileListVM.HasSelectedFile;
            bool hasFiles = FileListVM.HasFiles;

            _saveCommand.SetEnabled(HasUnsavedChanges && hasSelection);
            _saveAllCommand.SetEnabled(hasFiles && HasAnyModifiedFile());
            _removeAllTagsCommand.SetEnabled(FileListVM.SelectedFile is not null);
            _lookupOnlineCommand.SetEnabled(hasSelection && !IsLookingUp);
            _autoLookupCommand.SetEnabled(hasFiles && !IsLookingUp && !string.IsNullOrEmpty(FileListVM.CurrentFolderPath));
            _applyToAllCommand.SetEnabled(hasFiles && EditorVM.HasPendingBatchTag);
            _removeCoverCommand.SetEnabled(FileListVM.SelectedFile is not null && CoverVM.CoverImage is not null);
            _loadCoverCommand.SetEnabled(hasSelection);
            _applyCoverToAllCommand.SetEnabled(hasFiles && CoverVM.HasCover);

            _previewRenameCommand.SetEnabled(hasFiles && !string.IsNullOrWhiteSpace(RenameVM.RenamePattern));
            _executeRenameCommand.SetEnabled(RenameVM.PreviewItems.Count > 0);
        }

        private bool HasAnyModifiedFile()
        {
            foreach (TagFileItemViewModel file in FileListVM.Files)
            {
                if (file.IsModified)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

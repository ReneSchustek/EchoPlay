using EchoPlay.App.Services;
using EchoPlay.LocalLibrary.Parsing;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.ApplicationModel.Resources;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für den Tag-Manager.
    /// Verwaltet eine Liste von Audiodateien und erlaubt das Bearbeiten, Online-Nachschlagen
    /// und Speichern ihrer Metadaten (Tags).
    /// Bietet zusätzlich die Möglichkeit, alle Dateien im geöffneten Ordner
    /// nach einem konfigurierbaren Muster umzubenennen.
    /// </summary>
    public sealed class TagManagerViewModel : ObservableObject
    {
        private readonly ITagService _tagService;
        private readonly ITagLookupService _lookupService;
        private readonly IFileRenameService _fileRenameService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly IOnlineAccessGuard _onlineAccessGuard;
        private static readonly ResourceLoader _resources = ResourceLoader.GetForViewIndependentUse();

        private ObservableCollection<TagFileItemViewModel> _files = [];
        private TagFileItemViewModel? _selectedFile;
        private IReadOnlyList<TagFileItemViewModel> _selectedFiles = [];
        private string? _title;
        private string? _album;
        private string? _artist;
        private string? _albumArtist;
        private string? _year;
        private string? _trackNumber;
        private string? _trackCount;
        private string? _genre;
        private BitmapImage? _coverImage;
        private byte[]? _coverImageData;
        private string? _coverMimeType;
        private bool _isLoading;
        private bool _isLookingUp;
        private bool _hasUnsavedChanges;
        private string _autoLookupStatusText = string.Empty;
        private string _renamePattern = "{track:00} - {title}";
        private IReadOnlyList<RenamePreviewItem> _renamePreview = [];
        private string _batchProgressText = string.Empty;

        // Zuletzt geöffneter Ordnerpfad – wird für Vorschau und Umbenennung benötigt
        private string? _currentFolderPath;

        // Zwischenspeicher: gemeinsame Tags aus dem letzten Lookup-Ergebnis für "Alle taggen"
        private AudioTag? _pendingBatchTag;

        // Platzhalter für Felder mit unterschiedlichen Werten bei Mehrfachauswahl
        private const string MixedValuePlaceholder = "(verschieden)";

        // Merkt sich, welche Felder der Nutzer bei Mehrfachauswahl geändert hat.
        // Nur geänderte Felder werden beim Speichern auf alle selektierten Dateien geschrieben.
        private readonly HashSet<string> _editedFields = [];

        /// <summary>
        /// Wird ausgelöst, wenn der Online-Lookup fertig ist.
        /// Die Page abonniert dieses Event und zeigt den Auswahl-Dialog an.
        /// </summary>
        public event EventHandler<IReadOnlyList<TagLookupResult>>? LookupResultsReady;

        /// <summary>
        /// Wird ausgelöst, wenn der Auto-Lookup einen eindeutigen Treffer gefunden
        /// und die Tags automatisch übernommen hat.
        /// Die Page zeigt daraufhin eine Bestätigungs-InfoBar an.
        /// </summary>
        public event EventHandler<TagLookupResult>? AutoLookupApplied;

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer ein Cover per Datei-Picker laden möchte.
        /// Die Page öffnet daraufhin einen FileOpenPicker (benötigt WinRT-Interop).
        /// </summary>
        public event EventHandler? LoadCoverRequested;

        /// <summary>
        /// Wird ausgelöst, wenn nach "Alle taggen" die Rename-Vorschau automatisch berechnet wurde.
        /// Die Page scrollt daraufhin zur Rename-Sektion.
        /// </summary>
        public event EventHandler? RenamePreviewReady;

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

        /// <summary>
        /// Initialisiert das ViewModel mit allen benötigten Diensten.
        /// </summary>
        /// <param name="tagService">Liest und schreibt Metadaten in Audiodateien.</param>
        /// <param name="lookupService">Sucht Metadaten über MusicBrainz.</param>
        /// <param name="fileRenameService">Erstellt Umbenennungs-Vorschau und führt Umbenennung durch.</param>
        /// <param name="errorDialogService">Zeigt Fehlermeldungen als Dialog an.</param>
        /// <param name="confirmationDialogService">Zeigt Bestätigungsdialoge an.</param>
        /// <param name="onlineAccessGuard">Prüft den Offline-Modus und zeigt bei Bedarf einen Bestätigungsdialog.</param>
        public TagManagerViewModel(
            ITagService tagService,
            ITagLookupService lookupService,
            IFileRenameService fileRenameService,
            IErrorDialogService errorDialogService,
            IConfirmationDialogService confirmationDialogService,
            IOnlineAccessGuard onlineAccessGuard)
        {
            _tagService                  = tagService;
            _lookupService               = lookupService;
            _fileRenameService           = fileRenameService;
            _errorDialogService          = errorDialogService;
            _confirmationDialogService   = confirmationDialogService;
            _onlineAccessGuard           = onlineAccessGuard;

            _saveCommand            = new RelayCommand(() => _ = SaveAsync());
            _saveAllCommand         = new RelayCommand(() => _ = SaveAllAsync());
            _removeAllTagsCommand   = new RelayCommand(() => _ = RemoveAllTagsAsync());
            _lookupOnlineCommand    = new RelayCommand(() => _ = LookupOnlineAsync());
            _autoLookupCommand      = new RelayCommand(() => _ = AutoLookupAsync());
            _applyToAllCommand      = new RelayCommand(() => _ = ApplyToAllAsync());
            _removeCoverCommand     = new RelayCommand(() => _ = RemoveCoverAsync());
            _loadCoverCommand       = new RelayCommand(() => LoadCoverRequested?.Invoke(this, EventArgs.Empty));
            _applyCoverToAllCommand = new RelayCommand(() => _ = ApplyCoverToAllAsync());
            _previewRenameCommand   = new RelayCommand(() => _ = PreviewRenameAsync());
            _executeRenameCommand   = new RelayCommand(() => _ = ExecuteRenameAsync());

            SaveCommand            = _saveCommand;
            SaveAllCommand         = _saveAllCommand;
            RemoveAllTagsCommand   = _removeAllTagsCommand;
            LookupOnlineCommand    = _lookupOnlineCommand;
            AutoLookupCommand      = _autoLookupCommand;
            ApplyToAllCommand      = _applyToAllCommand;
            RemoveCoverCommand     = _removeCoverCommand;
            LoadCoverCommand       = _loadCoverCommand;
            ApplyCoverToAllCommand = _applyCoverToAllCommand;
            PreviewRenameCommand   = _previewRenameCommand;
            ExecuteRenameCommand   = _executeRenameCommand;

            RefreshCommandStates();
        }

        // --- Dateiliste ---

        /// <summary>Liste aller Audiodateien im geöffneten Ordner.</summary>
        public ObservableCollection<TagFileItemViewModel> Files
        {
            get => _files;
            private set
            {
                if (SetProperty(ref _files, value))
                {
                    OnPropertyChanged(nameof(HasFiles));
                    RefreshCommandStates();
                }
            }
        }

        /// <summary>
        /// Aktuell ausgewählte Datei (bei Einzelauswahl).
        /// Wird intern über <see cref="SetSelectedFiles"/> gesetzt.
        /// </summary>
        public TagFileItemViewModel? SelectedFile
        {
            get => _selectedFile;
            private set
            {
                SetProperty(ref _selectedFile, value);
                OnPropertyChanged(nameof(HasSelectedFile));
            }
        }

        /// <summary>
        /// Alle aktuell ausgewählten Dateien (bei Mehrfachauswahl).
        /// </summary>
        public IReadOnlyList<TagFileItemViewModel> SelectedFiles
        {
            get => _selectedFiles;
            private set => SetProperty(ref _selectedFiles, value);
        }

        /// <summary>
        /// Wird vom Code-Behind bei SelectionChanged aufgerufen.
        /// Entscheidet ob Einzel- oder Mehrfachauswahl vorliegt und lädt die Tags.
        /// </summary>
        /// <param name="selectedItems">Die aktuell selektierten Dateien.</param>
        public void SetSelectedFiles(IReadOnlyList<TagFileItemViewModel> selectedItems)
        {
            SelectedFiles = selectedItems;
            _editedFields.Clear();

            if (selectedItems.Count == 1)
            {
                SelectedFile = selectedItems[0];
                _ = LoadFileTagsAsync(selectedItems[0]);
            }
            else if (selectedItems.Count > 1)
            {
                SelectedFile = null;
                _ = LoadMultipleFileTagsAsync(selectedItems);
            }
            else
            {
                SelectedFile = null;
                ClearTagFields();
            }

            RefreshCommandStates();
        }

        // --- Bearbeitbare Tag-Felder (als string, damit die TextBox-Bindung reibungslos funktioniert) ---

        /// <summary>Titel des Tracks.</summary>
        public string? Title
        {
            get => _title;
            set { SetProperty(ref _title, value); _editedFields.Add(nameof(Title)); HasUnsavedChanges = true; }
        }

        /// <summary>Album des Tracks.</summary>
        public string? Album
        {
            get => _album;
            set { SetProperty(ref _album, value); _editedFields.Add(nameof(Album)); HasUnsavedChanges = true; }
        }

        /// <summary>Haupt-Interpret des Tracks.</summary>
        public string? Artist
        {
            get => _artist;
            set { SetProperty(ref _artist, value); _editedFields.Add(nameof(Artist)); HasUnsavedChanges = true; }
        }

        /// <summary>Album-Künstler (kann von Artist abweichen, z.B. bei Samplern).</summary>
        public string? AlbumArtist
        {
            get => _albumArtist;
            set { SetProperty(ref _albumArtist, value); _editedFields.Add(nameof(AlbumArtist)); HasUnsavedChanges = true; }
        }

        /// <summary>Erscheinungsjahr als Text – muss beim Speichern in uint geparst werden.</summary>
        public string? Year
        {
            get => _year;
            set { SetProperty(ref _year, value); _editedFields.Add(nameof(Year)); HasUnsavedChanges = true; }
        }

        /// <summary>Tracknummer als Text.</summary>
        public string? TrackNumber
        {
            get => _trackNumber;
            set { SetProperty(ref _trackNumber, value); _editedFields.Add(nameof(TrackNumber)); HasUnsavedChanges = true; }
        }

        /// <summary>Gesamtanzahl Tracks als Text.</summary>
        public string? TrackCount
        {
            get => _trackCount;
            set { SetProperty(ref _trackCount, value); _editedFields.Add(nameof(TrackCount)); HasUnsavedChanges = true; }
        }

        /// <summary>Genre des Tracks.</summary>
        public string? Genre
        {
            get => _genre;
            set { SetProperty(ref _genre, value); _editedFields.Add(nameof(Genre)); HasUnsavedChanges = true; }
        }

        // --- Cover-Anzeige (nur lesend – keine Rückbindung) ---

        /// <summary>
        /// Cover-Bild zur Anzeige als <see cref="BitmapImage"/>.
        /// Wird nur über <see cref="LoadFileTagsAsync"/> oder <see cref="RemoveCoverAsync"/> gesetzt.
        /// </summary>
        public BitmapImage? CoverImage
        {
            get => _coverImage;
            private set
            {
                SetProperty(ref _coverImage, value);
                OnPropertyChanged(nameof(CoverVisibility));
                RefreshCommandStates();
            }
        }

        // --- Lade- und Zustandsanzeige ---

        /// <summary>Gibt an, ob gerade Tags aus einer Datei geladen werden.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                SetProperty(ref _isLoading, value);
                OnPropertyChanged(nameof(IsLoadingVisibility));
            }
        }

        /// <summary>Gibt an, ob gerade eine MusicBrainz-Suche läuft.</summary>
        public bool IsLookingUp
        {
            get => _isLookingUp;
            private set
            {
                SetProperty(ref _isLookingUp, value);
                OnPropertyChanged(nameof(IsLookingUpVisibility));
                RefreshCommandStates();
            }
        }

        /// <summary>
        /// Statustext des Auto-Lookups – zeigt die verwendete Suchanfrage während der Suche,
        /// danach den übernommenen Ergebnisnamen oder eine leere Zeichenkette im Ruhezustand.
        /// </summary>
        public string AutoLookupStatusText
        {
            get => _autoLookupStatusText;
            private set
            {
                SetProperty(ref _autoLookupStatusText, value);
                OnPropertyChanged(nameof(AutoLookupStatusVisibility));
            }
        }

        /// <summary>
        /// Fortschrittstext für Batch-Operationen (z.B. "Datei 5 von 20…").
        /// Leer wenn keine Batch-Operation läuft.
        /// </summary>
        public string BatchProgressText
        {
            get => _batchProgressText;
            private set
            {
                SetProperty(ref _batchProgressText, value);
                OnPropertyChanged(nameof(BatchProgressVisibility));
            }
        }

        /// <summary>
        /// Gibt an, ob ungespeicherte Änderungen vorhanden sind.
        /// Wird bei jeder Feldänderung auf <c>true</c> gesetzt und nach dem Speichern zurückgesetzt.
        /// </summary>
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                SetProperty(ref _hasUnsavedChanges, value);
                RefreshCommandStates();
            }
        }

        // --- Datei-Umbenennung nach Muster ---

        /// <summary>
        /// Muster für die Umbenennung, z.B. <c>"{track:00} - {title}"</c>.
        /// Unterstützte Platzhalter: {title}, {album}, {artist}, {year},
        /// {track}, {track:00}, {track:000}, {filename}.
        /// </summary>
        public string RenamePattern
        {
            get => _renamePattern;
            set
            {
                if (SetProperty(ref _renamePattern, value))
                {
                    // Nach Musteränderung alte Vorschau zurücksetzen – sie wäre veraltet
                    _renamePreview = [];
                    OnPropertyChanged(nameof(RenamePreview));
                    OnPropertyChanged(nameof(RenamePreviewVisibility));
                    RefreshCommandStates();
                }
            }
        }

        /// <summary>
        /// Vorschau der Umbenennung: zeigt für jede Datei den alten und neuen Namen.
        /// Leer, solange keine Vorschau angefordert wurde oder das Muster geändert wurde.
        /// </summary>
        public IReadOnlyList<RenamePreviewItem> RenamePreview
        {
            get => _renamePreview;
            private set
            {
                if (SetProperty(ref _renamePreview, value))
                {
                    OnPropertyChanged(nameof(RenamePreviewVisibility));
                    RefreshCommandStates();
                }
            }
        }

        // --- Berechnete Visibility-Properties (kein Converter nötig) ---

        /// <summary>Ob mindestens eine Datei in der Liste vorhanden ist.</summary>
        public bool HasFiles => _files.Count > 0;

        /// <summary>
        /// Ob die Formularfelder aktiv sind – bei Einzel- oder Mehrfachauswahl.
        /// </summary>
        public bool HasSelectedFile => SelectedFile is not null || _selectedFiles.Count > 1;

        /// <summary>
        /// Sichtbarkeit des Lade-Indikators – sichtbar während Tags oder Ordner geladen werden.
        /// </summary>
        public Visibility IsLoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des Lookup-Indikators – sichtbar während MusicBrainz-Suche läuft.
        /// </summary>
        public Visibility IsLookingUpVisibility => IsLookingUp ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des Auto-Lookup-Statustexts – sichtbar sobald ein Text gesetzt ist.
        /// </summary>
        public Visibility AutoLookupStatusVisibility =>
            string.IsNullOrEmpty(_autoLookupStatusText) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>
        /// Sichtbarkeit des Cover-Bilds – sichtbar wenn ein Cover vorhanden ist.
        /// </summary>
        public Visibility CoverVisibility => CoverImage is not null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des Batch-Fortschrittstexts – sichtbar während einer Batch-Operation.
        /// </summary>
        public Visibility BatchProgressVisibility =>
            string.IsNullOrEmpty(_batchProgressText) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>
        /// Ob ein Batch-Tag aus einem Lookup bereitsteht und auf alle Dateien angewendet werden kann.
        /// </summary>
        public bool HasPendingBatchTag => _pendingBatchTag is not null;

        /// <summary>
        /// Sichtbarkeit der Umbenennungs-Vorschauliste – sichtbar wenn eine Vorschau berechnet wurde.
        /// </summary>
        public Visibility RenamePreviewVisibility =>
            _renamePreview.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // --- Commands ---

        /// <summary>Speichert die aktuell angezeigten Tags in die ausgewählte Datei.</summary>
        public ICommand SaveCommand { get; }

        /// <summary>Speichert alle modifizierten Dateien auf einmal.</summary>
        public ICommand SaveAllCommand { get; }

        /// <summary>Entfernt alle Tags der ausgewählten Datei nach Bestätigung.</summary>
        public ICommand RemoveAllTagsCommand { get; }

        /// <summary>
        /// Sucht online nach Tags bei MusicBrainz.
        /// Feuert nach Abschluss <see cref="LookupResultsReady"/>, damit die Page den Dialog zeigen kann.
        /// </summary>
        public ICommand LookupOnlineCommand { get; }

        /// <summary>
        /// Baut die Suchanfrage automatisch aus dem Ordnerkontext (Serienname + Folgenname)
        /// und übernimmt Tags ohne Dialog, wenn ein eindeutiger Treffer mit passender Track-Anzahl
        /// gefunden wird. Sonst wird <see cref="LookupResultsReady"/> gefeuert.
        /// </summary>
        public ICommand AutoLookupCommand { get; }

        /// <summary>
        /// Wendet die gemeinsamen Tags aus dem letzten Lookup auf alle Dateien im Ordner an.
        /// Album, Artist, AlbumArtist, Year, Genre und TrackCount werden überschrieben,
        /// Title und TrackNumber bleiben datei-individuell erhalten.
        /// </summary>
        public ICommand ApplyToAllCommand { get; }

        /// <summary>Entfernt das Cover-Bild aus der ausgewählten Datei.</summary>
        public ICommand RemoveCoverCommand { get; }

        /// <summary>Öffnet einen Datei-Picker zum Laden eines Cover-Bilds.</summary>
        public ICommand LoadCoverCommand { get; }

        /// <summary>Schreibt das aktuelle Cover-Bild auf alle Dateien im Ordner.</summary>
        public ICommand ApplyCoverToAllCommand { get; }

        /// <summary>
        /// Berechnet die Umbennungs-Vorschau ohne Dateien zu verändern.
        /// Nur aktiv wenn Dateien geladen sind und ein Muster eingegeben wurde.
        /// </summary>
        public ICommand PreviewRenameCommand { get; }

        /// <summary>
        /// Führt die Umbenennung nach Bestätigung durch.
        /// Nur aktiv wenn eine Vorschau berechnet wurde.
        /// </summary>
        public ICommand ExecuteRenameCommand { get; }

        // --- Öffentliche Methoden ---

        /// <summary>
        /// Lädt alle unterstützten Audiodateien aus dem angegebenen Ordner in die Dateiliste.
        /// </summary>
        /// <param name="folderPath">Absoluter Pfad zum Ordner.</param>
        public async Task LoadFolderAsync(string folderPath)
        {
            IsLoading          = true;
            _currentFolderPath = folderPath;
            _pendingBatchTag   = null;
            OnPropertyChanged(nameof(HasPendingBatchTag));
            Files              = [];
            RenamePreview      = [];
            ClearTagFields();

            try
            {
                IReadOnlyList<(string FilePath, AudioTag Tag)> results = await _tagService.ReadFolderAsync(folderPath);

                ObservableCollection<TagFileItemViewModel> items = new();

                foreach ((string path, AudioTag _) in results)
                {
                    items.Add(new TagFileItemViewModel(path, folderPath));
                }

                Files = items;
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(_resources.GetString("TagManagerFolderLoadErrorTitle"), ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Lädt die Tags der angegebenen Datei in die Formularfelder.
        /// </summary>
        /// <param name="file">Das ViewModel der zu ladenden Datei.</param>
        public async Task LoadFileTagsAsync(TagFileItemViewModel file)
        {
            IsLoading = true;
            ClearTagFields();

            try
            {
                AudioTag tag = await _tagService.ReadAsync(file.FilePath);
                PopulateTagFields(tag);
                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(_resources.GetString("TagManagerTagLoadErrorTitle"), ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Lädt die Tags mehrerer Dateien und zeigt gemeinsame Werte an.
        /// Felder mit unterschiedlichen Werten zeigen den Platzhalter "(verschieden)".
        /// </summary>
        /// <param name="files">Die selektierten Dateien.</param>
        public async Task LoadMultipleFileTagsAsync(IReadOnlyList<TagFileItemViewModel> files)
        {
            IsLoading = true;
            ClearTagFields();

            try
            {
                List<AudioTag> tags = new(files.Count);

                foreach (TagFileItemViewModel file in files)
                {
                    AudioTag tag = await _tagService.ReadAsync(file.FilePath);
                    tags.Add(tag);
                }

                // Gemeinsame Werte berechnen: wenn alle gleich → Wert anzeigen, sonst Platzhalter
                AudioTag first = tags[0];

                _title       = tags.All(t => t.Title == first.Title)             ? first.Title       : MixedValuePlaceholder;
                _album       = tags.All(t => t.Album == first.Album)             ? first.Album       : MixedValuePlaceholder;
                _artist      = tags.All(t => t.Artist == first.Artist)           ? first.Artist      : MixedValuePlaceholder;
                _albumArtist = tags.All(t => t.AlbumArtist == first.AlbumArtist) ? first.AlbumArtist : MixedValuePlaceholder;
                _genre       = tags.All(t => t.Genre == first.Genre)             ? first.Genre       : MixedValuePlaceholder;

                _year = tags.All(t => t.Year == first.Year)
                    ? first.Year?.ToString(CultureInfo.InvariantCulture)
                    : MixedValuePlaceholder;

                _trackNumber = tags.All(t => t.TrackNumber == first.TrackNumber)
                    ? first.TrackNumber?.ToString(CultureInfo.InvariantCulture)
                    : MixedValuePlaceholder;

                _trackCount = tags.All(t => t.TrackCount == first.TrackCount)
                    ? first.TrackCount?.ToString(CultureInfo.InvariantCulture)
                    : MixedValuePlaceholder;

                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Album));
                OnPropertyChanged(nameof(Artist));
                OnPropertyChanged(nameof(AlbumArtist));
                OnPropertyChanged(nameof(Year));
                OnPropertyChanged(nameof(TrackNumber));
                OnPropertyChanged(nameof(TrackCount));
                OnPropertyChanged(nameof(Genre));

                // Cover: nur anzeigen wenn alle Dateien dasselbe Cover haben
                _coverImageData = null;
                _coverMimeType  = null;
                CoverImage      = null;

                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(_resources.GetString("TagManagerTagLoadErrorTitle"), ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Übernimmt Felder aus einem Lookup-Ergebnis in die Formularfelder.
        /// Nur nicht-null-Felder werden übernommen – bestehende Werte bleiben erhalten.
        /// </summary>
        /// <param name="result">Das vom Nutzer im Dialog gewählte Suchergebnis.</param>
        public void ApplyLookupResult(TagLookupResult result)
        {
            if (result.Title is not null)    Title      = result.Title;
            if (result.Artist is not null)   Artist     = result.Artist;
            if (result.Album is not null)    Album      = result.Album;
            if (result.Genre is not null)    Genre      = result.Genre;
            if (result.Year.HasValue)        Year       = result.Year.Value.ToString(CultureInfo.InvariantCulture);
            if (result.TrackCount.HasValue)  TrackCount = result.TrackCount.Value.ToString(CultureInfo.InvariantCulture);
            HasUnsavedChanges = true;

            // Gemeinsame Tags für "Alle taggen" zwischenspeichern
            _pendingBatchTag = new AudioTag
            {
                Album       = result.Album,
                Artist      = result.Artist,
                AlbumArtist = result.Artist,
                Genre       = result.Genre,
                Year        = result.Year,
                TrackCount  = result.TrackCount
            };
            OnPropertyChanged(nameof(HasPendingBatchTag));
            RefreshCommandStates();
        }

        // --- Private Methoden ---

        private async Task SaveAsync()
        {
            // Einzelauswahl: alle Felder schreiben
            if (SelectedFile is not null)
            {
                try
                {
                    AudioTag tag = BuildAudioTagFromFields();
                    await _tagService.WriteAsync(SelectedFile.FilePath, tag);

                    SelectedFile.IsModified = false;
                    HasUnsavedChanges = false;
                }
                catch (Exception ex)
                {
                    await _errorDialogService.ShowAsync(_resources.GetString("TagManagerSaveErrorTitle"), ex.Message);
                }

                return;
            }

            // Mehrfachauswahl: nur geänderte Felder auf alle selektierten Dateien schreiben
            if (_selectedFiles.Count <= 1 || _editedFields.Count == 0) return;

            IsLoading = true;
            int processedCount = 0;

            try
            {
                AudioTag editedTag = BuildEditedFieldsTag();

                foreach (TagFileItemViewModel file in _selectedFiles)
                {
                    processedCount++;
                    BatchProgressText = string.Format(
                        CultureInfo.CurrentCulture,
                        _resources.GetString("TagManagerBatchProgressText"),
                        processedCount, _selectedFiles.Count);

                    // Bestehende Tags lesen und nur die geänderten Felder überschreiben
                    AudioTag existingTag = await _tagService.ReadAsync(file.FilePath);
                    AudioTag mergedTag = MergeEditedIntoExisting(editedTag, existingTag);
                    await _tagService.WriteAsync(file.FilePath, mergedTag);
                    file.IsModified = false;
                }

                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(_resources.GetString("TagManagerSaveErrorTitle"), ex.Message);
            }
            finally
            {
                BatchProgressText = string.Empty;
                IsLoading = false;
            }
        }

        /// <summary>
        /// Baut ein AudioTag nur aus den Feldern, die der Nutzer bei Mehrfachauswahl geändert hat.
        /// Nicht-geänderte Felder sind null und werden beim Merge ignoriert.
        /// </summary>
        private AudioTag BuildEditedFieldsTag()
        {
            return new AudioTag
            {
                Title       = _editedFields.Contains(nameof(Title))       ? NullIfEmpty(Title)       : null,
                Album       = _editedFields.Contains(nameof(Album))       ? NullIfEmpty(Album)       : null,
                Artist      = _editedFields.Contains(nameof(Artist))      ? NullIfEmpty(Artist)      : null,
                AlbumArtist = _editedFields.Contains(nameof(AlbumArtist)) ? NullIfEmpty(AlbumArtist) : null,
                Genre       = _editedFields.Contains(nameof(Genre))       ? NullIfEmpty(Genre)       : null,
                Year        = _editedFields.Contains(nameof(Year)) && uint.TryParse(Year, out uint y)           ? y    : null,
                TrackNumber = _editedFields.Contains(nameof(TrackNumber)) && uint.TryParse(TrackNumber, out uint tn) ? tn : null,
                TrackCount  = _editedFields.Contains(nameof(TrackCount)) && uint.TryParse(TrackCount, out uint tc)   ? tc : null
            };
        }

        /// <summary>
        /// Verschmilzt nur die vom Nutzer geänderten Felder in die bestehenden Tags einer Datei.
        /// Null-Felder im editedTag bedeuten "nicht geändert" → bestehender Wert bleibt erhalten.
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
                TrackNumber = edited.TrackNumber  ?? existing.TrackNumber,
                TrackCount  = edited.TrackCount   ?? existing.TrackCount
            };
        }

        private async Task RemoveAllTagsAsync()
        {
            if (SelectedFile is null) return;

            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                _resources.GetString("TagManagerRemoveAllTitle"),
                string.Format(CultureInfo.CurrentCulture, _resources.GetString("TagManagerRemoveAllMessage"), SelectedFile.FileName));

            if (!confirmed) return;

            try
            {
                await _tagService.RemoveAllTagsAsync(SelectedFile.FilePath);
                ClearTagFields();
                HasUnsavedChanges = false;
                SelectedFile.IsModified = false;
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(_resources.GetString("TagManagerRemoveTagsErrorTitle"), ex.Message);
            }
        }

        private async Task LookupOnlineAsync()
        {
            if (!HasSelectedFile) return;

            // Offline-Modus: Nutzer fragen, ob trotzdem ins Internet gegangen werden soll
            using IDisposable? onlineScope = await _onlineAccessGuard.RequestOnlineAccessAsync();
            if (onlineScope is null) return;

            // Suchbegriff aus Titel oder Dateiname zusammenbauen
            TagFileItemViewModel firstSelected = SelectedFile ?? _selectedFiles[0];
            string query = !string.IsNullOrWhiteSpace(Title) && Title != MixedValuePlaceholder
                ? Title
                : Path.GetFileNameWithoutExtension(firstSelected.FilePath);

            IsLookingUp = true;
            try
            {
                IReadOnlyList<TagLookupResult> results = await _lookupService.SearchAsync(query);
                LookupResultsReady?.Invoke(this, results);
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(_resources.GetString("TagManagerLookupErrorTitle"), ex.Message);
            }
            finally
            {
                IsLookingUp = false;
            }
        }

        private async Task AutoLookupAsync()
        {
            string query = BuildAutoLookupQuery(_currentFolderPath);

            if (string.IsNullOrWhiteSpace(query)) return;

            // Offline-Modus: Nutzer fragen, ob trotzdem ins Internet gegangen werden soll
            using IDisposable? onlineScope = await _onlineAccessGuard.RequestOnlineAccessAsync();
            if (onlineScope is null) return;

            // Suchanfrage im UI anzeigen, damit der Nutzer nachvollziehen kann, was gesucht wird
            AutoLookupStatusText = string.Format(
                CultureInfo.CurrentCulture,
                _resources.GetString("TagManagerAutoLookupSearchingText"),
                query);
            IsLookingUp          = true;

            try
            {
                IReadOnlyList<TagLookupResult> results = await _lookupService.SearchAsync(query);

                // Besten Treffer anhand der Track-Anzahl ermitteln
                int loadedTrackCount   = _files.Count;
                TagLookupResult? match = SelectBestMatch(results, loadedTrackCount);

                if (match is not null && match.TrackCount.HasValue
                    && match.TrackCount.Value == (uint)loadedTrackCount)
                {
                    // Eindeutiger Treffer mit exakter Track-Anzahl → direkt übernehmen
                    ApplyLookupResult(match);
                    AutoLookupStatusText = string.Empty;
                    AutoLookupApplied?.Invoke(this, match);
                }
                else
                {
                    // Kein eindeutiger Treffer → Auswahl-Dialog öffnen (sortiert nach Qualität)
                    AutoLookupStatusText = string.Empty;

                    List<TagLookupResult> sorted = results
                        .OrderByDescending(r => r.TrackCount.HasValue && r.TrackCount.Value == (uint)loadedTrackCount)
                        .ThenByDescending(r => r.TrackCount.HasValue)
                        .ToList();

                    LookupResultsReady?.Invoke(this, sorted);
                }
            }
            catch (Exception ex)
            {
                AutoLookupStatusText = string.Empty;
                await _errorDialogService.ShowAsync(_resources.GetString("TagManagerAutoLookupErrorTitle"), ex.Message);
            }
            finally
            {
                IsLookingUp = false;
            }
        }

        /// <summary>
        /// Baut die MusicBrainz-Suchanfrage aus dem Ordnerkontext zusammen.
        /// Serienname = übergeordneter Ordner, Folgenname = aktueller Ordner ohne führende Laufnummer.
        /// </summary>
        /// <param name="folderPath">Absoluter Pfad zum aktuell geöffneten Episodenordner.</param>
        /// <returns>
        /// Kombinierter Suchbegriff (z.B. „Die drei Fragezeichen Der Super-Papagei")
        /// oder ein leerer String wenn kein Ordner geöffnet ist.
        /// </returns>
        internal static string BuildAutoLookupQuery(string? folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return string.Empty;

            // Serienname aus dem übergeordneten Ordner
            string seriesName = Path.GetFileName(Path.GetDirectoryName(folderPath)) ?? string.Empty;

            // Folgentitel aus dem Ordnernamen – führende Laufnummer entfernen
            // (z.B. "001 - Der Super-Papagei" → "Der Super-Papagei")
            string episodeFolderName = Path.GetFileName(folderPath) ?? string.Empty;
            string episodeTitle      = EpisodeFolderParser.StripLeadingSequenceNumber(episodeFolderName);

            if (string.IsNullOrWhiteSpace(seriesName) || string.IsNullOrWhiteSpace(episodeTitle))
            {
                return string.Empty;
            }

            return $"{seriesName} {episodeTitle}";
        }

        /// <summary>
        /// Wählt den besten Treffer aus einer Liste von Suchergebnissen anhand der Track-Anzahl.
        /// Ein exakter Treffer (TrackCount == geladene Tracks) hat höchste Priorität.
        /// </summary>
        /// <param name="results">Suchergebnisse, sortiert nach MusicBrainz-Relevanz.</param>
        /// <param name="loadedTrackCount">Anzahl der aktuell im TagManager geladenen Tracks.</param>
        /// <returns>
        /// Ergebnis mit exakter Track-Anzahl, erstes Ergebnis wenn keines passt, oder
        /// <see langword="null"/> wenn die Liste leer ist.
        /// </returns>
        internal static TagLookupResult? SelectBestMatch(IReadOnlyList<TagLookupResult> results, int loadedTrackCount)
        {
            if (results.Count == 0) return null;

            // Exakter Track-Count-Treffer bevorzugen
            TagLookupResult? exactMatch = results.FirstOrDefault(
                r => r.TrackCount.HasValue && r.TrackCount.Value == (uint)loadedTrackCount);

            return exactMatch ?? results[0];
        }

        private async Task RemoveCoverAsync()
        {
            if (SelectedFile is null) return;

            try
            {
                await _tagService.WriteCoverAsync(SelectedFile.FilePath, null);
                _coverImageData = null;
                _coverMimeType  = null;
                CoverImage      = null;
                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(_resources.GetString("TagManagerCoverRemoveErrorTitle"), ex.Message);
            }
        }

        /// <summary>
        /// Speichert alle modifizierten Dateien auf einmal.
        /// Liest die bestehenden Tags jeder Datei und überschreibt die gemeinsamen Felder
        /// (Album, Artist, AlbumArtist, Year, Genre, TrackCount) aus dem Formular.
        /// Title und TrackNumber bleiben individuell erhalten.
        /// </summary>
        private async Task SaveAllAsync()
        {
            List<TagFileItemViewModel> modifiedFiles = _files.Where(f => f.IsModified).ToList();

            if (modifiedFiles.Count == 0) return;

            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                _resources.GetString("TagManagerSaveAllConfirmTitle"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    _resources.GetString("TagManagerSaveAllConfirmMessage"),
                    modifiedFiles.Count));

            if (!confirmed) return;

            IsLoading = true;
            int savedCount = 0;

            try
            {
                // Gemeinsame Felder aus dem Formular
                AudioTag sharedTag = BuildSharedTagFromFields();

                foreach (TagFileItemViewModel file in modifiedFiles)
                {
                    savedCount++;
                    BatchProgressText = string.Format(
                        CultureInfo.CurrentCulture,
                        _resources.GetString("TagManagerBatchProgressText"),
                        savedCount, modifiedFiles.Count);

                    AudioTag existingTag = await _tagService.ReadAsync(file.FilePath);

                    // Gemeinsame Felder überschreiben, individuelle behalten
                    AudioTag mergedTag = MergeSharedIntoExisting(sharedTag, existingTag);
                    await _tagService.WriteAsync(file.FilePath, mergedTag);
                    file.IsModified = false;
                }

                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(
                    _resources.GetString("TagManagerSaveErrorTitle"), ex.Message);
            }
            finally
            {
                BatchProgressText = string.Empty;
                IsLoading = false;
            }
        }

        /// <summary>
        /// Wendet die gemeinsamen Tags aus dem letzten Lookup auf alle Dateien im Ordner an.
        /// Für jede Datei werden die bestehenden Tags gelesen und die gemeinsamen Felder
        /// (Album, Artist, AlbumArtist, Year, Genre, TrackCount) überschrieben.
        /// Title und TrackNumber bleiben datei-individuell erhalten.
        /// </summary>
        private async Task ApplyToAllAsync()
        {
            if (_pendingBatchTag is null || _files.Count == 0) return;

            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                _resources.GetString("TagManagerApplyToAllConfirmTitle"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    _resources.GetString("TagManagerApplyToAllConfirmMessage"),
                    _files.Count));

            if (!confirmed) return;

            IsLoading = true;
            int processedCount = 0;

            try
            {
                foreach (TagFileItemViewModel file in _files)
                {
                    processedCount++;
                    BatchProgressText = string.Format(
                        CultureInfo.CurrentCulture,
                        _resources.GetString("TagManagerBatchProgressText"),
                        processedCount, _files.Count);

                    AudioTag existingTag = await _tagService.ReadAsync(file.FilePath);
                    AudioTag mergedTag = MergeSharedIntoExisting(_pendingBatchTag, existingTag);
                    await _tagService.WriteAsync(file.FilePath, mergedTag);
                    file.IsModified = false;
                }

                _pendingBatchTag = null;
                OnPropertyChanged(nameof(HasPendingBatchTag));
                HasUnsavedChanges = false;
                RefreshCommandStates();

                // Workflow-Verkettung: nach "Alle taggen" automatisch Rename-Vorschau aktualisieren
                if (!string.IsNullOrWhiteSpace(_renamePattern) && !string.IsNullOrEmpty(_currentFolderPath))
                {
                    await PreviewRenameAsync();
                    RenamePreviewReady?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(
                    _resources.GetString("TagManagerApplyToAllErrorTitle"), ex.Message);
            }
            finally
            {
                BatchProgressText = string.Empty;
                IsLoading = false;
            }
        }

        /// <summary>
        /// Schreibt das aktuelle Cover-Bild auf alle Dateien im Ordner.
        /// </summary>
        private async Task ApplyCoverToAllAsync()
        {
            if (_coverImageData is null || _files.Count == 0) return;

            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                _resources.GetString("TagManagerCoverApplyAllConfirmTitle"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    _resources.GetString("TagManagerCoverApplyAllConfirmMessage"),
                    _files.Count));

            if (!confirmed) return;

            IsLoading = true;
            int processedCount = 0;
            string mimeType = _coverMimeType ?? "image/jpeg";

            try
            {
                foreach (TagFileItemViewModel file in _files)
                {
                    processedCount++;
                    BatchProgressText = string.Format(
                        CultureInfo.CurrentCulture,
                        _resources.GetString("TagManagerBatchProgressText"),
                        processedCount, _files.Count);

                    await _tagService.WriteCoverAsync(file.FilePath, _coverImageData, mimeType);
                }
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(
                    _resources.GetString("TagManagerCoverApplyAllErrorTitle"), ex.Message);
            }
            finally
            {
                BatchProgressText = string.Empty;
                IsLoading = false;
            }
        }

        /// <summary>
        /// Setzt das Cover-Bild aus den übergebenen Bilddaten.
        /// Wird von der Page nach dem FileOpenPicker aufgerufen.
        /// </summary>
        /// <param name="imageData">Rohdaten des Bilds.</param>
        /// <param name="mimeType">MIME-Typ des Bilds (z.B. "image/jpeg").</param>
        public void SetCoverFromFile(byte[] imageData, string mimeType)
        {
            _coverImageData = imageData;
            _coverMimeType  = mimeType;

            BitmapImage bitmap = new();
            using MemoryStream stream = new(imageData);
            bitmap.SetSource(stream.AsRandomAccessStream());
            CoverImage = bitmap;

            HasUnsavedChanges = true;
        }

        /// <summary>
        /// Baut ein <see cref="AudioTag"/> nur mit den gemeinsamen Feldern aus dem Formular.
        /// Title und TrackNumber werden bewusst ausgelassen.
        /// </summary>
        private AudioTag BuildSharedTagFromFields()
        {
            return new AudioTag
            {
                Album       = NullIfEmpty(Album),
                Artist      = NullIfEmpty(Artist),
                AlbumArtist = NullIfEmpty(AlbumArtist),
                Genre       = NullIfEmpty(Genre),
                Year        = uint.TryParse(Year, out uint year) ? year : null,
                TrackCount  = uint.TryParse(TrackCount, out uint trackCnt) ? trackCnt : null
            };
        }

        /// <summary>
        /// Verschmilzt gemeinsame Tags in die bestehenden Tags einer Datei.
        /// Die individuellen Felder (Title, TrackNumber) stammen aus der bestehenden Datei,
        /// die gemeinsamen Felder (Album, Artist, etc.) aus dem Batch-Tag.
        /// </summary>
        private static AudioTag MergeSharedIntoExisting(AudioTag shared, AudioTag existing)
        {
            return new AudioTag
            {
                // Individuelle Felder: aus der bestehenden Datei übernehmen
                Title       = existing.Title,
                TrackNumber = existing.TrackNumber,

                // Gemeinsame Felder: aus dem Batch-Tag überschreiben
                Album       = shared.Album ?? existing.Album,
                Artist      = shared.Artist ?? existing.Artist,
                AlbumArtist = shared.AlbumArtist ?? existing.AlbumArtist,
                Genre       = shared.Genre ?? existing.Genre,
                Year        = shared.Year ?? existing.Year,
                TrackCount  = shared.TrackCount ?? existing.TrackCount
            };
        }

        /// <summary>
        /// Liest alle Tags aus dem aktuell geöffneten Ordner frisch und berechnet die Vorschau.
        /// Die Tags werden nicht gecacht – so ist die Vorschau auch nach externen Tag-Änderungen korrekt.
        /// </summary>
        private async Task PreviewRenameAsync()
        {
            if (string.IsNullOrEmpty(_currentFolderPath)) return;

            IsLoading = true;
            try
            {
                IReadOnlyList<(string FilePath, AudioTag Tag)> filesWithTags =
                    await _tagService.ReadFolderAsync(_currentFolderPath);

                RenamePreview = _fileRenameService.BuildPreview(filesWithTags, _renamePattern);
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(_resources.GetString("TagManagerPreviewErrorTitle"), ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Benennt alle Dateien im Ordner nach Nutzerbestätigung um
        /// und lädt danach den Ordner neu, damit die Dateiliste aktuell ist.
        /// </summary>
        private async Task ExecuteRenameAsync()
        {
            if (string.IsNullOrEmpty(_currentFolderPath)) return;

            int previewCount = _renamePreview.Count;

            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                _resources.GetString("TagManagerRenameConfirmTitle"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    _resources.GetString("TagManagerRenameConfirmMessage"),
                    previewCount, _renamePattern));

            if (!confirmed) return;

            IsLoading = true;
            try
            {
                IReadOnlyList<(string FilePath, AudioTag Tag)> filesWithTags =
                    await _tagService.ReadFolderAsync(_currentFolderPath);

                int renamedCount = await _fileRenameService.RenameAsync(filesWithTags, _renamePattern);

                // Ordner neu laden – Dateinamen haben sich verändert
                await LoadFolderAsync(_currentFolderPath);

                if (renamedCount < previewCount)
                {
                    await _errorDialogService.ShowAsync(
                        _resources.GetString("TagManagerRenamePartialErrorTitle"),
                        string.Format(
                            CultureInfo.CurrentCulture,
                            _resources.GetString("TagManagerRenamePartialErrorMessage"),
                            renamedCount, previewCount));
                }
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(_resources.GetString("TagManagerRenameErrorTitle"), ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Befüllt alle Formularfelder mit den Werten des gegebenen <see cref="AudioTag"/>.
        /// Setzt HasUnsavedChanges bewusst nicht – das geschieht erst nach echter Nutzeränderung.
        /// </summary>
        private void PopulateTagFields(AudioTag tag)
        {
            // HasUnsavedChanges darf hier nicht gesetzt werden – wir laden nur Daten, ändern nichts.
            // Die Setter rufen SetProperty auf, was PropertyChanged feuert – genug für die UI.
            _title       = tag.Title;
            _album       = tag.Album;
            _artist      = tag.Artist;
            _albumArtist = tag.AlbumArtist;
            _year        = tag.Year?.ToString(CultureInfo.InvariantCulture);
            _trackNumber = tag.TrackNumber?.ToString(CultureInfo.InvariantCulture);
            _trackCount  = tag.TrackCount?.ToString(CultureInfo.InvariantCulture);
            _genre       = tag.Genre;

            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Album));
            OnPropertyChanged(nameof(Artist));
            OnPropertyChanged(nameof(AlbumArtist));
            OnPropertyChanged(nameof(Year));
            OnPropertyChanged(nameof(TrackNumber));
            OnPropertyChanged(nameof(TrackCount));
            OnPropertyChanged(nameof(Genre));

            // Cover aus Byte-Array laden – MemoryStream wird in BitmapImage konvertiert
            if (tag.CoverImageData is not null && tag.CoverImageData.Length > 0)
            {
                _coverImageData = tag.CoverImageData;
                _coverMimeType  = tag.CoverMimeType;

                BitmapImage bitmap = new();
                using MemoryStream stream = new(tag.CoverImageData);
                bitmap.SetSource(stream.AsRandomAccessStream());
                CoverImage = bitmap;
            }
            else
            {
                _coverImageData = null;
                _coverMimeType  = null;
                CoverImage      = null;
            }
        }

        private void ClearTagFields()
        {
            _editedFields.Clear();
            _title = _album = _artist = _albumArtist = _year = _trackNumber = _trackCount = _genre = null;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Album));
            OnPropertyChanged(nameof(Artist));
            OnPropertyChanged(nameof(AlbumArtist));
            OnPropertyChanged(nameof(Year));
            OnPropertyChanged(nameof(TrackNumber));
            OnPropertyChanged(nameof(TrackCount));
            OnPropertyChanged(nameof(Genre));
            _coverImageData = null;
            _coverMimeType  = null;
            CoverImage      = null;
            HasUnsavedChanges = false;
        }

        /// <summary>
        /// Baut ein <see cref="AudioTag"/> aus den aktuellen Formularfeldern zusammen.
        /// Leere Strings werden als <see langword="null"/> übergeben, damit TagLib# bestehende Werte entfernt.
        /// Jahreszahl und Tracknummern werden aus Strings geparst – ungültige Eingaben werden ignoriert.
        /// </summary>
        private AudioTag BuildAudioTagFromFields()
        {
            return new AudioTag
            {
                Title       = NullIfEmpty(Title),
                Album       = NullIfEmpty(Album),
                Artist      = NullIfEmpty(Artist),
                AlbumArtist = NullIfEmpty(AlbumArtist),
                Genre       = NullIfEmpty(Genre),
                Year        = uint.TryParse(Year, out uint year) ? year : null,
                TrackNumber = uint.TryParse(TrackNumber, out uint trackNum) ? trackNum : null,
                TrackCount  = uint.TryParse(TrackCount, out uint trackCnt) ? trackCnt : null
            };
        }

        private static string? NullIfEmpty(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value;

        /// <summary>
        /// Aktualisiert den aktivierten/deaktivierten Zustand aller Commands anhand des aktuellen Zustands.
        /// Muss nach jeder Zustandsänderung aufgerufen werden, die CanExecute beeinflusst.
        /// </summary>
        private void RefreshCommandStates()
        {
            bool hasSelection = HasSelectedFile;
            _saveCommand.SetEnabled(HasUnsavedChanges && hasSelection);
            _saveAllCommand.SetEnabled(HasFiles && _files.Any(f => f.IsModified));
            _removeAllTagsCommand.SetEnabled(SelectedFile is not null);
            _lookupOnlineCommand.SetEnabled(hasSelection && !IsLookingUp);
            _autoLookupCommand.SetEnabled(HasFiles && !IsLookingUp && !string.IsNullOrEmpty(_currentFolderPath));
            _applyToAllCommand.SetEnabled(HasFiles && _pendingBatchTag is not null);
            _removeCoverCommand.SetEnabled(SelectedFile is not null && CoverImage is not null);
            _loadCoverCommand.SetEnabled(hasSelection);
            _applyCoverToAllCommand.SetEnabled(HasFiles && _coverImageData is not null);

            // Vorschau: nur wenn Dateien geladen sind und ein nicht-leeres Muster vorhanden ist
            _previewRenameCommand.SetEnabled(
                HasFiles && !string.IsNullOrWhiteSpace(_renamePattern));

            // Umbenennen: erst nach Berechnung der Vorschau
            _executeRenameCommand.SetEnabled(_renamePreview.Count > 0);
        }
    }
}

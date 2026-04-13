using EchoPlay.App.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für die Dateiliste des Tag-Managers.
    /// Hält die geladenen Dateien, den aktuell geöffneten Ordnerpfad und die Auswahl
    /// (Einzel- und Mehrfachauswahl). Die tatsächlichen Datei-Operationen (Laden aus
    /// Ordner, Tags speichern) koordiniert das übergeordnete <see cref="TagManagerViewModel"/>,
    /// weil sie den Tag-Service und Dialoge brauchen.
    /// </summary>
    public sealed class TagFileListViewModel : ObservableObject
    {
        private ObservableCollection<TagFileItemViewModel> _files = [];
        private TagFileItemViewModel? _selectedFile;
        private IReadOnlyList<TagFileItemViewModel> _selectedFiles = [];
        private string? _currentFolderPath;

        /// <summary>Liste aller Audiodateien im geöffneten Ordner.</summary>
        public ObservableCollection<TagFileItemViewModel> Files
        {
            get => _files;
            private set
            {
                if (SetProperty(ref _files, value))
                {
                    OnPropertyChanged(nameof(HasFiles));
                }
            }
        }

        /// <summary>
        /// Aktuell ausgewählte Datei (nur bei Einzelauswahl gesetzt).
        /// Wird intern über <see cref="SetSelectedFiles"/> gesetzt.
        /// </summary>
        public TagFileItemViewModel? SelectedFile
        {
            get => _selectedFile;
            private set
            {
                if (SetProperty(ref _selectedFile, value))
                {
                    OnPropertyChanged(nameof(HasSelectedFile));
                }
            }
        }

        /// <summary>
        /// Alle aktuell ausgewählten Dateien (bei Mehrfachauswahl).
        /// </summary>
        public IReadOnlyList<TagFileItemViewModel> SelectedFiles
        {
            get => _selectedFiles;
            private set
            {
                if (SetProperty(ref _selectedFiles, value))
                {
                    OnPropertyChanged(nameof(HasSelectedFile));
                }
            }
        }

        /// <summary>Ob mindestens eine Datei in der Liste vorhanden ist.</summary>
        public bool HasFiles => _files.Count > 0;

        /// <summary>
        /// Ob die Formularfelder aktiv sind – bei Einzel- oder Mehrfachauswahl.
        /// </summary>
        public bool HasSelectedFile => _selectedFile is not null || _selectedFiles.Count > 1;

        /// <summary>
        /// Aktuell geöffneter Ordnerpfad. Wird nach <see cref="SetFiles"/> gesetzt und
        /// von Auto-Lookup, Rename-Vorschau und Rename-Ausführung im Top-VM verwendet.
        /// </summary>
        public string? CurrentFolderPath
        {
            get => _currentFolderPath;
            private set => SetProperty(ref _currentFolderPath, value);
        }

        /// <summary>
        /// Wird ausgelöst, wenn sich die Auswahl geändert hat – das Top-VM lädt daraufhin
        /// die Tag-Felder der neuen Auswahl und aktualisiert den Editor-Zustand.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Sub-VM -> Top-VM-Bridge: meldet die aktuelle Multi-Auswahl an den Orchestrator (LoadMultipleFileTagsAsync); Action<IReadOnlyList<...>> bleibt semantisch klarer als ein EventArgs-Wrapper mit identischem Inhalt.")]
        public event Action<IReadOnlyList<TagFileItemViewModel>>? SelectionChanged;

        /// <summary>
        /// Ersetzt die geladene Datei-Liste und den Ordnerpfad und leert die Auswahl.
        /// Wird vom Top-VM nach <c>LoadFolderAsync</c> aufgerufen.
        /// </summary>
        /// <param name="files">Die neu geladenen Dateien.</param>
        /// <param name="folderPath">Der zugehörige Ordnerpfad.</param>
        public void SetFiles(IReadOnlyList<TagFileItemViewModel> files, string folderPath)
        {
            CurrentFolderPath = folderPath;
            Files             = new ObservableCollection<TagFileItemViewModel>(files);
            SelectedFile      = null;
            SelectedFiles     = [];
        }

        /// <summary>
        /// Leert die Datei-Liste komplett. Wird vor einem Reload aufgerufen, damit die UI
        /// sofort reagiert, noch bevor der neue Ordner geladen ist.
        /// </summary>
        public void Clear()
        {
            Files             = [];
            SelectedFile      = null;
            SelectedFiles     = [];
            CurrentFolderPath = null;
        }

        /// <summary>
        /// Wird vom Page-Code-Behind bei <c>SelectionChanged</c> aufgerufen. Entscheidet ob
        /// Einzel- oder Mehrfachauswahl vorliegt und löst das <see cref="SelectionChanged"/>-Event
        /// aus, damit das Top-VM die Tag-Felder neu laden kann.
        /// </summary>
        /// <param name="selectedItems">Die aktuell selektierten Dateien.</param>
        public void SetSelectedFiles(IReadOnlyList<TagFileItemViewModel> selectedItems)
        {
            ArgumentNullException.ThrowIfNull(selectedItems);
            SelectedFiles = selectedItems;
            SelectedFile  = selectedItems.Count == 1 ? selectedItems[0] : null;

            SelectionChanged?.Invoke(selectedItems);
        }
    }
}

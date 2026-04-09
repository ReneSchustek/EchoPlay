using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;
using System.IO;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Repräsentiert eine einzelne Audiodatei in der Dateiliste des Tag-Managers.
    /// Merkt sich, ob die Tags der Datei seit dem letzten Speichern verändert wurden.
    /// </summary>
    public sealed class TagFileItemViewModel : ObservableObject
    {
        private bool _isModified;

        /// <summary>
        /// Initialisiert das ViewModel mit dem vollständigen Dateipfad.
        /// </summary>
        /// <param name="filePath">Absoluter Pfad zur Audiodatei.</param>
        /// <param name="baseFolderPath">
        /// Stammordner für die Berechnung des relativen Pfads.
        /// Wenn null, wird nur der Dateiname angezeigt.
        /// </param>
        public TagFileItemViewModel(string filePath, string? baseFolderPath = null)
        {
            FilePath = filePath;
            FileName = Path.GetFileName(filePath);

            // Relativer Pfad ab Stammordner – zeigt Unterordnerstruktur
            if (baseFolderPath is not null)
            {
                RelativePath = Path.GetRelativePath(baseFolderPath, filePath);
            }
            else
            {
                RelativePath = FileName;
            }
        }

        /// <summary>Absoluter Pfad zur Audiodatei.</summary>
        public string FilePath { get; }

        /// <summary>
        /// Nur der Dateiname ohne Pfad – für interne Verwendung.
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// Relativer Pfad ab dem Stammordner – zeigt Unterordner in der Dateiliste.
        /// Z.B. „001 - Der Super-Papagei\track01.mp3".
        /// </summary>
        public string RelativePath { get; }

        /// <summary>
        /// Gibt an, ob die Tags der Datei seit dem letzten Speichern geändert wurden.
        /// Wird in der UI als Markierung neben dem Dateinamen angezeigt.
        /// </summary>
        public bool IsModified
        {
            get => _isModified;
            set
            {
                SetProperty(ref _isModified, value);
                OnPropertyChanged(nameof(IsModifiedVisibility));
            }
        }

        /// <summary>
        /// Sichtbarkeit des Änderungshinweises – sichtbar wenn <see cref="IsModified"/> <c>true</c> ist.
        /// </summary>
        public Visibility IsModifiedVisibility => IsModified ? Visibility.Visible : Visibility.Collapsed;
    }
}

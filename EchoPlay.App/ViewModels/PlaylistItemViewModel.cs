using Microsoft.UI.Xaml;
using System.IO;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Repräsentiert einen einzelnen Track in der Playlist des Players.
    /// Enthält Anzeigename, Pfad und den Hervorhebungsstatus für den aktuell spielenden Track.
    /// </summary>
    public sealed class PlaylistItemViewModel : ObservableObject
    {
        private bool _isCurrentTrack;

        /// <summary>
        /// Initialisiert das Playlist-Element.
        /// </summary>
        /// <param name="index">Nullbasierter Index in der Playlist.</param>
        /// <param name="fullPath">Vollständiger Dateipfad der Audiodatei.</param>
        public PlaylistItemViewModel(int index, string fullPath)
        {
            Index    = index;
            FullPath = fullPath;
            // Nur der Dateiname ohne Erweiterung wird angezeigt – der Pfad ist für die Playlist irrelevant.
            // Erster Buchstabe wird großgeschrieben, weil Kassetten-Rips und manche Dateinamen
            // mit Kleinbuchstaben beginnen (z.B. "01a spuk in der werkstatt").
            string rawName = Path.GetFileNameWithoutExtension(fullPath);
            FileName = rawName.Length > 0 && char.IsLower(rawName[0])
                ? char.ToUpperInvariant(rawName[0]) + rawName[1..]
                : rawName;
        }

        /// <summary>Nullbasierter Index in der Playlist.</summary>
        public int Index { get; }

        /// <summary>Einsbasierte Zeilennummer für die Anzeige in der UI.</summary>
        public int DisplayIndex => Index + 1;

        /// <summary>Anzeigename des Tracks (Dateiname ohne Erweiterung).</summary>
        public string FileName { get; }

        /// <summary>Vollständiger Dateipfad – wird für die Wiedergabe benötigt.</summary>
        public string FullPath { get; }

        /// <summary>
        /// Gibt an, ob dieser Track gerade abgespielt wird.
        /// Steuert die visuelle Hervorhebung in der Playlist.
        /// </summary>
        public bool IsCurrentTrack
        {
            get => _isCurrentTrack;
            set
            {
                if (SetProperty(ref _isCurrentTrack, value))
                {
                    OnPropertyChanged(nameof(IsCurrentTrackVisibility));
                    OnPropertyChanged(nameof(NotCurrentTrackVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Play-Icons: sichtbar wenn dieser Track aktiv ist.</summary>
        public Visibility IsCurrentTrackVisibility =>
            _isCurrentTrack ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit der Zeilennummer: sichtbar wenn dieser Track nicht aktiv ist.</summary>
        public Visibility NotCurrentTrackVisibility =>
            _isCurrentTrack ? Visibility.Collapsed : Visibility.Visible;
    }
}

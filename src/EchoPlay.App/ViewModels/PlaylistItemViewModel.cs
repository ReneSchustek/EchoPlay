using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Parsing;
using Microsoft.UI.Xaml;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Repräsentiert einen einzelnen Track in der Playlist des Players.
    /// Enthält Anzeigename, Pfad und den Hervorhebungsstatus für den aktuell spielenden Track.
    /// </summary>
    public sealed class PlaylistItemViewModel : ObservableObject, ITrackTitleTarget
    {
        private bool _isCurrentTrack;
        private string _title;

        /// <summary>
        /// Initialisiert das Playlist-Element.
        /// </summary>
        /// <param name="index">Nullbasierter Index in der Playlist.</param>
        /// <param name="fullPath">Vollständiger Dateipfad der Audiodatei.</param>
        public PlaylistItemViewModel(int index, string fullPath)
        {
            Index = index;
            FullPath = fullPath;

            // Bis der Titel aus der Kennzeichnung gelesen ist, steht hier der aufgeräumte
            // Dateiname – ohne Endung und ohne die Zeilennummer, die links daneben steht.
            _title = TrackDisplayTitle.FromFilePath(fullPath, index + 1);
        }

        /// <summary>Nullbasierter Index in der Playlist.</summary>
        public int Index { get; }

        /// <summary>Einsbasierte Zeilennummer für die Anzeige in der UI.</summary>
        public int DisplayIndex => Index + 1;

        /// <summary>
        /// Angezeigter Titel des Tracks: der Titel aus der Kennzeichnung der Datei,
        /// ersatzweise der Dateiname ohne Endung und ohne die daneben stehende Nummer.
        /// </summary>
        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        /// <summary>Vollständiger Dateipfad – wird für die Wiedergabe benötigt.</summary>
        public string FullPath { get; }

        /// <inheritdoc/>
        string ITrackTitleTarget.FilePath => FullPath;

        /// <inheritdoc/>
        int ITrackTitleTarget.TrackNumber => DisplayIndex;

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

        /// <summary>
        /// Übernimmt den aus der Kennzeichnung gelesenen Titel.
        /// Ein leerer Wert wird verworfen, damit die Zeile nie ohne Text dasteht.
        /// </summary>
        /// <param name="resolvedTitle">Der gelesene Titel oder <see langword="null"/>.</param>
        public void ApplyTitle(string? resolvedTitle)
        {
            if (!string.IsNullOrWhiteSpace(resolvedTitle))
            {
                Title = resolvedTitle;
            }
        }
    }
}

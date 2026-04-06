using System;
using System.Globalization;
using System.IO;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Zeilen-ViewModel für einen lokalen Track in der rechten Spalte der lokalen Mediathek.
    /// Der "Im Tag-Manager bearbeiten"-Befehl delegiert die Navigation über einen Callback
    /// an <see cref="MediathekLokalViewModel"/>, der das <see cref="MediathekLokalViewModel.NavigateToTagManagerRequested"/>-Event feuert.
    /// ViewModels sollen nicht direkt navigieren – die Page-Ebene übernimmt das.
    /// </summary>
    public sealed class LocalTrackRowViewModel
    {
        /// <summary>
        /// Erstellt ein Zeilen-ViewModel für einen lokalen Track.
        /// </summary>
        /// <param name="trackId">Datenbank-ID des Tracks.</param>
        /// <param name="trackNumber">Tracknummer innerhalb der Episode (1-basiert).</param>
        /// <param name="filePath">Absoluter Dateipfad der Audiodatei.</param>
        /// <param name="duration">Abspieldauer.</param>
        /// <param name="requestTagManagerNavigation">
        /// Callback zum Auslösen der Tag-Manager-Navigation.
        /// Erhält den Ordnerpfad der Audiodatei als Argument.
        /// </param>
        public LocalTrackRowViewModel(
            Guid trackId,
            int trackNumber,
            string filePath,
            TimeSpan duration,
            Action<string> requestTagManagerNavigation)
        {
            TrackId     = trackId;
            TrackNumber = trackNumber;
            FilePath    = filePath;
            FileName    = Path.GetFileName(filePath);
            Duration    = duration;

            OpenInTagManagerCommand = new RelayCommand(() =>
            {
                // Den übergeordneten Ordner übergeben – der Tag-Manager lädt alle Audiodateien
                // des Ordners, sodass der Nutzer die gewünschte Datei direkt auswählen kann
                string? folder = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    requestTagManagerNavigation(folder);
                }
            });
        }

        /// <summary>Datenbank-ID des Tracks.</summary>
        public Guid TrackId { get; }

        /// <summary>Tracknummer innerhalb der Episode (1-basiert).</summary>
        public int TrackNumber { get; }

        /// <summary>Dateiname ohne Verzeichnispfad.</summary>
        public string FileName { get; }

        /// <summary>Absoluter Dateipfad der Audiodatei.</summary>
        public string FilePath { get; }

        /// <summary>Abspieldauer der Audiodatei.</summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// Dauer als formatierter Anzeigetext.
        /// Über einer Stunde im Format "1:23:45", darunter "12:34".
        /// </summary>
        public string DurationText => Duration.TotalHours >= 1
            ? Duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : Duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);

        /// <summary>
        /// Öffnet den Tag-Manager mit dem Episodenordner dieses Tracks.
        /// Alle Audiodateien im gleichen Ordner werden im Tag-Manager geladen.
        /// </summary>
        public ICommand OpenInTagManagerCommand { get; }
    }
}

using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using Microsoft.UI.Xaml;
using System;
using System.Globalization;
using System.Text;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Alles, was die Wiedergabe an Zeit anzeigt: die Position in der laufenden Datei, die
    /// verstrichene und verbleibende Zeit, den Fortschritt über die ganze Folge und den
    /// Suchlauf am Regler.
    /// </summary>
    /// <remarks>
    /// Das sind zwei Zeitachsen nebeneinander — die der Datei und die der Folge. Wer beide im
    /// Ansichtsmodell des Players führt, hat dort acht Felder liegen, die nur miteinander zu
    /// tun haben und mit nichts sonst.
    /// </remarks>
    public sealed class PlaybackTimeDisplay : ObservableObject
    {
        private readonly IPlayerService _playerService;

        private static readonly CompositeFormat EpisodeProgressFormat =
            CompositeFormat.Parse(EchoPlay.App.Helpers.SafeResourceLoader.Get(
                "PlayerEpisodeProgressFormat", "{0} von {1}"));

        private double _positionSeconds;
        private double _durationSeconds;
        private double _episodeProgressPercent;
        private string _episodeProgressText = string.Empty;
        private string _elapsedText = "0:00";
        private string _remainingOrTotalText = "-0:00";

        // Während der Nutzer den Regler zieht, dürfen eingehende Positionsmeldungen ihn nicht
        // zurückspringen lassen.
        private bool _isSeeking;

        private bool _showRemainingTime = true;

        /// <summary>
        /// Richtet die Anzeige auf den Wiedergabedienst ein.
        /// </summary>
        /// <param name="playerService">Liefert Position und Dauer und nimmt den Suchlauf entgegen.</param>
        public PlaybackTimeDisplay(IPlayerService playerService)
        {
            _playerService = playerService;
            ToggleTimeDisplayCommand = new RelayCommand(ToggleTimeDisplay);
        }

        /// <summary>Aktuelle Abspielposition in Sekunden — der Wert des Reglers.</summary>
        public double PositionSeconds
        {
            get => _positionSeconds;
            set
            {
                // Nur während eines Suchlaufs schreiben, sonst kämpfen Regler und Dienst
                // gegeneinander.
                if (_isSeeking)
                {
                    _ = SetProperty(ref _positionSeconds, value);
                }
            }
        }

        /// <summary>Gesamtdauer der laufenden Datei in Sekunden — das Maximum des Reglers.</summary>
        public double DurationSeconds
        {
            get => _durationSeconds;
            private set => SetProperty(ref _durationSeconds, value);
        }

        /// <summary>
        /// Fortschritt in der ganzen Folge, in Prozent (0 bis 100).
        /// </summary>
        /// <remarks>
        /// Der Regler darüber zeigt die laufende Datei. Bei einer Folge aus vier Dateien sagt
        /// „Minute 12" allein nichts — erst dieser Wert beantwortet, wie weit man im Hörspiel ist.
        /// </remarks>
        public double EpisodeProgressPercent
        {
            get => _episodeProgressPercent;
            private set => SetProperty(ref _episodeProgressPercent, value);
        }

        /// <summary>Fortschritt der Folge als Text, etwa „1:12:30 von 4:41:44".</summary>
        public string EpisodeProgressText
        {
            get => _episodeProgressText;
            private set => SetProperty(ref _episodeProgressText, value);
        }

        /// <summary>
        /// Sichtbarkeit des Folgen-Fortschritts. Er erscheint nur, wenn die Gesamtdauer bekannt
        /// ist und die Folge aus mehr als einer Datei besteht — sonst doppelte er den Regler
        /// darüber.
        /// </summary>
        public Visibility EpisodeProgressVisibility =>
            _playerService.OverallDuration > TimeSpan.Zero && _playerService.CurrentTrackPaths.Count > 1
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>Verstrichene Zeit, etwa „3:45" oder „1:03:45".</summary>
        public string ElapsedText
        {
            get => _elapsedText;
            private set => SetProperty(ref _elapsedText, value);
        }

        /// <summary>
        /// Verbleibende Zeit („-23:45") oder Gesamtdauer („1:00:00"), je nach Umschaltung.
        /// </summary>
        public string RemainingOrTotalText
        {
            get => _remainingOrTotalText;
            private set => SetProperty(ref _remainingOrTotalText, value);
        }

        /// <summary>Wechselt die rechte Zeitanzeige zwischen verbleibender Zeit und Gesamtdauer.</summary>
        public ICommand ToggleTimeDisplayCommand { get; }

        /// <summary>
        /// Beginnt einen Suchlauf. Ab hier bestimmt der Regler die Position, nicht der Dienst.
        /// </summary>
        public void BeginSeek()
        {
            _isSeeking = true;
        }

        /// <summary>
        /// Beendet den Suchlauf und übergibt die gewählte Stelle an den Wiedergabedienst.
        /// </summary>
        public void CommitSeek()
        {
            _playerService.SeekTo(TimeSpan.FromSeconds(_positionSeconds));
            _isSeeking = false;
        }

        /// <summary>
        /// Zieht alle Zeitangaben aus dem Wiedergabedienst nach. Der Regler bleibt während
        /// eines Suchlaufs unberührt.
        /// </summary>
        public void Refresh()
        {
            if (!_isSeeking)
            {
                _ = SetProperty(ref _positionSeconds, _playerService.Position.TotalSeconds, nameof(PositionSeconds));
            }

            DurationSeconds = _playerService.Duration.TotalSeconds;
            UpdateTexts();
        }

        /// <summary>Setzt die Anzeige zurück, wenn keine Wiedergabe mehr läuft.</summary>
        public void Reset()
        {
            _ = SetProperty(ref _positionSeconds, 0d, nameof(PositionSeconds));
            DurationSeconds = 0;
            UpdateTexts();
        }

        private void ToggleTimeDisplay()
        {
            _showRemainingTime = !_showRemainingTime;
            UpdateTexts();
        }

        /// <summary>Schreibt die drei Textangaben und den Fortschritt der Folge neu.</summary>
        private void UpdateTexts()
        {
            TimeSpan position = _playerService.Position;
            TimeSpan duration = _playerService.Duration;

            ElapsedText = PlaybackTimeFormat.Format(position);
            UpdateEpisodeProgress();

            if (_showRemainingTime)
            {
                RemainingOrTotalText = "-" + PlaybackTimeFormat.Format(
                    PlaybackTimeFormat.Remaining(position, duration));
            }
            else
            {
                RemainingOrTotalText = PlaybackTimeFormat.Format(duration);
            }
        }

        /// <summary>Zieht den Fortschritt über die ganze Folge nach.</summary>
        private void UpdateEpisodeProgress()
        {
            TimeSpan overallPosition = _playerService.OverallPosition;
            TimeSpan overallDuration = _playerService.OverallDuration;

            EpisodeProgressPercent = PlaybackTimeFormat.Percent(overallPosition, overallDuration);

            EpisodeProgressText = overallDuration > TimeSpan.Zero
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    EpisodeProgressFormat,
                    PlaybackTimeFormat.Format(overallPosition),
                    PlaybackTimeFormat.Format(overallDuration))
                : string.Empty;

            OnPropertyChanged(nameof(EpisodeProgressVisibility));
        }

    }
}

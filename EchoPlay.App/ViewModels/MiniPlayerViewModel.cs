using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für den MiniPlayer am unteren Rand des Hauptfensters.
    /// Abonniert <see cref="IPlayerService.StateChanged"/> und leitet Zustandsänderungen
    /// thread-sicher an den UI-Thread weiter.
    /// </summary>
    public sealed class MiniPlayerViewModel : ObservableObject, IDisposable
    {
        private readonly IPlayerService _playerService;

        // Null außerhalb des UI-Threads (z.B. in Unit-Tests)
        private readonly DispatcherQueue? _dispatcherQueue;

        private string _trackTitle = string.Empty;
        private double _positionSeconds;
        private double _durationSeconds;
        private bool _isPlaying;
        private double _playbackRate = 1.0;
        private string _sleepTimerText = string.Empty;
        private string _elapsedText = string.Empty;
        private string _remainingText = string.Empty;
        private string _errorMessage = string.Empty;

        /// <summary>
        /// Initialisiert das ViewModel und registriert sich für Zustandsänderungen des PlayerService.
        /// </summary>
        /// <param name="playerService">Der zentrale Wiedergabe-Service.</param>
        public MiniPlayerViewModel(IPlayerService playerService)
        {
            _playerService = playerService;

            // GetForCurrentThread() wirft in WinRT-losen Prozessen (z.B. Unit-Tests) – daher try-catch.
            try
            {
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            }
            catch (Exception)
            {
                _dispatcherQueue = null;
            }

            PlayCommand     = new RelayCommand(() => _playerService.Resume());
            PauseCommand    = new RelayCommand(() => _playerService.Pause());
            StopCommand     = new RelayCommand(() => _playerService.Stop());
            NextCommand     = new RelayCommand(() => _playerService.SkipToNext());
            PreviousCommand = new RelayCommand(() => _playerService.SkipToPrevious());

            _playerService.StateChanged += OnStateChanged;
            _playerService.ErrorOccurred += OnErrorOccurred;
        }

        /// <summary>Titel des aktuell laufenden Tracks.</summary>
        public string TrackTitle
        {
            get => _trackTitle;
            private set => SetProperty(ref _trackTitle, value);
        }

        /// <summary>Aktuelle Position in Sekunden – für den Slider.</summary>
        public double PositionSeconds
        {
            get => _positionSeconds;
            private set => SetProperty(ref _positionSeconds, value);
        }

        /// <summary>Gesamtdauer des aktuellen Tracks in Sekunden – als Slider-Maximum.</summary>
        public double DurationSeconds
        {
            get => _durationSeconds;
            private set => SetProperty(ref _durationSeconds, value);
        }

        /// <summary>Gibt an, ob gerade Wiedergabe aktiv ist.</summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            private set => SetProperty(ref _isPlaying, value);
        }

        /// <summary>
        /// Wiedergabegeschwindigkeit. 1.0 = normal, 0.75 = gedrosselt, 2.0 = doppelt.
        /// Schreibzugriff propagiert den Wert direkt an den PlayerService.
        /// </summary>
        public double PlaybackRate
        {
            get => _playbackRate;
            set
            {
                if (SetProperty(ref _playbackRate, value))
                {
                    _playerService.PlaybackRate = value;
                }
            }
        }

        /// <summary>
        /// Formatierter Countdown des Einschlaf-Timers (z.B. "28:45").
        /// Leer, wenn kein Timer aktiv ist.
        /// </summary>
        public string SleepTimerText
        {
            get => _sleepTimerText;
            private set => SetProperty(ref _sleepTimerText, value);
        }

        /// <summary>Formatierte bereits gespielte Zeit, z.B. "3:45".</summary>
        public string ElapsedText
        {
            get => _elapsedText;
            private set => SetProperty(ref _elapsedText, value);
        }

        /// <summary>Formatierte verbleibende Zeit, z.B. "-56:15".</summary>
        public string RemainingText
        {
            get => _remainingText;
            private set => SetProperty(ref _remainingText, value);
        }

        /// <summary>
        /// Letzte Fehlermeldung des PlayerService.
        /// Leer, wenn kein Fehler vorliegt. Wird bei jedem neuen StateChanged zurückgesetzt.
        /// </summary>
        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        /// <summary>
        /// Sichtbarkeit des MiniPlayers – eingeblendet sobald ein Track geladen ist.
        /// </summary>
        public Visibility MiniPlayerVisibility =>
            string.IsNullOrEmpty(_trackTitle) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Startet die Wiedergabe.</summary>
        public RelayCommand PlayCommand { get; }

        /// <summary>Pausiert die Wiedergabe.</summary>
        public RelayCommand PauseCommand { get; }

        /// <summary>Stoppt die Wiedergabe und blendet den MiniPlayer aus.</summary>
        public RelayCommand StopCommand { get; }

        /// <summary>Springt zum nächsten Track.</summary>
        public RelayCommand NextCommand { get; }

        /// <summary>Springt zum vorherigen Track.</summary>
        public RelayCommand PreviousCommand { get; }

        /// <summary>
        /// Springt zu einer bestimmten Position im aktuellen Track.
        /// Wird durch den Slider im MiniPlayer ausgelöst.
        /// </summary>
        /// <param name="seconds">Zielposition in Sekunden.</param>
        public void SeekTo(double seconds)
        {
            _playerService.SeekTo(TimeSpan.FromSeconds(seconds));
        }

        /// <summary>
        /// Setzt oder deaktiviert den Einschlaf-Timer.
        /// </summary>
        /// <param name="duration">Zeitspanne bis zum automatischen Stopp. Null deaktiviert den Timer.</param>
        public void SetSleepTimer(TimeSpan? duration)
        {
            _playerService.SetSleepTimer(duration);
        }

        /// <summary>
        /// Gibt Ressourcen frei und meldet sich vom PlayerService ab.
        /// </summary>
        public void Dispose()
        {
            _playerService.StateChanged -= OnStateChanged;
            _playerService.ErrorOccurred -= OnErrorOccurred;
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            // StateChanged kann aus dem Timer-Thread kommen – UI-Dispatch erforderlich.
            // In Tests gibt es keinen UI-Thread, daher direkt aktualisieren.
            if (_dispatcherQueue is not null)
            {
                _dispatcherQueue.TryEnqueue(UpdateFromState);
            }
            else
            {
                UpdateFromState();
            }
        }

        private void OnErrorOccurred(object? sender, string message)
        {
            // ErrorOccurred kann aus beliebigen Threads kommen – UI-Dispatch erforderlich
            if (_dispatcherQueue is not null)
            {
                _dispatcherQueue.TryEnqueue(() => ErrorMessage = message);
            }
            else
            {
                ErrorMessage = message;
            }
        }

        private void UpdateFromState()
        {
            // Fehlermeldung bei normaler Zustandsänderung zurücksetzen
            ErrorMessage    = string.Empty;
            TrackTitle      = _playerService.CurrentTrackTitle ?? string.Empty;
            PositionSeconds = _playerService.Position.TotalSeconds;
            DurationSeconds = _playerService.Duration.TotalSeconds;
            IsPlaying       = _playerService.IsPlaying;
            SleepTimerText  = FormatSleepTimer(_playerService.SleepTimerRemaining);

            // Zeitanzeige: gespielt und verbleibend
            TimeSpan position = _playerService.Position;
            TimeSpan duration = _playerService.Duration;
            ElapsedText = FormatTime(position);
            TimeSpan remaining = duration - position;
            RemainingText = remaining > TimeSpan.Zero ? "-" + FormatTime(remaining) : FormatTime(duration);

            OnPropertyChanged(nameof(MiniPlayerVisibility));
        }

        /// <summary>
        /// Formatiert eine Zeitspanne als lesbaren Text.
        /// Unter einer Stunde: "m:ss", ab einer Stunde: "h:mm:ss".
        /// </summary>
        private static string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
            {
                return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
            }

            return $"{(int)time.TotalMinutes}:{time.Seconds:D2}";
        }

        /// <summary>
        /// Formatiert die verbleibende Sleep-Timer-Zeit als "MM:ss" mit Gesamtminuten.
        /// Gibt <see cref="string.Empty"/> zurück wenn kein Timer aktiv ist.
        /// </summary>
        /// <param name="remaining">Verbleibende Zeitspanne oder null.</param>
        /// <returns>Z.B. "28:45", "60:00" oder <see cref="string.Empty"/>.</returns>
        private static string FormatSleepTimer(TimeSpan? remaining)
        {
            if (remaining is null || remaining.Value <= TimeSpan.Zero)
            {
                return string.Empty;
            }

            return $"{(int)remaining.Value.TotalMinutes:D2}:{remaining.Value.Seconds:D2}";
        }
    }
}

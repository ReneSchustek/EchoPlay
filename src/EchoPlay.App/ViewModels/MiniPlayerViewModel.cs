using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Parsing;
using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
        private readonly IServiceScopeFactory _scopeFactory;

        // Null außerhalb des UI-Threads (z.B. in Unit-Tests)
        private readonly DispatcherQueue? _dispatcherQueue;

        private string? _titleSourcePath;
        private string _trackTitle = string.Empty;
        private double _positionSeconds;
        private double _durationSeconds;
        private double _episodeProgressPercent;
        private string _episodeProgressText = string.Empty;

        // Einmal zerlegt statt bei jedem Positions-Tick: Die Anzeige frischt zweimal je
        // Sekunde auf, und das Muster ändert sich dabei nie.
        private static readonly System.Text.CompositeFormat EpisodeProgressFormat =
            System.Text.CompositeFormat.Parse(
                EchoPlay.App.Helpers.SafeResourceLoader.Get("MiniPlayerEpisodeProgressFormat", "Folge {0} %"));
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
        /// <param name="scopeFactory">Fabrik für den Scope, aus dem der Titel der Spur gelesen wird.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "DispatcherQueue.GetForCurrentThread() wirft in WinRT-losen Prozessen (Unit-Test-Host) native/COM-Fehler; der Fallback auf 'null' erlaubt das VM auch außerhalb von WinUI zu konstruieren.")]
        public MiniPlayerViewModel(IPlayerService playerService, IServiceScopeFactory scopeFactory)
        {
            _playerService = playerService;
            _scopeFactory = scopeFactory;
            Volume = new VolumeControl(playerService, scopeFactory);

            // GetForCurrentThread() wirft in WinRT-losen Prozessen (z.B. Unit-Tests) – daher try-catch.
            try
            {
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            }
            catch (Exception)
            {
                _dispatcherQueue = null;
            }

            PlayCommand = new RelayCommand(() => InvokeWithScope("MiniPlayerPlay", () => _playerService.Resume()));
            PauseCommand = new RelayCommand(() => InvokeWithScope("MiniPlayerPause", () => _playerService.Pause()));
            StopCommand = new RelayCommand(() => InvokeWithScope("MiniPlayerStop", () => _playerService.Stop()));
            NextCommand = new RelayCommand(() => InvokeWithScope("MiniPlayerNext", () => _playerService.SkipToNext()));
            PreviousCommand = new RelayCommand(() => InvokeWithScope("MiniPlayerPrevious", () => _playerService.SkipToPrevious()));

            _playerService.StateChanged += OnStateChanged;
            _playerService.ErrorOccurred += OnErrorOccurred;
        }

        /// <summary>Der Vorgang, der den Titel aus der Kennzeichnung nachlädt — für Tests.</summary>
        internal Task PendingTitleLoad { get; private set; } = Task.CompletedTask;

        /// <summary>Titel des aktuell laufenden Tracks.</summary>
        public string TrackTitle
        {
            get => _trackTitle;
            private set => SetProperty(ref _trackTitle, value);
        }

        /// <summary>
        /// Lautstärke und Stummschaltung. Beide Wiedergabe-Ansichten zeigen denselben Wert,
        /// weil beide denselben Wiedergabedienst bedienen.
        /// </summary>
        public VolumeControl Volume { get; }

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

        /// <summary>
        /// Fortschritt in der ganzen Folge, in Prozent (0–100).
        /// </summary>
        /// <remarks>
        /// Im Mini-Player ist kein Platz für zwei Regler. Der vorhandene zeigt die laufende
        /// Datei; dieser schmale Balken darunter beantwortet die Frage, die bei einer Folge
        /// aus mehreren Dateien offen bleibt: wie weit bin ich im Hörspiel.
        /// </remarks>
        public double EpisodeProgressPercent
        {
            get => _episodeProgressPercent;
            private set => SetProperty(ref _episodeProgressPercent, value);
        }

        /// <summary>
        /// Beschriftung des Folgen-Balkens, etwa „Folge 49 %".
        /// </summary>
        /// <remarks>
        /// Ohne sie ist ein schmaler Balken unter der Zeitanzeige nicht zu deuten — er sieht
        /// aus wie ein zweiter Positionsregler, meint aber die ganze Folge.
        /// </remarks>
        public string EpisodeProgressText
        {
            get => _episodeProgressText;
            private set => SetProperty(ref _episodeProgressText, value);
        }

        /// <summary>
        /// Sichtbarkeit des Folgen-Fortschritts — nur bei bekannter Gesamtdauer und mehr
        /// als einer Datei.
        /// </summary>
        public Visibility EpisodeProgressVisibility =>
            _playerService.OverallDuration > TimeSpan.Zero && _playerService.CurrentTrackPaths.Count > 1
                ? Visibility.Visible
                : Visibility.Collapsed;

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

        // Hilfs-Methode: alle MiniPlayer-Befehle müssen sich auf einer Korrelations-ID
        // im Log ablegen. Vermeidet Code-Duplikation in den fünf RelayCommand-Lambdas.
        private static void InvokeWithScope(string actionName, Action action)
        {
            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction(actionName);
            action();
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            // StateChanged kann aus dem Timer-Thread kommen – UI-Dispatch erforderlich.
            // In Tests gibt es keinen UI-Thread, daher direkt aktualisieren.
            if (_dispatcherQueue is not null)
            {
                _ = _dispatcherQueue.TryEnqueue(UpdateFromState);
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
                _ = _dispatcherQueue.TryEnqueue(() => ErrorMessage = message);
            }
            else
            {
                ErrorMessage = message;
            }
        }

        private void UpdateFromState()
        {
            // Fehlermeldung bei normaler Zustandsänderung zurücksetzen
            ErrorMessage = string.Empty;
            UpdateTrackTitle();
            PositionSeconds = _playerService.Position.TotalSeconds;
            DurationSeconds = _playerService.Duration.TotalSeconds;
            IsPlaying = _playerService.IsPlaying;
            SleepTimerText = FormatSleepTimer(_playerService.SleepTimerRemaining);

            // Zeitanzeige: gespielt und verbleibend
            TimeSpan position = _playerService.Position;
            TimeSpan duration = _playerService.Duration;
            ElapsedText = PlaybackTimeFormat.Format(position);

            // Bleibt nichts mehr übrig, steht dort die Gesamtdauer statt „-0:00".
            TimeSpan remaining = PlaybackTimeFormat.Remaining(position, duration);
            RemainingText = remaining > TimeSpan.Zero
                ? "-" + PlaybackTimeFormat.Format(remaining)
                : PlaybackTimeFormat.Format(duration);

            TimeSpan overallDuration = _playerService.OverallDuration;
            EpisodeProgressPercent = PlaybackTimeFormat.Percent(
                _playerService.OverallPosition, overallDuration);

            EpisodeProgressText = overallDuration > TimeSpan.Zero
                ? string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    EpisodeProgressFormat,
                    Math.Round(EpisodeProgressPercent))
                : string.Empty;

            OnPropertyChanged(nameof(EpisodeProgressVisibility));
            OnPropertyChanged(nameof(MiniPlayerVisibility));
        }

        /// <summary>
        /// Setzt den angezeigten Titel: sofort den aufgeräumten Dateinamen, danach — sobald
        /// gelesen — den Titel aus der Kennzeichnung. Damit steht unten dasselbe wie in der
        /// Wiedergabeliste der Player-Seite.
        /// </summary>
        private void UpdateTrackTitle()
        {
            string? path = _playerService.CurrentTrackPath;

            if (path is null)
            {
                TrackTitle = _playerService.CurrentTrackTitle ?? string.Empty;
                _titleSourcePath = null;
                return;
            }

            if (string.Equals(path, _titleSourcePath, StringComparison.OrdinalIgnoreCase))
            {
                // Derselbe Track wie beim letzten Tick – der Titel steht schon.
                return;
            }

            _titleSourcePath = path;
            TrackTitle = TrackDisplayTitle.FromFilePath(path, TrackDisplayTitle.NoNumberShown);
            PendingTitleLoad = LoadTitleFromTagAsync(path);
        }

        /// <summary>
        /// Liest den Titel aus der Kennzeichnung nach. Wechselt der Track zwischenzeitlich,
        /// wird das Ergebnis verworfen.
        /// </summary>
        private async Task LoadTitleFromTagAsync(string path)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ITrackTitleResolver resolver = scope.ServiceProvider.GetRequiredService<ITrackTitleResolver>();

            IReadOnlyList<string> titles = await resolver.ResolveAsync(
                [new TrackTitleRequest(path, TrackDisplayTitle.NoNumberShown)]);

            if (titles.Count > 0 && string.Equals(path, _titleSourcePath, StringComparison.OrdinalIgnoreCase))
            {
                TrackTitle = titles[0];
            }
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

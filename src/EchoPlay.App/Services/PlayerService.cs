using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Singleton-Service für die Audiowiedergabe.
    /// Kapselt <see cref="MediaPlayer"/> und <see cref="MediaPlaybackList"/> und stellt
    /// eine stabile, ereignisbasierte API für den MiniPlayer und die Episodenliste bereit.
    /// </summary>
    public sealed class PlayerService : IPlayerService, IAsyncDisposable, IDisposable
    {
        // Auto-Save alle 30 Sekunden während aktiver Wiedergabe (500 ms × 60 Ticks)
        private const int AutoSaveIntervalTicks = 60;

        private readonly MediaPlayer _player;
        private readonly MediaPlaybackList _playlist;

        // Die Spurdauern der laufenden Folge. Ohne sie kennt der Dienst nur die Stelle in
        // der laufenden Datei — und die allein taugt nicht zum Fortsetzen.
        private EpisodeTimeline _timeline = EpisodeTimeline.Empty;
        private readonly System.Timers.Timer _positionTimer;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private readonly IClock _clock;

        // _stateLock schützt die veränderlichen Felder dieses Dienstes.
        private readonly object _stateLock = new();
        private IReadOnlyList<string> _currentTrackPaths = [];
        private readonly PlaybackStateWriter _stateWriter;

        private Guid _currentEpisodeId;
        private readonly SleepTimer _sleepTimer = new();
        private int _autoSaveTick;
        private bool _disposed;

        /// <summary>
        /// Initialisiert den PlayerService und konfiguriert die Wiedergabeliste.
        /// </summary>
        /// <param name="scopeFactory">Fabrik für DI-Scopes (für PlaybackState-Persistenz).</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        /// <param name="clock">Zeitquelle für Zeitstempel.</param>
        public PlayerService(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory, IClock clock)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("PlayerService");
            _clock = clock;
            _stateWriter = new PlaybackStateWriter(scopeFactory, _logger, clock);

            _player = new();
            _playlist = new();
            _player.Source = _playlist;

            // 500 ms-Takt für Positionsanzeige, Auto-Save und Sleep-Timer-Countdown
            _positionTimer = new(500);
            _positionTimer.Elapsed += OnPositionTimerElapsed;

            _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            _playlist.CurrentItemChanged += OnCurrentItemChanged;
            _player.MediaFailed += OnMediaFailed;
        }

        /// <summary>
        /// Wird ausgelöst, wenn sich Abspielstatus, Track oder Position geändert haben.
        /// </summary>
        public event EventHandler? StateChanged;

        /// <inheritdoc/>
        public event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// Gibt an, ob gerade Wiedergabe aktiv ist.
        /// </summary>
        public bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

        /// <summary>
        /// Titel des aktuell laufenden Tracks (Dateiname ohne Erweiterung).
        /// Null, wenn nichts spielt.
        /// </summary>
        public string? CurrentTrackTitle { get; private set; }

        /// <summary>
        /// Dateipfad des aktuell laufenden Tracks. Null, wenn nichts spielt.
        /// </summary>
        public string? CurrentTrackPath { get; private set; }

        /// <summary>
        /// Dateipfade der laufenden Wiedergabeliste, in Reihenfolge. Leer, wenn nichts spielt.
        /// </summary>
        /// <remarks>
        /// Die Abspielliste selbst führt nur <c>MediaSource</c>-Objekte; die Pfade daraus
        /// zurückzugewinnen ist umständlich und unsicher. Sie werden deshalb beim Start
        /// mitgeschrieben.
        /// </remarks>
        public IReadOnlyList<string> CurrentTrackPaths
        {
            get { lock (_stateLock) { return _currentTrackPaths; } }
        }

        /// <summary>
        /// Aktuelle Abspielposition.
        /// </summary>
        public TimeSpan Position => _player.PlaybackSession.Position;

        /// <summary>
        /// Gesamtdauer des aktuell laufenden Tracks.
        /// </summary>
        public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;

        /// <summary>
        /// Die Stelle in der ganzen Folge, über alle Spuren gerechnet.
        /// </summary>
        public TimeSpan OverallPosition
        {
            get
            {
                EpisodeTimeline timeline;
                lock (_stateLock) { timeline = _timeline; }

                return timeline.ToOverall(CurrentTrackIndex, _player.PlaybackSession.Position);
            }
        }

        /// <summary>
        /// Die Gesamtdauer der Folge über alle Spuren.
        /// </summary>
        public TimeSpan OverallDuration
        {
            get
            {
                lock (_stateLock) { return _timeline.TotalDuration; }
            }
        }

        /// <summary>
        /// Die laufende Spur, nullbasiert. Ohne laufende Wiedergabe null.
        /// </summary>
        private int CurrentTrackIndex
        {
            get
            {
                uint index = _playlist.CurrentItemIndex;

                // Die Abspielliste meldet zwischen zwei Titeln einen unbelegten Index.
                return index == uint.MaxValue ? 0 : (int)index;
            }
        }

        /// <summary>
        /// Wiedergabegeschwindigkeit. 1.0 entspricht normaler Geschwindigkeit.
        /// Gültige Werte: 0.25 bis 4.0 (Plattformlimit des MediaPlayer).
        /// </summary>
        public double PlaybackRate
        {
            get => _player.PlaybackSession.PlaybackRate;
            set => _player.PlaybackSession.PlaybackRate = value;
        }

        /// <summary>
        /// Lautstärke der Wiedergabe, von 0,0 (still) bis 1,0 (voll).
        /// </summary>
        /// <remarks>
        /// Der Wert wirkt sofort, wird hier aber nicht gespeichert: Ein Regler feuert
        /// während des Ziehens dutzende Male, und jede Änderung in die Datenbank zu
        /// schreiben hieße, den Wiedergabepfad mit Schreibvorgängen zu belegen. Das
        /// Speichern stößt die Oberfläche an, wenn der Nutzer loslässt.
        /// </remarks>
        public double Volume
        {
            get => _player.Volume;
            set
            {
                double desired = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_player.Volume - desired) < 0.0001)
                {
                    return;
                }

                _player.Volume = desired;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Ob die Wiedergabe stummgeschaltet ist.
        /// </summary>
        public bool IsMuted
        {
            get => _player.IsMuted;
            set
            {
                if (_player.IsMuted == value)
                {
                    return;
                }

                _player.IsMuted = value;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Verbleibende Zeit des Einschlaf-Timers.
        /// Null, wenn kein Timer aktiv ist.
        /// </summary>
        public TimeSpan? SleepTimerRemaining => _sleepTimer.Remaining;

        /// <summary>
        /// Startet die Wiedergabe einer Trackliste ab dem angegebenen Index.
        /// Ein laufender Playback wird dabei gestoppt und ersetzt.
        /// </summary>
        /// <param name="episodeId">ID der Episode – für PlaybackState-Persistenz.</param>
        /// <param name="trackPaths">Absolute Dateipfade der Audiotracks, in Reihenfolge.</param>
        /// <param name="startIndex">Index des ersten Tracks (0-basiert).</param>
        /// <param name="resumePosition">
        /// Stelle, ab der fortgesetzt wird. Mit bekannten Spurdauern gilt sie für die ganze
        /// Folge und bestimmt damit auch die Spur; sonst für die übergebene Spur.
        /// </param>
        /// <param name="trackDurations">Die Dauern der Spuren in Abspielreihenfolge, sofern bekannt.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "MediaPlayer.Play-Einstieg: kaputte/fehlende Audio-Dateien, Codec-Fehler oder MediaFoundation-COM-Fehler werden als Nutzer-Fehlermeldung über 'ErrorOccurred' signalisiert, ohne die App zu reißen.")]
        public void Play(
            Guid episodeId,
            IReadOnlyList<string> trackPaths,
            int startIndex = 0,
            TimeSpan resumePosition = default,
            IReadOnlyList<TimeSpan>? trackDurations = null)
        {
            ArgumentNullException.ThrowIfNull(trackPaths);
            _logger.Debug(() => $"Wiedergabe gestartet: EpisodeId={episodeId}, Tracks={trackPaths.Count}, StartIndex={startIndex}");

            EpisodeTimeline timeline = trackDurations is null
                ? EpisodeTimeline.Empty
                : new EpisodeTimeline(trackDurations);

            // Mit bekannten Spurdauern gilt die Fortsetzstelle für die ganze Folge und
            // bestimmt damit auch die Spur. Ohne sie bleibt es beim bisherigen Verhalten:
            // die Stelle gilt für die übergebene Spur.
            TimeSpan positionInTrack = resumePosition;
            if (timeline.IsKnown && resumePosition > TimeSpan.Zero)
            {
                (startIndex, positionInTrack) = timeline.ToTrack(resumePosition);
            }

            try
            {
                lock (_stateLock)
                {
                    _currentEpisodeId = episodeId;
                    _autoSaveTick = 0;
                    _currentTrackPaths = [.. trackPaths];
                    _timeline = timeline;
                }

                _playlist.Items.Clear();

                foreach (string path in trackPaths)
                {
                    // CA2000: MediaSource-Lebensdauer wird von der MediaPlaybackList verwaltet –
                    // Dispose erfolgt beim Entfernen aus der Playlist oder beim Player-Shutdown.
#pragma warning disable CA2000
                    _playlist.Items.Add(
                        new MediaPlaybackItem(
                            Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path))));
#pragma warning restore CA2000
                }

                _ = _playlist.MoveTo((uint)Math.Max(0, startIndex));
                _player.Play();

                if (positionInTrack > TimeSpan.Zero)
                {
                    _player.PlaybackSession.Position = positionInTrack;
                }

                _positionTimer.Start();
            }
            catch (Exception ex) when (ex is UriFormatException or System.IO.FileNotFoundException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.Error("Wiedergabe konnte nicht gestartet werden: {Reason}", ex, ex.Message);
                lock (_stateLock) { ResetPlaybackState(); }
                ErrorOccurred?.Invoke(this, $"Wiedergabe fehlgeschlagen: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error("Unerwarteter Fehler beim Starten der Wiedergabe: {Reason}", ex, ex.Message);
                lock (_stateLock) { ResetPlaybackState(); }
                ErrorOccurred?.Invoke(this, "Ein unerwarteter Fehler ist bei der Wiedergabe aufgetreten.");
            }
        }

        /// <summary>
        /// Pausiert die Wiedergabe und persistiert die aktuelle Position.
        /// </summary>
        public void Pause()
        {
            _player.Pause();
            _ = SavePlaybackStateSnapshotAsync();
        }

        /// <summary>
        /// Stoppt die Wiedergabe vollständig: Position speichern, Timer anhalten,
        /// Titel zurücksetzen. Die eigentlichen Media-Pipeline-Operationen (Pause, Playlist leeren)
        /// laufen auf einem Hintergrund-Thread, weil <c>MediaPlayer.Pause()</c> den UI-Thread
        /// deadlocken kann wenn die Pipeline gleichzeitig UI-Benachrichtigungen sendet.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Media-Pipeline-Stop auf Hintergrund-Thread: MediaPlayer.Pause / Playlist.Items.Clear können bei gleichzeitigen Pipeline-Events native Fehler werfen, die aber den logischen Stop (State-Reset, State-Persistenz) nicht verhindern dürfen.")]
        public void Stop()
        {
            _logger.Info("Wiedergabe gestoppt – Position wird gespeichert.");

            // EpisodeId und Position unter Lock kopieren, dann State zurücksetzen.
            // So kann kein paralleler Timer-Callback mit halbfertigem State arbeiten.
            Guid episodeToSave;
            TimeSpan positionToSave;

            lock (_stateLock)
            {
                episodeToSave = _currentEpisodeId;
                // Gespeichert wird die Stelle in der Folge, nicht die in der Datei — sonst
                // setzt die Wiedergabe später in der falschen Spur wieder ein.
                positionToSave = _timeline.ToOverall(CurrentTrackIndex, _player.PlaybackSession.Position);
                ResetPlaybackState();
            }

            if (episodeToSave != Guid.Empty)
            {
                _ = _stateWriter.SaveAsync(episodeToSave, positionToSave);
            }

            StateChanged?.Invoke(this, EventArgs.Empty);

            // Media-Pipeline auf Hintergrund-Thread stoppen –
            // Pause() und Items.Clear() können den UI-Thread deadlocken
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    _player.Pause();
                    _playlist.Items.Clear();
                }
                catch (Exception ex)
                {
                    // Media-Pipeline-Fehler beim Stoppen sind nicht kritisch,
                    // aber für Diagnose bei subtilen Wiedergabe-Bugs hilfreich.
                    _logger.Warning("Media-Pipeline-Fehler beim Stop: {Reason}", ex.Message);
                }
            });
        }

        /// <summary>
        /// Setzt den internen Wiedergabezustand zurück: Timer anhalten,
        /// Schlaf-Timer löschen, Titel und Episode-ID leeren.
        /// Muss unter <see cref="_stateLock"/> aufgerufen werden.
        /// </summary>
        private void ResetPlaybackState()
        {
            _positionTimer.Stop();
            _sleepTimer.Set(null);
            _autoSaveTick = 0;

            CurrentTrackTitle = null;
            CurrentTrackPath = null;
            _currentEpisodeId = Guid.Empty;
            _currentTrackPaths = [];
            _timeline = EpisodeTimeline.Empty;
        }

        /// <summary>
        /// Setzt eine pausierte Wiedergabe fort.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "MediaPlayer.Play-Wrapper: native/COM-Fehler beim Fortsetzen (Codec/Stream-Probleme) werden als Nutzer-Fehlermeldung über 'ErrorOccurred' signalisiert, ohne die App zu reißen.")]
        public void Resume()
        {
            try
            {
                _player.Play();
            }
            catch (Exception ex)
            {
                _logger.Error("Wiedergabe konnte nicht fortgesetzt werden: {Reason}", ex, ex.Message);
                ErrorOccurred?.Invoke(this, "Wiedergabe konnte nicht fortgesetzt werden.");
            }
        }

        /// <summary>
        /// Springt zum nächsten Track in der Wiedergabeliste.
        /// </summary>
        public void SkipToNext()
        {
            _ = _playlist.MoveNext();
        }

        /// <summary>
        /// Springt zum vorherigen Track in der Wiedergabeliste.
        /// </summary>
        public void SkipToPrevious()
        {
            _ = _playlist.MovePrevious();
        }

        /// <summary>
        /// Springt zu einer bestimmten Position im aktuellen Track.
        /// </summary>
        /// <param name="position">Die Zielposition.</param>
        public void SeekTo(TimeSpan position)
        {
            _player.PlaybackSession.Position = position;
        }

        /// <summary>
        /// Setzt oder deaktiviert den Einschlaf-Timer.
        /// Bei Ablauf wird die Wiedergabe automatisch pausiert.
        /// </summary>
        /// <param name="duration">Zeitspanne bis zum automatischen Stopp. Null deaktiviert den Timer.</param>
        public void SetSleepTimer(TimeSpan? duration)
        {
            _sleepTimer.Set(duration);

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Gibt alle Ressourcen frei und speichert die aktuelle Position asynchron.
        /// Wird vom DI-Container aufgerufen, wenn der Host per <c>DisposeAsync</c> entsorgt wird.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Shutdown-Pfad: DB-/IO-Fehler beim Sichern der letzten Abspielposition (SavePlaybackStateSnapshotAsync) dürfen den Host-Dispose nicht blockieren – die App beendet sich, der Verlust wird lediglich geloggt.")]
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            _positionTimer.Stop();

            try
            {
                await SavePlaybackStateSnapshotAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error("Abspielposition konnte beim App-Ende nicht gespeichert werden.", ex);
            }

            _stateWriter.Dispose();
            _positionTimer.Dispose();
            _player.Dispose();
        }

        /// <summary>
        /// Sync-Fallback für DI-Container und Tests, die ohne Async-Dispose-Pfad arbeiten.
        /// Delegiert auf <see cref="DisposeAsync"/>, damit die Save-Logik nur an einer Stelle gepflegt wird.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            // Blockierend, weil dieser Sync-Fallback nur im Dispose-Pfad läuft: dort gibt es
            // keinen UI-SynchronizationContext, an dem das Warten deadlocken könnte.
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        private void OnPositionTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            bool shouldSave = false;

            lock (_stateLock)
            {
                // Alle dreißig Sekunden sichern: Die Stelle geht auch dann nicht
                // verloren, wenn die Anwendung hart beendet wird.
                _autoSaveTick++;
                if (_autoSaveTick >= AutoSaveIntervalTicks)
                {
                    _autoSaveTick = 0;
                    shouldSave = true;
                }
            }

            // Der Einschlaf-Zeitgeber hängt am selben Takt, gehört aber nicht unter
            // die Sperre des Wiedergabezustands.
            bool shouldPause = _sleepTimer.Tick(TimeSpan.FromMilliseconds(500));

            if (shouldSave)
            {
                _ = SavePlaybackStateSnapshotAsync();
            }

            if (shouldPause)
            {
                Pause();
                return;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPlaybackStateChanged(MediaPlaybackSession session, object args)
        {
            if (!IsPlaying)
            {
                _positionTimer.Stop();
            }
            else
            {
                _positionTimer.Start();
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnCurrentItemChanged(MediaPlaybackList sender, CurrentMediaPlaybackItemChangedEventArgs args)
        {
            if (args.NewItem?.Source?.Uri is Uri uri)
            {
                CurrentTrackTitle = System.IO.Path.GetFileNameWithoutExtension(uri.LocalPath);
                CurrentTrackPath = uri.LocalPath;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Behandelt Codec-Fehler, korrupte Dateien und I/O-Probleme der Media-Pipeline.
        /// </summary>
        /// <param name="sender">Der auslösende <see cref="MediaPlayer"/>.</param>
        /// <param name="args">Fehlerdetails der Media-Pipeline, u. a. die Fehlermeldung.</param>
        private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            string message = args.ErrorMessage ?? "Unbekannter Wiedergabefehler";
            _logger.Error("MediaPlayer-Fehler: {Message} (Error: {Error})", null, message, args.Error);
            ErrorOccurred?.Invoke(this, $"Wiedergabefehler: {message}");
        }

        /// <summary>
        /// Erstellt einen thread-sicheren Snapshot des aktuellen Zustands und speichert ihn.
        /// Werte werden unter Lock kopiert, die DB-Persistierung erfolgt außerhalb.
        /// </summary>
        private async System.Threading.Tasks.Task SavePlaybackStateSnapshotAsync()
        {
            Guid episodeId;
            TimeSpan position;

            lock (_stateLock)
            {
                episodeId = _currentEpisodeId;
                position = _timeline.ToOverall(CurrentTrackIndex, _player.PlaybackSession.Position);
            }

            await _stateWriter.SaveAsync(episodeId, position);
        }
    }
}

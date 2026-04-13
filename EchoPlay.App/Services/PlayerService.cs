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
    public sealed class PlayerService : IPlayerService, IDisposable
    {
        // Auto-Save alle 30 Sekunden während aktiver Wiedergabe (500 ms × 60 Ticks)
        private const int AutoSaveIntervalTicks = 60;

        private readonly MediaPlayer _player;
        private readonly MediaPlaybackList _playlist;
        private readonly System.Timers.Timer _positionTimer;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private readonly IClock _clock;

        // Synchronisierung: _stateLock schützt alle mutable Felder (synchron),
        // _saveLock serialisiert die async DB-Persistierung.
        private readonly object _stateLock = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        private Guid _currentEpisodeId;
        private TimeSpan? _sleepTimerRemaining;
        private int _autoSaveTick;

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
        /// Aktuelle Abspielposition.
        /// </summary>
        public TimeSpan Position => _player.PlaybackSession.Position;

        /// <summary>
        /// Gesamtdauer des aktuell laufenden Tracks.
        /// </summary>
        public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;

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
        /// Verbleibende Zeit des Einschlaf-Timers.
        /// Null, wenn kein Timer aktiv ist.
        /// </summary>
        public TimeSpan? SleepTimerRemaining => _sleepTimerRemaining;

        /// <summary>
        /// Startet die Wiedergabe einer Trackliste ab dem angegebenen Index.
        /// Ein laufender Playback wird dabei gestoppt und ersetzt.
        /// </summary>
        /// <param name="episodeId">ID der Episode – für PlaybackState-Persistenz.</param>
        /// <param name="trackPaths">Absolute Dateipfade der Audiotracks, in Reihenfolge.</param>
        /// <param name="startIndex">Index des ersten Tracks (0-basiert).</param>
        /// <param name="resumePosition">Position, ab der fortgesetzt werden soll.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "MediaPlayer.Play-Einstieg: kaputte/fehlende Audio-Dateien, Codec-Fehler oder MediaFoundation-COM-Fehler werden als Nutzer-Fehlermeldung ueber 'ErrorOccurred' signalisiert, ohne die App zu reissen.")]
        public void Play(Guid episodeId, IReadOnlyList<string> trackPaths, int startIndex = 0, TimeSpan resumePosition = default)
        {
            ArgumentNullException.ThrowIfNull(trackPaths);
            _logger.Debug($"Wiedergabe gestartet: EpisodeId={episodeId}, Tracks={trackPaths.Count}, StartIndex={startIndex}");

            try
            {
                lock (_stateLock)
                {
                    _currentEpisodeId = episodeId;
                    _autoSaveTick = 0;
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

                _ = _playlist.MoveTo((uint)startIndex);
                _player.Play();

                if (resumePosition > TimeSpan.Zero)
                {
                    _player.PlaybackSession.Position = resumePosition;
                }

                _positionTimer.Start();
            }
            catch (Exception ex) when (ex is UriFormatException or System.IO.FileNotFoundException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.Error($"Wiedergabe konnte nicht gestartet werden: {ex.Message}", ex);
                lock (_stateLock) { ResetPlaybackState(); }
                ErrorOccurred?.Invoke(this, $"Wiedergabe fehlgeschlagen: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Unerwarteter Fehler beim Starten der Wiedergabe: {ex.Message}", ex);
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Media-Pipeline-Stop auf Hintergrund-Thread: MediaPlayer.Pause / Playlist.Items.Clear koennen bei gleichzeitigen Pipeline-Events native Fehler werfen, die aber den logischen Stop (State-Reset, State-Persistenz) nicht verhindern duerfen.")]
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
                positionToSave = _player.PlaybackSession.Position;
                ResetPlaybackState();
            }

            if (episodeToSave != Guid.Empty)
            {
                _ = SavePlaybackStateForEpisodeAsync(episodeToSave, positionToSave);
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
                    _logger.Warning($"Media-Pipeline-Fehler beim Stop: {ex.Message}");
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
            _sleepTimerRemaining = null;
            _autoSaveTick = 0;

            CurrentTrackTitle = null;
            _currentEpisodeId = Guid.Empty;
        }

        /// <summary>
        /// Setzt eine pausierte Wiedergabe fort.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "MediaPlayer.Play-Wrapper: native/COM-Fehler beim Fortsetzen (Codec/Stream-Probleme) werden als Nutzer-Fehlermeldung ueber 'ErrorOccurred' signalisiert, ohne die App zu reissen.")]
        public void Resume()
        {
            try
            {
                _player.Play();
            }
            catch (Exception ex)
            {
                _logger.Error($"Wiedergabe konnte nicht fortgesetzt werden: {ex.Message}", ex);
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
            lock (_stateLock)
            {
                _sleepTimerRemaining = duration;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Gibt alle Ressourcen frei und speichert die aktuelle Position.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Shutdown-Pfad: DB-/IO-Fehler beim Sichern der letzten Abspielposition (SavePlaybackStateSnapshotAsync) duerfen den Host-Dispose nicht blockieren – die App beendet sich, der Verlust wird lediglich geloggt.")]
        public void Dispose()
        {
            _positionTimer.Stop();

            // Position beim App-Beenden sichern, damit der Nutzer beim nächsten Start weiterhören kann.
            // Task.Run vermeidet Deadlock falls Dispose() vom UI-Thread aufgerufen wird.
            try
            {
                System.Threading.Tasks.Task.Run(() => SavePlaybackStateSnapshotAsync()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // App beendet sich sowieso – aber der Verlust der Position soll nachvollziehbar sein.
                _logger.Error("Abspielposition konnte beim App-Ende nicht gespeichert werden.", ex);
            }

            _saveLock.Dispose();
            _positionTimer.Dispose();
            _player.Dispose();
        }

        private void OnPositionTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            bool shouldSave = false;
            bool shouldPause = false;

            lock (_stateLock)
            {
                // Auto-Save alle 30 Sekunden: Position geht auch bei Force-Close nicht verloren
                _autoSaveTick++;
                if (_autoSaveTick >= AutoSaveIntervalTicks)
                {
                    _autoSaveTick = 0;
                    shouldSave = true;
                }

                // Sleep-Timer runterzählen; bei Ablauf Wiedergabe anhalten
                if (_sleepTimerRemaining.HasValue)
                {
                    _sleepTimerRemaining = _sleepTimerRemaining.Value - TimeSpan.FromMilliseconds(500);
                    if (_sleepTimerRemaining.Value <= TimeSpan.Zero)
                    {
                        _sleepTimerRemaining = null;
                        shouldPause = true;
                    }
                }
            }

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
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Behandelt Codec-Fehler, korrupte Dateien und I/O-Probleme der Media-Pipeline.
        /// </summary>
        private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            string message = args.ErrorMessage ?? "Unbekannter Wiedergabefehler";
            _logger.Error($"MediaPlayer-Fehler: {message} (Error: {args.Error})");
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
                position = _player.PlaybackSession.Position;
            }

            await SavePlaybackStateForEpisodeAsync(episodeId, position);
        }

        /// <summary>
        /// Speichert die Position für eine bestimmte Episode in der Datenbank.
        /// Wird von <see cref="SavePlaybackStateSnapshotAsync"/> und <see cref="Stop"/> verwendet.
        /// <see cref="_saveLock"/> stellt sicher, dass nie zwei Saves gleichzeitig laufen.
        /// </summary>
        /// <param name="episodeId">Die Episode, für die gespeichert wird.</param>
        /// <param name="position">Die aktuelle Abspielposition.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Persistenz der Abspielposition im Hintergrund: DbContext-/Concurrency-/Migration-Fehler duerfen die Wiedergabe nicht stoeren – bei Scheitern wird der Verlust geloggt und der naechste Autosave-Tick versucht es erneut.")]
        private async System.Threading.Tasks.Task SavePlaybackStateForEpisodeAsync(Guid episodeId, TimeSpan position)
        {
            if (episodeId == Guid.Empty)
            {
                return;
            }

            // Bereits ein Save aktiv → diesen Durchlauf überspringen
            if (!await _saveLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IPlaybackStateDataService service = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

                PlaybackState? existing = await service.GetByEpisodeIdAsync(episodeId);

                if (existing is null)
                {
                    PlaybackState newState = new()
                    {
                        EpisodeId = episodeId,
                        LastPosition = position,
                        LastPlayedAt = _clock.UtcNow
                    };

                    await service.AddAsync(newState);
                }
                else
                {
                    existing.LastPosition = position;
                    existing.LastPlayedAt = _clock.UtcNow;
                    await service.UpdateAsync(existing);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Wiedergabestatus konnte nicht gespeichert werden: {ex.Message}");
            }
            finally
            {
                _ = _saveLock.Release();
            }
        }
    }
}

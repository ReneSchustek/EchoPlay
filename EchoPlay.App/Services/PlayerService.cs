using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
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

        private Guid _currentEpisodeId;
        private TimeSpan? _sleepTimerRemaining;
        private int _autoSaveTick;

        /// <summary>
        /// Initialisiert den PlayerService und konfiguriert die Wiedergabeliste.
        /// </summary>
        /// <param name="scopeFactory">Fabrik für DI-Scopes (für PlaybackState-Persistenz).</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        public PlayerService(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
        {
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("PlayerService");

            _player = new();
            _playlist = new();
            _player.Source = _playlist;

            // 500 ms-Takt für Positionsanzeige, Auto-Save und Sleep-Timer-Countdown
            _positionTimer = new(500);
            _positionTimer.Elapsed += OnPositionTimerElapsed;

            _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            _playlist.CurrentItemChanged += OnCurrentItemChanged;
        }

        /// <summary>
        /// Wird ausgelöst, wenn sich Abspielstatus, Track oder Position geändert haben.
        /// </summary>
        public event EventHandler? StateChanged;

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
        public void Play(Guid episodeId, IReadOnlyList<string> trackPaths, int startIndex = 0, TimeSpan resumePosition = default)
        {
            _logger.Debug($"Wiedergabe gestartet: EpisodeId={episodeId}, Tracks={trackPaths.Count}, StartIndex={startIndex}");
            _currentEpisodeId = episodeId;
            _autoSaveTick = 0;

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

            _playlist.MoveTo((uint)startIndex);
            _player.Play();

            if (resumePosition > TimeSpan.Zero)
            {
                _player.PlaybackSession.Position = resumePosition;
            }

            _positionTimer.Start();
        }

        /// <summary>
        /// Pausiert die Wiedergabe und persistiert die aktuelle Position.
        /// </summary>
        public void Pause()
        {
            _player.Pause();
            _ = SavePlaybackStateAsync();
        }

        /// <summary>
        /// Stoppt die Wiedergabe vollständig: Position speichern, Timer anhalten,
        /// Titel zurücksetzen. Die eigentlichen Media-Pipeline-Operationen (Pause, Playlist leeren)
        /// laufen auf einem Hintergrund-Thread, weil <c>MediaPlayer.Pause()</c> den UI-Thread
        /// deadlocken kann wenn die Pipeline gleichzeitig UI-Benachrichtigungen sendet.
        /// </summary>
        public void Stop()
        {
            _logger.Info("Wiedergabe gestoppt – Position wird gespeichert.");

            // Position sichern, bevor die EpisodeId gelöscht wird
            _ = SavePlaybackStateAsync();

            ResetPlaybackState();

            StateChanged?.Invoke(this, EventArgs.Empty);

            // Media-Pipeline auf Hintergrund-Thread stoppen –
            // Pause() und Items.Clear() können den UI-Thread deadlocken
            System.Threading.Tasks.Task.Run(() =>
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
        /// Wird von <see cref="Stop"/> aufgerufen, nachdem die Position gespeichert wurde.
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
        public void Resume()
        {
            _player.Play();
        }

        /// <summary>
        /// Springt zum nächsten Track in der Wiedergabeliste.
        /// </summary>
        public void SkipToNext()
        {
            _playlist.MoveNext();
        }

        /// <summary>
        /// Springt zum vorherigen Track in der Wiedergabeliste.
        /// </summary>
        public void SkipToPrevious()
        {
            _playlist.MovePrevious();
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
            _sleepTimerRemaining = duration;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Gibt alle Ressourcen frei und speichert die aktuelle Position.
        /// </summary>
        public void Dispose()
        {
            _positionTimer.Stop();

            // Position beim App-Beenden sichern, damit der Nutzer beim nächsten Start weiterhören kann.
            // Task.Run vermeidet Deadlock falls Dispose() vom UI-Thread aufgerufen wird.
            try
            {
                System.Threading.Tasks.Task.Run(SavePlaybackStateAsync).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // App beendet sich sowieso – aber der Verlust der Position soll nachvollziehbar sein.
                _logger.Error("Abspielposition konnte beim App-Ende nicht gespeichert werden.", ex);
            }

            _positionTimer.Dispose();
            _player.Dispose();
        }

        private void OnPositionTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            // Auto-Save alle 30 Sekunden: Position geht auch bei Force-Close nicht verloren
            _autoSaveTick++;
            if (_autoSaveTick >= AutoSaveIntervalTicks)
            {
                _autoSaveTick = 0;
                _ = SavePlaybackStateAsync();
            }

            // Sleep-Timer runterzählen; bei Ablauf Wiedergabe anhalten
            if (_sleepTimerRemaining.HasValue)
            {
                _sleepTimerRemaining = _sleepTimerRemaining.Value - TimeSpan.FromMilliseconds(500);
                if (_sleepTimerRemaining.Value <= TimeSpan.Zero)
                {
                    _sleepTimerRemaining = null;
                    Pause();
                    // StateChanged wird über OnPlaybackStateChanged gefeuert
                    return;
                }
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
        /// Speichert die aktuelle Position als PlaybackState in der Datenbank.
        /// Verwendet einen eigenen Scope, da PlayerService ein Singleton ist.
        /// </summary>
        private async System.Threading.Tasks.Task SavePlaybackStateAsync()
        {
            if (_currentEpisodeId == Guid.Empty)
            {
                return;
            }

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IPlaybackStateDataService service = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

                PlaybackState? existing = await service.GetByEpisodeIdAsync(_currentEpisodeId);
                TimeSpan currentPosition = Position;

                if (existing is null)
                {
                    PlaybackState newState = new()
                    {
                        EpisodeId = _currentEpisodeId,
                        LastPosition = currentPosition,
                        LastPlayedAt = DateTime.UtcNow
                    };

                    await service.AddAsync(newState);
                }
                else
                {
                    existing.LastPosition = currentPosition;
                    existing.LastPlayedAt = DateTime.UtcNow;
                    await service.UpdateAsync(existing);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Wiedergabestatus konnte nicht gespeichert werden: {ex.Message}");
            }
        }
    }
}

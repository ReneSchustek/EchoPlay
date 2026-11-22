using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using EchoPlay.App.Helpers;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Singleton-Service für die Audiowiedergabe.
    /// Kapselt <see cref="MediaPlayer"/> und stellt eine stabile, ereignisbasierte API für den
    /// MiniPlayer und die Episodenliste bereit. Die Abspielliste führt <see cref="PlaybackQueue"/>,
    /// den Takt für Anzeige, Sicherung und Einschlaf-Timer <see cref="PlaybackTicker"/>, die
    /// laufende Folge samt Hörstand <see cref="PlayingEpisode"/>.
    /// </summary>
    public sealed class PlayerService : IPlayerService, IAsyncDisposable, IDisposable
    {
        private readonly MediaPlayer _player;
        private readonly PlaybackQueue _queue = new();
        private readonly PlaybackTicker _ticker = new();
        private readonly PlayingEpisode _episode = new();
        private readonly PlaybackStateWriter _stateWriter;
        private readonly ILogger _logger;
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
            _logger = loggerFactory.CreateLogger("PlayerService");
            _stateWriter = new PlaybackStateWriter(scopeFactory, _logger, clock);

            _player = new() { Source = _queue.List };

            _ticker.SaveDue += (_, _) => _ = SavePlaybackStateSnapshotAsync();
            _ticker.SleepTimerElapsed += (_, _) => Pause();
            _ticker.Ticked += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);

            _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            _queue.List.CurrentItemChanged += OnCurrentItemChanged;
            _player.MediaFailed += OnMediaFailed;
        }

        /// <inheritdoc/>
        public event EventHandler? StateChanged;

        /// <inheritdoc/>
        public event EventHandler<string>? ErrorOccurred;

        /// <inheritdoc/>
        public bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

        /// <inheritdoc/>
        public string? CurrentTrackTitle { get; private set; }

        /// <inheritdoc/>
        public string? CurrentTrackPath { get; private set; }

        /// <inheritdoc/>
        /// <remarks>
        /// Die Abspielliste selbst führt nur <c>MediaSource</c>-Objekte; die Pfade daraus
        /// zurückzugewinnen ist umständlich und unsicher. Sie werden deshalb beim Start
        /// mitgeschrieben.
        /// </remarks>
        public IReadOnlyList<string> CurrentTrackPaths => _episode.TrackPaths;

        /// <summary>
        /// Der zuletzt angestoßene Hintergrundschritt von <see cref="Stop"/>. Das Beenden wartet
        /// darauf, damit keine Quelle nach dem Freigeben des Players noch angefasst wird.
        /// </summary>
        internal Task PipelineStopTask { get; private set; } = Task.CompletedTask;

        /// <summary>Die Quellen der laufenden Abspielliste; Einblick für Tests der Freigabe.</summary>
        internal IReadOnlyList<MediaSource> PlaylistSources => _queue.Sources;

        /// <summary>Anzahl der Einträge in der Abspielliste; Einblick für Tests.</summary>
        internal int PlaylistItemCount => _queue.Count;

        /// <inheritdoc/>
        public TimeSpan Position => _player.PlaybackSession.Position;

        /// <inheritdoc/>
        public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;

        /// <inheritdoc/>
        public TimeSpan OverallPosition => _episode.ToOverall(_queue.CurrentIndex, _player.PlaybackSession.Position);

        /// <inheritdoc/>
        public TimeSpan OverallDuration => _episode.TotalDuration;

        /// <inheritdoc/>
        public double PlaybackRate
        {
            get => _player.PlaybackSession.PlaybackRate;
            set => _player.PlaybackSession.PlaybackRate = value;
        }

        /// <inheritdoc/>
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

                // Gleitkommawerte aus dem Regler treffen den gespeicherten Wert selten genau; ohne
                // Toleranz löste derselbe Wert erneut ein Änderungsereignis aus.
                if (Math.Abs(_player.Volume - desired) < 0.0001)
                {
                    return;
                }

                _player.Volume = desired;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public TimeSpan? SleepTimerRemaining => _ticker.SleepTimerRemaining;

        /// <inheritdoc/>
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
            // bestimmt damit auch die Spur. Ohne sie gilt die Stelle für die übergebene Spur.
            TimeSpan positionInTrack = resumePosition;
            if (timeline.IsKnown && resumePosition > TimeSpan.Zero)
            {
                (startIndex, positionInTrack) = timeline.ToTrack(resumePosition);
            }

            try
            {
                StartPlayback(episodeId, trackPaths, timeline, startIndex, positionInTrack);
            }
            catch (Exception ex) when (ex is UriFormatException or System.IO.FileNotFoundException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.Error("Wiedergabe konnte nicht gestartet werden: {Reason}", ex, ex.Message);
                ResetPlaybackState();
                ErrorOccurred?.Invoke(this, FormatError("PlayerErrorStartFailed", "Wiedergabe fehlgeschlagen: {0}", ex.Message));
            }
            catch (Exception ex)
            {
                _logger.Error("Unerwarteter Fehler beim Starten der Wiedergabe: {Reason}", ex, ex.Message);
                ResetPlaybackState();
                ErrorOccurred?.Invoke(this, SafeResourceLoader.Get("PlayerErrorUnexpected", "Ein unerwarteter Fehler ist bei der Wiedergabe aufgetreten."));
            }
        }

        private void StartPlayback(
            Guid episodeId,
            IReadOnlyList<string> trackPaths,
            EpisodeTimeline timeline,
            int startIndex,
            TimeSpan positionInTrack)
        {
            _episode.Start(episodeId, trackPaths, timeline);
            _ticker.RestartAutoSave();
            _queue.Load(trackPaths);
            _queue.MoveTo(startIndex);
            _player.Play();

            if (positionInTrack > TimeSpan.Zero)
            {
                _player.PlaybackSession.Position = positionInTrack;
            }

            _ticker.Start();
        }

        /// <inheritdoc/>
        public void Pause()
        {
            _player.Pause();
            _ = SavePlaybackStateSnapshotAsync();
        }

        /// <summary>
        /// Stoppt die Wiedergabe vollständig: Position speichern, Takt anhalten,
        /// Titel zurücksetzen. Die eigentlichen Media-Pipeline-Operationen (Pause, Playlist leeren)
        /// laufen auf einem Hintergrund-Thread, weil <c>MediaPlayer.Pause()</c> den UI-Thread
        /// deadlocken kann, wenn die Pipeline gleichzeitig UI-Benachrichtigungen sendet.
        /// </summary>
        public void Stop()
        {
            _logger.Info("Wiedergabe gestoppt – Position wird gespeichert.");

            (Guid episodeToSave, TimeSpan positionToSave) = TakeSnapshot();
            ResetPlaybackState();

            if (episodeToSave != Guid.Empty)
            {
                _ = _stateWriter.SaveAsync(episodeToSave, positionToSave);
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
            PipelineStopTask = _queue.ClearInBackground(_player.Pause, _logger);
        }

        /// <inheritdoc/>
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
                ErrorOccurred?.Invoke(this, SafeResourceLoader.Get("PlayerErrorResumeFailed", "Wiedergabe konnte nicht fortgesetzt werden."));
            }
        }

        /// <inheritdoc/>
        public void SkipToNext() => MoveWithinPlaylist(_queue.MoveNext);

        /// <inheritdoc/>
        public void SkipToPrevious() => MoveWithinPlaylist(_queue.MovePrevious);

        /// <inheritdoc/>
        public void SeekTo(TimeSpan position) => _player.PlaybackSession.Position = position;

        /// <inheritdoc/>
        public void SetSleepTimer(TimeSpan? duration)
        {
            _ticker.SetSleepTimer(duration);
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

            _ticker.Stop();

            try
            {
                await SavePlaybackStateSnapshotAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error("Abspielposition konnte beim App-Ende nicht gespeichert werden.", ex);
            }

            // Der Hintergrundschritt fängt seine Fehler selbst; hier geht es nur darum, dass er
            // fertig ist, bevor Player und Quellen verschwinden.
            await PipelineStopTask.ConfigureAwait(false);

            _stateWriter.Dispose();
            _ticker.Dispose();
            _player.Dispose();
            _queue.Dispose();
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

        /// <summary>
        /// Setzt den Wiedergabezustand zurück: Takt anhalten, Einschlaf-Timer löschen,
        /// Titel und Episode-ID leeren.
        /// </summary>
        private void ResetPlaybackState()
        {
            _ticker.Reset();
            _episode.Clear();
            CurrentTrackTitle = null;
            CurrentTrackPath = null;
        }

        /// <summary>
        /// Wechselt die Spur. Konnte die Pipeline eine Spur nicht öffnen, weist die Abspielliste
        /// den Wechsel mit einem COM-Fehler ab; die defekte Spur hat <see cref="OnMediaFailed"/>
        /// dann schon gemeldet, deshalb bleibt es hier beim Protokoll.
        /// </summary>
        /// <param name="move">Der Sprung in der Abspielliste.</param>
        private void MoveWithinPlaylist(Action move)
        {
            try
            {
                move();
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                _logger.Warning("Spurwechsel von der Abspielliste abgelehnt: {Reason}", ex.Message);
            }
        }

        /// <summary>Kennung der laufenden Folge und Stelle darin, in einem Zug gelesen.</summary>
        private (Guid Id, TimeSpan Position) TakeSnapshot() =>
            _episode.Snapshot(_queue.CurrentIndex, _player.PlaybackSession.Position);

        private void OnPlaybackStateChanged(MediaPlaybackSession session, object args)
        {
            if (IsPlaying)
            {
                _ticker.Start();
            }
            else
            {
                _ticker.Stop();
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
            string message = args.ErrorMessage ?? SafeResourceLoader.Get("PlayerErrorUnknown", "Unbekannter Wiedergabefehler");
            _logger.Error("MediaPlayer-Fehler: {Message} (Error: {Error})", null, message, args.Error);
            ErrorOccurred?.Invoke(this, FormatError("PlayerErrorMediaFailed", "Wiedergabefehler: {0}", message));
        }

        /// <summary>
        /// Setzt eine Fehlermeldung aus der Vorlage der Ressourcen zusammen. Der Miniplayer zeigt
        /// sie unverändert an, deshalb kommt sie in der Sprache der Oberfläche.
        /// </summary>
        /// <param name="key">Schlüssel der Vorlage mit einem Platzhalter.</param>
        /// <param name="fallback">Vorlage, wenn die Ressourcen fehlen (Testprozess).</param>
        /// <param name="detail">Die Meldung des Systems für den Platzhalter.</param>
        private static string FormatError(string key, string fallback, string detail)
        {
            string format = SafeResourceLoader.Get(key, fallback);
            return string.Format(CultureInfo.CurrentCulture, format, detail);
        }

        /// <summary>Speichert den Hörstand der laufenden Folge.</summary>
        private Task SavePlaybackStateSnapshotAsync()
        {
            (Guid episodeId, TimeSpan position) = TakeSnapshot();
            return _stateWriter.SaveAsync(episodeId, position);
        }
    }
}

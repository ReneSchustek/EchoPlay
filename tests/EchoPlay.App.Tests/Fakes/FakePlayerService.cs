using EchoPlay.App.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IPlayerService"/>.
    /// Zeichnet Methodenaufrufe auf und ermöglicht das manuelle Auslösen von <see cref="StateChanged"/>.
    /// </summary>
    internal sealed class FakePlayerService : IPlayerService
    {
        /// <inheritdoc/>
        public event EventHandler? StateChanged;

        /// <inheritdoc/>
        public event EventHandler<string>? ErrorOccurred;

        /// <inheritdoc/>
        public bool IsPlaying { get; private set; }

        /// <inheritdoc/>
        public string? CurrentTrackTitle { get; private set; }

        /// <inheritdoc/>
        public string? CurrentTrackPath { get; private set; }

        /// <inheritdoc/>
        public IReadOnlyList<string> CurrentTrackPaths { get; private set; } = [];

        /// <inheritdoc/>
        public TimeSpan Position { get; private set; }

        /// <inheritdoc/>
        public TimeSpan Duration { get; private set; }

        /// <summary>Die Stelle in der ganzen Folge.</summary>
        public TimeSpan OverallPosition { get; private set; }

        /// <summary>Die Gesamtdauer der Folge über alle Spuren.</summary>
        public TimeSpan OverallDuration { get; private set; }

        /// <summary>Lautstärke von 0,0 bis 1,0.</summary>
        public double Volume { get; set; } = 1.0;

        /// <summary>Ob stummgeschaltet ist.</summary>
        public bool IsMuted { get; set; }

        /// <inheritdoc/>
        public double PlaybackRate { get; set; } = 1.0;

        /// <inheritdoc/>
        public TimeSpan? SleepTimerRemaining { get; private set; }

        /// <summary>Zuletzt übergebene Dauer aus <see cref="SetSleepTimer"/>.</summary>
        public TimeSpan? LastSetSleepTimerArg { get; private set; }

        /// <summary>Gibt an, ob <see cref="SetSleepTimer"/> mindestens einmal aufgerufen wurde.</summary>
        public bool SetSleepTimerWasCalled { get; private set; }

        /// <summary>Aufgezeichnete Play-Aufrufe.</summary>
        public List<(Guid EpisodeId, IReadOnlyList<string> TrackPaths, int StartIndex, TimeSpan ResumePosition, IReadOnlyList<TimeSpan>? TrackDurations)> PlayCalls { get; } = [];

        /// <summary>Gibt an, ob <see cref="Pause"/> aufgerufen wurde.</summary>
        public bool PauseWasCalled { get; private set; }

        /// <summary>Gibt an, ob <see cref="Stop"/> aufgerufen wurde.</summary>
        public bool StopWasCalled { get; private set; }

        /// <summary>Gibt an, ob <see cref="Resume"/> aufgerufen wurde.</summary>
        public bool ResumeWasCalled { get; private set; }

        /// <summary>Zuletzt übergebene Position aus <see cref="SeekTo"/>.</summary>
        public TimeSpan? SeekToArg { get; private set; }

        /// <summary>
        /// Simuliert einen Wiedergabefehler und feuert <see cref="ErrorOccurred"/>.
        /// </summary>
        /// <param name="message">Die Fehlermeldung für den Nutzer.</param>
        public void SimulateError(string message)
        {
            ErrorOccurred?.Invoke(this, message);
        }

        /// <summary>
        /// Setzt den internen Zustand und feuert <see cref="StateChanged"/>.
        /// Ermöglicht Tests, auf Zustandsänderungen des ViewModel zu reagieren.
        /// </summary>
        public void SetState(string? trackTitle, bool isPlaying, double positionSeconds, double durationSeconds)
        {
            CurrentTrackTitle = trackTitle;
            IsPlaying = isPlaying;
            Position = TimeSpan.FromSeconds(positionSeconds);
            Duration = TimeSpan.FromSeconds(durationSeconds);
            OverallPosition = Position;
            OverallDuration = Duration;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        public void Play(
            Guid episodeId,
            IReadOnlyList<string> trackPaths,
            int startIndex = 0,
            TimeSpan resumePosition = default,
            IReadOnlyList<TimeSpan>? trackDurations = null)
        {
            PlayCalls.Add((episodeId, trackPaths, startIndex, resumePosition, trackDurations));
            CurrentTrackPaths = [.. trackPaths];
        }

        /// <summary>
        /// Simuliert eine Wiedergabe, die woanders gestartet wurde: Der Dienst führt eine
        /// Liste, ohne dass die Player-Seite daran beteiligt war.
        /// </summary>
        /// <param name="trackPaths">Dateipfade der laufenden Liste.</param>
        /// <param name="currentPath">Dateipfad der laufenden Spur.</param>
        public void SimulateExternalPlayback(IReadOnlyList<string> trackPaths, string? currentPath)
        {
            CurrentTrackPaths = [.. trackPaths];
            CurrentTrackPath = currentPath;
            CurrentTrackTitle = currentPath is null ? null : Path.GetFileNameWithoutExtension(currentPath);
            IsPlaying = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        public void Pause()
        {
            PauseWasCalled = true;
        }

        /// <inheritdoc/>
        public void Stop()
        {
            StopWasCalled = true;
            CurrentTrackTitle = null;
            CurrentTrackPath = null;
            CurrentTrackPaths = [];
            IsPlaying = false;

            // Der echte Dienst verwirft beim Stoppen den gesamten Wiedergabestand
            // (ResetPlaybackState). Bliebe die Position hier stehen, zeigte die Zeitanzeige
            // im Test eine Stelle in einer Datei, die gar nicht mehr läuft.
            Position = TimeSpan.Zero;
            Duration = TimeSpan.Zero;
            OverallPosition = TimeSpan.Zero;
            OverallDuration = TimeSpan.Zero;

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        public void Resume()
        {
            ResumeWasCalled = true;
        }

        /// <summary>Anzahl der Sprünge zur nächsten Spur.</summary>
        public int SkipToNextCallCount { get; private set; }

        /// <summary>Anzahl der Sprünge zur vorherigen Spur.</summary>
        public int SkipToPreviousCallCount { get; private set; }

        /// <inheritdoc/>
        public void SkipToNext() => SkipToNextCallCount++;

        /// <inheritdoc/>
        public void SkipToPrevious() => SkipToPreviousCallCount++;

        /// <inheritdoc/>
        public void SeekTo(TimeSpan position)
        {
            SeekToArg = position;
        }

        /// <inheritdoc/>
        public void SetSleepTimer(TimeSpan? duration)
        {
            SleepTimerRemaining = duration;
            LastSetSleepTimerArg = duration;
            SetSleepTimerWasCalled = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

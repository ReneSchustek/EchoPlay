using EchoPlay.App.Services;
using System;
using System.Collections.Generic;

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
        public bool IsPlaying { get; private set; }

        /// <inheritdoc/>
        public string? CurrentTrackTitle { get; private set; }

        /// <inheritdoc/>
        public TimeSpan Position { get; private set; }

        /// <inheritdoc/>
        public TimeSpan Duration { get; private set; }

        /// <inheritdoc/>
        public double PlaybackRate { get; set; } = 1.0;

        /// <inheritdoc/>
        public TimeSpan? SleepTimerRemaining { get; private set; }

        /// <summary>Zuletzt übergebene Dauer aus <see cref="SetSleepTimer"/>.</summary>
        public TimeSpan? LastSetSleepTimerArg { get; private set; }

        /// <summary>Gibt an, ob <see cref="SetSleepTimer"/> mindestens einmal aufgerufen wurde.</summary>
        public bool SetSleepTimerWasCalled { get; private set; }

        /// <summary>Aufgezeichnete Play-Aufrufe.</summary>
        public List<(Guid EpisodeId, IReadOnlyList<string> TrackPaths, int StartIndex, TimeSpan ResumePosition)> PlayCalls { get; } = [];

        /// <summary>Gibt an, ob <see cref="Pause"/> aufgerufen wurde.</summary>
        public bool PauseWasCalled { get; private set; }

        /// <summary>Gibt an, ob <see cref="Stop"/> aufgerufen wurde.</summary>
        public bool StopWasCalled { get; private set; }

        /// <summary>Gibt an, ob <see cref="Resume"/> aufgerufen wurde.</summary>
        public bool ResumeWasCalled { get; private set; }

        /// <summary>Zuletzt übergebene Position aus <see cref="SeekTo"/>.</summary>
        public TimeSpan? SeekToArg { get; private set; }

        /// <summary>
        /// Setzt den internen Zustand und feuert <see cref="StateChanged"/>.
        /// Ermöglicht Tests, auf Zustandsänderungen des ViewModel zu reagieren.
        /// </summary>
        public void SetState(string? trackTitle, bool isPlaying, double positionSeconds, double durationSeconds)
        {
            CurrentTrackTitle = trackTitle;
            IsPlaying         = isPlaying;
            Position          = TimeSpan.FromSeconds(positionSeconds);
            Duration          = TimeSpan.FromSeconds(durationSeconds);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        public void Play(Guid episodeId, IReadOnlyList<string> trackPaths, int startIndex = 0, TimeSpan resumePosition = default)
        {
            PlayCalls.Add((episodeId, trackPaths, startIndex, resumePosition));
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
            IsPlaying = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        public void Resume()
        {
            ResumeWasCalled = true;
        }

        /// <inheritdoc/>
        public void SkipToNext() { }

        /// <inheritdoc/>
        public void SkipToPrevious() { }

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

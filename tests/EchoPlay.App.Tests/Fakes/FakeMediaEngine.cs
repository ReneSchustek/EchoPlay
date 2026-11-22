using EchoPlay.App.Services;
using Windows.Media.Playback;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Wiedergabe-Engine ohne Betriebssystem: hält Zustand, Stelle und Lautstärke im Speicher und
    /// meldet Wechsel wie die echte. Damit prüfen die Tests den Dienst auch auf einem Rechner, der
    /// nicht abspielen kann.
    /// </summary>
    internal sealed class FakeMediaEngine : IMediaEngine
    {
        public event EventHandler? PlaybackStateChanged;

        public event EventHandler<MediaEngineFailure>? Failed;

        public MediaPlaybackState PlaybackState { get; private set; } = MediaPlaybackState.None;

        public TimeSpan Position { get; set; }

        public TimeSpan NaturalDuration { get; set; }

        public double PlaybackRate { get; set; } = 1.0;

        public double Volume { get; set; } = 1.0;

        public bool IsMuted { get; set; }

        /// <summary>Wie oft der Dienst die Wiedergabe gestartet oder fortgesetzt hat.</summary>
        public int PlayCount { get; private set; }

        /// <summary>Ob der Dienst die Engine freigegeben hat.</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>Lässt den nächsten Start mit dieser Ausnahme scheitern, wie es ein Rechner ohne Wiedergabe tut.</summary>
        public Exception? PlayFailure { get; set; }

        public void Play()
        {
            if (PlayFailure is not null)
            {
                throw PlayFailure;
            }

            PlayCount++;
            SetState(MediaPlaybackState.Playing);
        }

        public void Pause() => SetState(MediaPlaybackState.Paused);

        /// <summary>Meldet eine Spur, die nicht abspielbar ist, wie es die Media-Pipeline tut.</summary>
        public void RaiseFailure(string? message)
            => Failed?.Invoke(this, new MediaEngineFailure(message, MediaPlayerError.DecodingError));

        public void Dispose() => IsDisposed = true;

        private void SetState(MediaPlaybackState state)
        {
            PlaybackState = state;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

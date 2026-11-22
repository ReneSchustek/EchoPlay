using Windows.Media.Playback;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Die Wiedergabe-Engine des Betriebssystems: ein <see cref="MediaPlayer"/>, der die
    /// Abspielliste des Dienstes spielt. Reicht dessen Ereignisse als einfache Meldungen weiter,
    /// damit <see cref="PlayerService"/> keine WinRT-Ereignistypen kennen muss.
    /// </summary>
    internal sealed class MediaPlayerEngine : IMediaEngine
    {
        private readonly MediaPlayer _player;

        /// <summary>Erzeugt den Player mit der Abspielliste als Quelle.</summary>
        /// <param name="playlist">Die Abspielliste des Dienstes.</param>
        public MediaPlayerEngine(MediaPlaybackList playlist)
        {
            _player = new() { Source = playlist };
            _player.PlaybackSession.PlaybackStateChanged += OnSessionStateChanged;
            _player.MediaFailed += OnMediaFailed;
        }

        /// <inheritdoc/>
        public event EventHandler? PlaybackStateChanged;

        /// <inheritdoc/>
        public event EventHandler<MediaEngineFailure>? Failed;

        /// <inheritdoc/>
        public MediaPlaybackState PlaybackState => _player.PlaybackSession.PlaybackState;

        /// <inheritdoc/>
        public TimeSpan Position
        {
            get => _player.PlaybackSession.Position;
            set => _player.PlaybackSession.Position = value;
        }

        /// <inheritdoc/>
        public TimeSpan NaturalDuration => _player.PlaybackSession.NaturalDuration;

        /// <inheritdoc/>
        public double PlaybackRate
        {
            get => _player.PlaybackSession.PlaybackRate;
            set => _player.PlaybackSession.PlaybackRate = value;
        }

        /// <inheritdoc/>
        public double Volume
        {
            get => _player.Volume;
            set => _player.Volume = value;
        }

        /// <inheritdoc/>
        public bool IsMuted
        {
            get => _player.IsMuted;
            set => _player.IsMuted = value;
        }

        /// <inheritdoc/>
        public void Play() => _player.Play();

        /// <inheritdoc/>
        public void Pause() => _player.Pause();

        /// <inheritdoc/>
        public void Dispose()
        {
            _player.PlaybackSession.PlaybackStateChanged -= OnSessionStateChanged;
            _player.MediaFailed -= OnMediaFailed;
            _player.Dispose();
        }

        private void OnSessionStateChanged(MediaPlaybackSession sender, object args)
            => PlaybackStateChanged?.Invoke(this, EventArgs.Empty);

        private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
            => Failed?.Invoke(this, new MediaEngineFailure(args.ErrorMessage, args.Error));
    }
}

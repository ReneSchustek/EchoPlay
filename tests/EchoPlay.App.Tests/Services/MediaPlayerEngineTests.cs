using EchoPlay.App.Services;
using Windows.Media.Playback;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft, dass <see cref="MediaPlayerEngine"/> Einstellungen an den echten
    /// <see cref="MediaPlayer"/> weitergibt. Ohne Abspielen: Der Player lässt sich auch auf einem
    /// Rechner ohne Medienwiedergabe anlegen und einstellen, starten nicht.
    /// </summary>
    public sealed class MediaPlayerEngineTests
    {
        [Fact]
        public void NewEngine_DoesNotPlayByItself()
        {
            MediaPlaybackList playlist = new();
            using MediaPlayerEngine engine = new(playlist);

            // Mit einer Liste als Quelle beginnt der Player gleich zu öffnen; abspielen tut er nicht.
            Assert.NotEqual(MediaPlaybackState.Playing, engine.PlaybackState);
            Assert.Equal(TimeSpan.Zero, engine.Position);
            Assert.Equal(TimeSpan.Zero, engine.NaturalDuration);
        }

        [Fact]
        public void Settings_ReachThePlayer()
        {
            MediaPlaybackList playlist = new();
            using MediaPlayerEngine engine = new(playlist);

            engine.Volume = 0.25;
            engine.IsMuted = true;
            engine.PlaybackRate = 1.25;

            Assert.Equal(0.25, engine.Volume, precision: 3);
            Assert.True(engine.IsMuted);
            Assert.Equal(1.25, engine.PlaybackRate, precision: 3);
        }

        [Fact]
        public void Position_CanBeSetWithoutAPlayingTrack()
        {
            MediaPlaybackList playlist = new();
            using MediaPlayerEngine engine = new(playlist);

            engine.Position = TimeSpan.Zero;
            engine.Pause();

            Assert.Equal(TimeSpan.Zero, engine.Position);
        }
    }
}

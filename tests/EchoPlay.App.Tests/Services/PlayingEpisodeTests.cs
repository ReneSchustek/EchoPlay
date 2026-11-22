using EchoPlay.App.Services;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft, dass die laufende Folge Kennung, Spuren und Stelle in der Folge zusammenhält.
    /// </summary>
    public sealed class PlayingEpisodeTests
    {
        private static readonly Guid EpisodeId = Guid.Parse("3c1f0e9a-5b7d-4b8e-9a2f-1d6c7e8f9a0b");

        [Fact]
        public void Snapshot_WithKnownDurations_ReturnsPositionInWholeEpisode()
        {
            PlayingEpisode episode = new();
            episode.Start(EpisodeId, ["a.mp3", "b.mp3"],
                new EpisodeTimeline([TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(4)]));

            (Guid id, TimeSpan position) = episode.Snapshot(1, TimeSpan.FromMinutes(1));

            Assert.Equal(EpisodeId, id);
            Assert.Equal(TimeSpan.FromMinutes(4), position);
            Assert.Equal(TimeSpan.FromMinutes(7), episode.TotalDuration);
            Assert.Equal(["a.mp3", "b.mp3"], episode.TrackPaths);
        }

        [Fact]
        public void Clear_ForgetsEpisodeTracksAndDurations()
        {
            PlayingEpisode episode = new();
            episode.Start(EpisodeId, ["a.mp3"], new EpisodeTimeline([TimeSpan.FromMinutes(3)]));

            episode.Clear();

            Assert.Equal(Guid.Empty, episode.Snapshot(0, TimeSpan.Zero).Id);
            Assert.Empty(episode.TrackPaths);
            Assert.Equal(TimeSpan.Zero, episode.TotalDuration);
        }

        [Fact]
        public void ToOverall_WithoutDurations_KeepsPositionInTrack()
        {
            PlayingEpisode episode = new();
            episode.Start(EpisodeId, ["a.mp3"], EpisodeTimeline.Empty);

            Assert.Equal(TimeSpan.FromSeconds(42), episode.ToOverall(0, TimeSpan.FromSeconds(42)));
        }
    }
}

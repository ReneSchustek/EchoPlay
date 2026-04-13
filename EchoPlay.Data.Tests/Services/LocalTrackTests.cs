using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="LocalTrackDataService"/>.
    /// </summary>
    public sealed class LocalTrackTests : DbTestBase
    {
        [Fact]
        public async Task GetByEpisodeIdAsync_ReturnsTracks()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");

            _ = Context.LocalTracks.Add(new LocalTrack
            {
                EpisodeId = episode.Id,
                FilePath = @"C:\track1.mp3",
                TrackNumber = 1
            });
            _ = Context.LocalTracks.Add(new LocalTrack
            {
                EpisodeId = episode.Id,
                FilePath = @"C:\track2.mp3",
                TrackNumber = 2
            });
            _ = await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            LocalTrackDataService service = new(Context, NullLoggerFactory);
            IReadOnlyList<LocalTrack> tracks = await service.GetByEpisodeIdAsync(episode.Id);

            Assert.Equal(2, tracks.Count);
        }

        [Fact]
        public async Task GetByEpisodeIdAsync_ReturnsEmpty_WhenNoTracks()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");

            LocalTrackDataService service = new(Context, NullLoggerFactory);
            IReadOnlyList<LocalTrack> tracks = await service.GetByEpisodeIdAsync(episode.Id);

            Assert.Empty(tracks);
        }

        [Fact]
        public async Task SaveTracksForEpisodeAsync_InsertsNewTracks()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");

            List<LocalTrack> tracks =
            [
                new LocalTrack { EpisodeId = episode.Id, FilePath = @"C:\a.mp3", TrackNumber = 1 },
                new LocalTrack { EpisodeId = episode.Id, FilePath = @"C:\b.mp3", TrackNumber = 2 }
            ];

            LocalTrackDataService service = new(Context, NullLoggerFactory);
            await service.SaveTracksForEpisodeAsync(episode.Id, tracks);
            Context.ChangeTracker.Clear();

            IReadOnlyList<LocalTrack> result = await service.GetByEpisodeIdAsync(episode.Id);
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task SaveTracksForEpisodeAsync_ReplacesExistingTracks()
        {
            Series series = await DataBuilder.PersistSeriesAsync("TKKG");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Folge 1");

            // Alte Tracks anlegen
            _ = Context.LocalTracks.Add(new LocalTrack
            {
                EpisodeId = episode.Id,
                FilePath = @"C:\old.mp3",
                TrackNumber = 1
            });
            _ = await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            // Neue Tracks speichern (ersetzt alte)
            List<LocalTrack> newTracks =
            [
                new LocalTrack { EpisodeId = episode.Id, FilePath = @"C:\new1.mp3", TrackNumber = 1 },
                new LocalTrack { EpisodeId = episode.Id, FilePath = @"C:\new2.mp3", TrackNumber = 2 }
            ];

            LocalTrackDataService service = new(Context, NullLoggerFactory);
            await service.SaveTracksForEpisodeAsync(episode.Id, newTracks);
            Context.ChangeTracker.Clear();

            IReadOnlyList<LocalTrack> result = await service.GetByEpisodeIdAsync(episode.Id);
            Assert.Equal(2, result.Count);
            Assert.Contains(result, t => t.FilePath == @"C:\new1.mp3");
        }
    }
}

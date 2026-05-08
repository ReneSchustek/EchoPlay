using EchoPlay.Core.Models.Import;
using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Mapping;

namespace EchoPlay.Spotify.Tests.Mapping
{
    /// <summary>
    /// Tests für die Übersetzung eines Spotify-Albums in eine <see cref="ImportEpisode"/>.
    /// Ein Album entspricht in EchoPlay einer Hörspielfolge.
    /// </summary>
    public sealed class SpotifyEpisodeMapperTests
    {
        [Fact]
        public void MapAlbumToEpisode_AlbumWithThreeTracks_SumsDurations()
        {
            SpotifyAlbumDto album = new()
            {
                SpotifyAlbumId = "album-1",
                Title = "Folge 17 – Das Geheimnis",
                ReleaseDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                ImageUrl = "https://i.scdn.co/album.jpg",
                TotalTracks = 3
            };
            SpotifyTrackDto[] tracks =
            [
                new() { SpotifyTrackId = "t1", Title = "Kapitel 1", Duration = TimeSpan.FromMinutes(20) },
                new() { SpotifyTrackId = "t2", Title = "Kapitel 2", Duration = TimeSpan.FromMinutes(15) },
                new() { SpotifyTrackId = "t3", Title = "Kapitel 3", Duration = TimeSpan.FromMinutes(10) }
            ];

            ImportEpisode result = SpotifyEpisodeMapper.MapAlbumToEpisode(album, tracks, orderIndex: 5);

            Assert.Equal("album-1", result.SourceEpisodeId);
            Assert.Equal("Folge 17 – Das Geheimnis", result.Title);
            Assert.Equal(17, result.EpisodeNumber);
            Assert.Equal(new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), result.ReleaseDate);
            Assert.Equal(TimeSpan.FromMinutes(45), result.Duration);
            Assert.Equal(5, result.OrderIndex);
            Assert.Equal("https://open.spotify.com/album/album-1", result.ProviderUrl);
            Assert.Equal("https://i.scdn.co/album.jpg", result.CoverImageUrl);
            Assert.Equal("Spotify", result.Source);
        }

        [Fact]
        public void MapAlbumToEpisode_EmptyTracks_DurationIsZero()
        {
            SpotifyAlbumDto album = new()
            {
                SpotifyAlbumId = "album-empty",
                Title = "Album ohne Tracks",
                TotalTracks = 0
            };

            ImportEpisode result = SpotifyEpisodeMapper.MapAlbumToEpisode(album, [], orderIndex: 0);

            Assert.Equal(TimeSpan.Zero, result.Duration);
            Assert.Equal("https://open.spotify.com/album/album-empty", result.ProviderUrl);
        }

        [Fact]
        public void MapAlbumToEpisode_TitleWithoutNumber_EpisodeNumberIsNull()
        {
            SpotifyAlbumDto album = new()
            {
                SpotifyAlbumId = "album-x",
                Title = "Sonderausgabe",
                TotalTracks = 1
            };
            SpotifyTrackDto[] tracks = [new() { SpotifyTrackId = "t", Title = "Track", Duration = TimeSpan.FromMinutes(5) }];

            ImportEpisode result = SpotifyEpisodeMapper.MapAlbumToEpisode(album, tracks, orderIndex: 1);

            Assert.Null(result.EpisodeNumber);
        }

        [Fact]
        public void MapAlbumToEpisode_NullCoverAndReleaseDate_PassesNullThrough()
        {
            SpotifyAlbumDto album = new()
            {
                SpotifyAlbumId = "album-null",
                Title = "Folge 7",
                ReleaseDate = null,
                ImageUrl = null,
                TotalTracks = 1
            };
            SpotifyTrackDto[] tracks = [new() { SpotifyTrackId = "t", Title = "Kapitel", Duration = TimeSpan.FromMinutes(30) }];

            ImportEpisode result = SpotifyEpisodeMapper.MapAlbumToEpisode(album, tracks, orderIndex: 2);

            Assert.Null(result.ReleaseDate);
            Assert.Null(result.CoverImageUrl);
            Assert.Equal(7, result.EpisodeNumber);
        }
    }
}

using System;
using System.Collections.Generic;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Mapping;
using EchoPlay.Core.Models.Import;

namespace EchoPlay.AppleMusic.Tests.Mapping
{
    /// <summary>
    /// Verifiziert das Mapping iTunes-Album → ImportEpisode.
    /// </summary>
    public sealed class AppleMusicEpisodeMapperTests
    {
        [Fact]
        public void MapAlbumToEpisode_WithTracks_SumsDurations()
        {
            ITunesCollectionDto album = new()
            {
                CollectionId = 12345,
                CollectionName = "Folge 042: Ein Test"
            };
            List<ITunesTrackDto> tracks =
            [
                new() { TrackTimeMillis = 60_000 },
                new() { TrackTimeMillis = 120_000 },
                new() { TrackTimeMillis = 30_000 }
            ];

            ImportEpisode result = AppleMusicEpisodeMapper.MapAlbumToEpisode(album, tracks, orderIndex: 7);

            Assert.Equal(TimeSpan.FromMilliseconds(210_000), result.Duration);
            Assert.Equal(7, result.OrderIndex);
            Assert.Equal("12345", result.SourceEpisodeId);
            Assert.Equal("Folge 042: Ein Test", result.Title);
            Assert.Equal("AppleMusic", result.Source);
        }

        [Fact]
        public void MapAlbumToEpisode_NullTrackDuration_TreatedAsZero()
        {
            ITunesCollectionDto album = new() { CollectionId = 1, CollectionName = "Folge X" };
            List<ITunesTrackDto> tracks =
            [
                new() { TrackTimeMillis = null },
                new() { TrackTimeMillis = 5_000 }
            ];

            ImportEpisode result = AppleMusicEpisodeMapper.MapAlbumToEpisode(album, tracks, orderIndex: 0);

            Assert.Equal(TimeSpan.FromMilliseconds(5_000), result.Duration);
        }

        [Fact]
        public void MapAlbumToEpisode_EmptyTracks_DurationIsZero()
        {
            ITunesCollectionDto album = new() { CollectionId = 1, CollectionName = "Leeres Album" };

            ImportEpisode result = AppleMusicEpisodeMapper.MapAlbumToEpisode(album, [], orderIndex: 0);

            Assert.Equal(TimeSpan.Zero, result.Duration);
        }

        [Fact]
        public void MapAlbumToEpisode_ArtworkUrl100_ConvertedTo600()
        {
            ITunesCollectionDto album = new()
            {
                CollectionId = 1,
                CollectionName = "Test",
                ArtworkUrl100 = "https://is1-ssl.mzstatic.com/cover/100x100bb.jpg"
            };

            ImportEpisode result = AppleMusicEpisodeMapper.MapAlbumToEpisode(album, [], orderIndex: 0);

            Assert.Equal("https://is1-ssl.mzstatic.com/cover/600x600bb.jpg", result.CoverImageUrl);
        }

        [Fact]
        public void MapAlbumToEpisode_NullArtwork_ResultIsNull()
        {
            ITunesCollectionDto album = new() { CollectionId = 1, CollectionName = "Test", ArtworkUrl100 = null };

            ImportEpisode result = AppleMusicEpisodeMapper.MapAlbumToEpisode(album, [], orderIndex: 0);

            Assert.Null(result.CoverImageUrl);
        }

        [Fact]
        public void MapAlbumToEpisode_NullAlbum_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(
                () => AppleMusicEpisodeMapper.MapAlbumToEpisode(null!, [], 0));
        }

        [Fact]
        public void MapAlbumToEpisode_ReleaseDateValid_ParsesCorrectly()
        {
            ITunesCollectionDto album = new()
            {
                CollectionId = 1,
                CollectionName = "Test",
                ReleaseDate = "2025-04-13T07:00:00Z"
            };

            ImportEpisode result = AppleMusicEpisodeMapper.MapAlbumToEpisode(album, [], 0);

            DateTime parsed = Assert.NotNull(result.ReleaseDate);
            Assert.Equal(2025, parsed.Year);
            Assert.Equal(4, parsed.Month);
        }

        [Fact]
        public void MapAlbumToEpisode_ReleaseDateInvalid_ReturnsNull()
        {
            ITunesCollectionDto album = new()
            {
                CollectionId = 1,
                CollectionName = "Test",
                ReleaseDate = "not-a-date"
            };

            ImportEpisode result = AppleMusicEpisodeMapper.MapAlbumToEpisode(album, [], 0);

            Assert.Null(result.ReleaseDate);
        }

        [Fact]
        public void MapAlbumToEpisode_TitleWithEpisodeNumber_ExtractsNumber()
        {
            ITunesCollectionDto album = new() { CollectionId = 1, CollectionName = "Folge 042: Test" };

            ImportEpisode result = AppleMusicEpisodeMapper.MapAlbumToEpisode(album, [], 0);

            Assert.Equal(42, result.EpisodeNumber);
        }
    }
}

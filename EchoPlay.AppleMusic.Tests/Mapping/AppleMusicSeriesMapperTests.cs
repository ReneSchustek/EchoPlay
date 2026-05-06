using System;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Mapping;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Scoring;

namespace EchoPlay.AppleMusic.Tests.Mapping
{
    /// <summary>
    /// Verifiziert das Mapping iTunes-Artist → ImportSeries.
    /// </summary>
    public sealed class AppleMusicSeriesMapperTests
    {
        [Fact]
        public void Map_HappyPath_ReturnsImportSeries()
        {
            ITunesArtistDto artist = new()
            {
                ArtistId = 99,
                ArtistName = "Die drei ???"
            };
            HoerspielScoreResult score = HoerspielScoreResult.Yes("99", HoerspielDecisionReason.KnownSeriesName, 95, "test");

            ImportSeries result = AppleMusicSeriesMapper.Map(artist, score);

            Assert.Equal("99", result.SourceSeriesId);
            Assert.Equal("AppleMusic", result.Source);
            Assert.Equal("Die drei ???", result.Title);
            Assert.True(result.IsHoerspiel);
            Assert.Equal(95, result.Score);
        }

        [Fact]
        public void Map_NoHoerspiel_PassesScoreThrough()
        {
            ITunesArtistDto artist = new() { ArtistId = 1, ArtistName = "Pop-Artist" };
            HoerspielScoreResult score = HoerspielScoreResult.No("1", HoerspielDecisionReason.NegativeMusicGenre, 10, "non-hoerspiel");

            ImportSeries result = AppleMusicSeriesMapper.Map(artist, score);

            Assert.False(result.IsHoerspiel);
            Assert.Equal(10, result.Score);
        }

        [Fact]
        public void Map_DescriptionAndCover_AreNullByDesign()
        {
            // iTunes Search API liefert keine Editorial Notes oder Artwork auf Artist-Ebene.
            ITunesArtistDto artist = new() { ArtistId = 1, ArtistName = "X" };
            HoerspielScoreResult score = HoerspielScoreResult.Yes("1", HoerspielDecisionReason.KnownSeriesName, 50, "test");

            ImportSeries result = AppleMusicSeriesMapper.Map(artist, score);

            Assert.Null(result.Description);
            Assert.Null(result.CoverImageUrl);
        }

        [Fact]
        public void Map_NullArtist_ThrowsArgumentNullException()
        {
            HoerspielScoreResult score = HoerspielScoreResult.Yes("1", HoerspielDecisionReason.KnownSeriesName, 50, "test");

            _ = Assert.Throws<ArgumentNullException>(() => AppleMusicSeriesMapper.Map(null!, score));
        }

        [Fact]
        public void Map_NullScoreResult_ThrowsArgumentNullException()
        {
            ITunesArtistDto artist = new() { ArtistId = 1, ArtistName = "X" };

            _ = Assert.Throws<ArgumentNullException>(() => AppleMusicSeriesMapper.Map(artist, null!));
        }
    }
}

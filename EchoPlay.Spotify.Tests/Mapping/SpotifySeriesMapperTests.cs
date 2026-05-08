using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Scoring;
using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Mapping;
using EchoPlay.Spotify.Tests.Fakes;

namespace EchoPlay.Spotify.Tests.Mapping
{
    /// <summary>
    /// Tests für die Übersetzung eines Spotify-Künstlers in ein <see cref="ImportSeries"/>.
    /// </summary>
    public sealed class SpotifySeriesMapperTests
    {
        [Fact]
        public async Task MapToImportSeriesAsync_FullArtist_PopulatesAllFields()
        {
            HoerspielScoreResult positiveResult = HoerspielScoreResult.Yes(
                "artist-1",
                HoerspielDecisionReason.KnownSeriesName,
                42,
                "Genre 'audiobook' erkannt");
            FakeHoerspielScorer scorer = new(positiveResult);
            SpotifySeriesMapper mapper = new(scorer);

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-1",
                Name = "Die drei ???",
                ImageUrl = "https://i.scdn.co/cover.jpg",
                Genres = ["audiobook"]
            };

            ImportSeries result = await mapper.MapToImportSeriesAsync(artist, "drei fragezeichen");

            Assert.Equal("artist-1", result.SourceSeriesId);
            Assert.Equal("Spotify", result.Source);
            Assert.Equal("Die drei ???", result.Title);
            Assert.Equal("https://i.scdn.co/cover.jpg", result.CoverImageUrl);
            Assert.True(result.IsHoerspiel);
            Assert.Equal(42, result.Score);
            Assert.Null(result.Description);
        }

        [Fact]
        public async Task MapToImportSeriesAsync_NegativeScore_SetsIsHoerspielFalse()
        {
            HoerspielScoreResult negativeResult = HoerspielScoreResult.No(
                "artist-2",
                HoerspielDecisionReason.NegativeMusicGenre,
                0,
                "Genre 'pop' erkannt");
            FakeHoerspielScorer scorer = new(negativeResult);
            SpotifySeriesMapper mapper = new(scorer);

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-2",
                Name = "Pop-Sternchen",
                Genres = ["pop"]
            };

            ImportSeries result = await mapper.MapToImportSeriesAsync(artist, "Pop-Sternchen");

            Assert.False(result.IsHoerspiel);
            Assert.Equal(0, result.Score);
        }

        [Fact]
        public async Task MapToImportSeriesAsync_NoCoverImage_PassesNullThrough()
        {
            FakeHoerspielScorer scorer = new(HoerspielScoreResult.Yes(
                "artist-3",
                HoerspielDecisionReason.KnownSeriesName,
                30,
                "Treffer"));
            SpotifySeriesMapper mapper = new(scorer);

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-3",
                Name = "Ohne Cover",
                ImageUrl = null
            };

            ImportSeries result = await mapper.MapToImportSeriesAsync(artist, "ohne cover");

            Assert.Null(result.CoverImageUrl);
        }

        [Fact]
        public async Task MapToImportSeriesAsync_CancellationRequested_PropagatesViaScorer()
        {
            ThrowingCancellationScorer scorer = new();
            SpotifySeriesMapper mapper = new(scorer);

            SpotifyArtistDto artist = new()
            {
                SpotifyArtistId = "artist-4",
                Name = "Egal"
            };

            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            _ = await Assert.ThrowsAsync<OperationCanceledException>(
                () => mapper.MapToImportSeriesAsync(artist, "egal", cts.Token));
        }

        // Lokaler Scorer, der den Token an die ScoreAsync-Implementierung weiterreicht und bei
        // ausgelöstem Abbruch eine OperationCanceledException wirft. Der allgemeine FakeHoerspielScorer
        // ignoriert den Token und kann das Verhalten daher nicht abdecken.
        private sealed class ThrowingCancellationScorer : IHoerspielScorer<SpotifyArtistDto>
        {
            public Task<HoerspielScoreResult> ScoreAsync(
                SpotifyArtistDto source,
                string searchQuery,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(HoerspielScoreResult.No(
                    string.Empty,
                    HoerspielDecisionReason.NegativeMusicGenre,
                    0,
                    string.Empty));
            }
        }
    }
}

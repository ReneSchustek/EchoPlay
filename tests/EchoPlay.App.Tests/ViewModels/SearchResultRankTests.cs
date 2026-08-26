using EchoPlay.App.ViewModels;
using EchoPlay.Core.Models.Import;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests der Rangfolge, nach der die Trefferkarten eingereiht werden.
    /// </summary>
    /// <remarks>
    /// Die Treffer erscheinen einzeln, sobald sie feststehen. Eine Sortierung am Ende gäbe
    /// es dafür nicht mehr — jede Karte muss beim Einfügen wissen, wohin sie gehört.
    /// </remarks>
    public sealed class SearchResultRankTests
    {
        [Fact]
        public void Create_WhenTheTitleContainsTheQuery_MarksTheHit()
        {
            SearchResultRank rank = SearchResultRank.Create(Series("Bibi Blocksberg", score: 100), "bibi");

            Assert.True(rank.MatchesQuery);
            Assert.Equal(100, rank.Score);
        }

        [Fact]
        public void Create_WhenOnlyTheArtistContainsTheQuery_MarksTheHit()
        {
            // Bei Album-Treffern steht der Serienname im Künstlerfeld — auch der zählt.
            ImportSeries album = new()
            {
                SourceSeriesId = "1",
                Source = "AppleMusic",
                Title = "Der verhexte Kalender",
                ArtistName = "Bibi Blocksberg",
                IsAlbumResult = true,
                Score = 50
            };

            Assert.True(SearchResultRank.Create(album, "Bibi").MatchesQuery);
        }

        [Fact]
        public void Create_WithoutAnyMatch_DoesNotMarkTheHit()
        {
            Assert.False(SearchResultRank.Create(Series("Benjamin Blümchen", score: 100), "Bibi").MatchesQuery);
        }

        [Fact]
        public void RanksBefore_ANameMatch_BeatsAHigherScore()
        {
            SearchResultRank match = SearchResultRank.Create(Series("Bibi und Tina", score: 50), "Bibi");
            SearchResultRank other = SearchResultRank.Create(Series("Benjamin Blümchen", score: 100), "Bibi");

            Assert.True(match.RanksBefore(other));
            Assert.False(other.RanksBefore(match));
        }

        [Fact]
        public void RanksBefore_AmongNameMatches_TheHigherScoreWins()
        {
            SearchResultRank strong = SearchResultRank.Create(Series("Bibi Blocksberg", score: 100), "Bibi");
            SearchResultRank weak = SearchResultRank.Create(Series("Bibi Babydoll", score: 50), "Bibi");

            Assert.True(strong.RanksBefore(weak));
            Assert.False(weak.RanksBefore(strong));
        }

        [Fact]
        public void RanksBefore_WithEqualRank_KeepsTheExistingOrder()
        {
            // Gleichstand heißt: nicht davor. So bleibt die Reihenfolge des Anbieters erhalten.
            SearchResultRank first = SearchResultRank.Create(Series("Bibi Eins", score: 60), "Bibi");
            SearchResultRank second = SearchResultRank.Create(Series("Bibi Zwei", score: 60), "Bibi");

            Assert.False(second.RanksBefore(first));
        }

        private static ImportSeries Series(string title, int score) => new()
        {
            SourceSeriesId = "1",
            Source = "AppleMusic",
            Title = title,
            Score = score
        };
    }
}

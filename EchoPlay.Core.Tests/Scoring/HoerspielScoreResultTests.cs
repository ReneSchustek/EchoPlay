using EchoPlay.Core.Scoring;

namespace EchoPlay.Core.Tests.Scoring
{
    /// <summary>
    /// Tests für <see cref="HoerspielScoreResult"/>.
    /// Prüft die Factory-Methoden <c>Yes</c> und <c>No</c>.
    /// </summary>
    public sealed class HoerspielScoreResultTests
    {
        [Fact]
        public void Yes_SetsIsHoerspielTrue()
        {
            // Yes-Ergebnis muss IsHoerspiel = true setzen
            HoerspielScoreResult result = HoerspielScoreResult.Yes(
                "artist-1", HoerspielDecisionReason.KnownSeriesName, 90, "debug");

            Assert.True(result.IsHoerspiel);
        }

        [Fact]
        public void No_SetsIsHoerspielFalse()
        {
            // No-Ergebnis muss IsHoerspiel = false setzen
            HoerspielScoreResult result = HoerspielScoreResult.No(
                "artist-1", HoerspielDecisionReason.NegativeMusicGenre, 5, "debug");

            Assert.False(result.IsHoerspiel);
        }

        [Fact]
        public void Yes_PreservesAllFields()
        {
            // Alle übergebenen Werte müssen im Ergebnis enthalten sein
            HoerspielScoreResult result = HoerspielScoreResult.Yes(
                "artist-abc", HoerspielDecisionReason.KnownSeriesName, 85, "TKKG erkannt");

            Assert.Equal("artist-abc", result.ArtistId);
            Assert.Equal(HoerspielDecisionReason.KnownSeriesName, result.Reason);
            Assert.Equal(85, result.Score);
            Assert.Equal("TKKG erkannt", result.DebugInfo);
        }

        [Fact]
        public void No_PreservesAllFields()
        {
            // Auch No-Ergebnisse müssen alle Felder korrekt setzen
            HoerspielScoreResult result = HoerspielScoreResult.No(
                "artist-xyz", HoerspielDecisionReason.NegativeMusicGenre, 10, "Pop-Genre erkannt");

            Assert.Equal("artist-xyz", result.ArtistId);
            Assert.Equal(HoerspielDecisionReason.NegativeMusicGenre, result.Reason);
            Assert.Equal(10, result.Score);
            Assert.Equal("Pop-Genre erkannt", result.DebugInfo);
        }

        [Fact]
        public void Yes_WithReasonNone_IsValid()
        {
            // Reason = None ist zulässig – Score-basierte Entscheidung
            HoerspielScoreResult result = HoerspielScoreResult.Yes(
                "artist-1", HoerspielDecisionReason.None, 60, "");

            Assert.True(result.IsHoerspiel);
            Assert.Equal(HoerspielDecisionReason.None, result.Reason);
        }
    }
}

using EchoPlay.Core.Scoring;

namespace EchoPlay.Spotify.Tests.Scoring
{
    /// <summary>
    /// Tests für den Hörspiel-Entscheidungscache.
    /// Stellt sicher, dass gespeicherte Ergebnisse korrekt abgerufen werden
    /// und nicht vorhandene Einträge sauber behandelt werden.
    /// </summary>
    public sealed class HoerspielDecisionCacheTests
    {
        /// <summary>Logger-Factory ohne Ausgabeziele für Tests.</summary>
        private static readonly EchoPlay.Logger.Abstractions.ILoggerFactory NullLoggerFactory =
            new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

        /// <summary>
        /// Ein gespeichertes Ergebnis wird beim nächsten Zugriff korrekt zurückgegeben.
        /// </summary>
        [Fact]
        public void TryGet_AfterStore_ReturnsStoredResult()
        {
            HoerspielDecisionCache cache = new(NullLoggerFactory);
            HoerspielScoreResult stored = HoerspielScoreResult.Yes(
                "artist-123",
                HoerspielDecisionReason.KnownSeriesName,
                100,
                "Test");

            cache.Store(stored);

            bool found = cache.TryGet("artist-123", out HoerspielScoreResult? result);

            Assert.True(found);
            Assert.NotNull(result);
            Assert.Equal("artist-123", result.ArtistId);
            Assert.True(result.IsHoerspiel);
        }

        /// <summary>
        /// Ein nicht gespeicherter Eintrag führt zu einem negativen Ergebnis.
        /// </summary>
        [Fact]
        public void TryGet_WithoutStore_ReturnsFalse()
        {
            HoerspielDecisionCache cache = new(NullLoggerFactory);

            bool found = cache.TryGet("nicht-vorhanden", out HoerspielScoreResult? result);

            Assert.False(found);
            Assert.Null(result);
        }

        /// <summary>
        /// Mehrere Einträge können unabhängig voneinander gespeichert und abgerufen werden.
        /// </summary>
        [Fact]
        public void Store_MultipleEntries_EachRetrievable()
        {
            HoerspielDecisionCache cache = new(NullLoggerFactory);

            HoerspielScoreResult result1 = HoerspielScoreResult.Yes(
                "artist-a",
                HoerspielDecisionReason.KnownSeriesName,
                100,
                "Serie A");

            HoerspielScoreResult result2 = HoerspielScoreResult.No(
                "artist-b",
                HoerspielDecisionReason.NegativeMusicGenre,
                0,
                "Musik B");

            cache.Store(result1);
            cache.Store(result2);

            _ = cache.TryGet("artist-a", out HoerspielScoreResult? retrieved1);
            _ = cache.TryGet("artist-b", out HoerspielScoreResult? retrieved2);

            Assert.NotNull(retrieved1);
            Assert.True(retrieved1.IsHoerspiel);

            Assert.NotNull(retrieved2);
            Assert.False(retrieved2.IsHoerspiel);
        }

        /// <summary>
        /// Ein erneutes Speichern derselben Artist-ID überschreibt den bestehenden Eintrag nicht.
        /// Das ConcurrentDictionary verwendet TryAdd, das erste Ergebnis bleibt bestehen.
        /// </summary>
        [Fact]
        public void Store_SameArtistTwice_KeepsFirstEntry()
        {
            HoerspielDecisionCache cache = new(NullLoggerFactory);

            HoerspielScoreResult first = HoerspielScoreResult.Yes(
                "artist-x",
                HoerspielDecisionReason.KnownSeriesName,
                100,
                "Erster Eintrag");

            HoerspielScoreResult second = HoerspielScoreResult.No(
                "artist-x",
                HoerspielDecisionReason.NegativeMusicGenre,
                0,
                "Zweiter Eintrag");

            cache.Store(first);
            cache.Store(second);

            _ = cache.TryGet("artist-x", out HoerspielScoreResult? retrieved);

            Assert.NotNull(retrieved);
            // Der erste Eintrag bleibt, weil TryAdd verwendet wird
            Assert.True(retrieved.IsHoerspiel);
            Assert.Equal("Erster Eintrag", retrieved.DebugInfo);
        }
    }
}

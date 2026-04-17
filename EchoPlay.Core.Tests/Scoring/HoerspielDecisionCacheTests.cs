using EchoPlay.Core.Scoring;
using EchoPlay.Core.Tests.Fakes;

namespace EchoPlay.Core.Tests.Scoring
{
    /// <summary>
    /// Tests für <see cref="HoerspielDecisionCache"/>.
    /// Prüft das Speichern, Abrufen und die Idempotenz des Caches.
    /// </summary>
    public sealed class HoerspielDecisionCacheTests
    {
        private static HoerspielDecisionCache BuildCache()
            => new(new FakeLoggerFactory());

        [Fact]
        public void TryGet_ReturnsFalse_WhenCacheIsEmpty()
        {
            // Leerer Cache liefert keinen Treffer
            HoerspielDecisionCache cache = BuildCache();

            bool found = cache.TryGet("artist-1", out HoerspielScoreResult? result);

            Assert.False(found);
            Assert.Null(result);
        }

        [Fact]
        public void Store_ThenTryGet_ReturnsCachedResult()
        {
            // Nach dem Speichern muss derselbe Wert abrufbar sein
            HoerspielDecisionCache cache = BuildCache();
            HoerspielScoreResult stored = HoerspielScoreResult.Yes(
                "artist-1", HoerspielDecisionReason.KnownSeriesName, 90, "Test");

            cache.Store(stored);
            bool found = cache.TryGet("artist-1", out HoerspielScoreResult? result);

            Assert.True(found);
            Assert.NotNull(result);
            Assert.True(result!.IsHoerspiel);
            Assert.Equal(90, result.Score);
        }

        [Fact]
        public void TryGet_ReturnsFalse_ForUnknownArtistId()
        {
            // Eine andere Artist-ID liefert keinen Treffer
            HoerspielDecisionCache cache = BuildCache();
            HoerspielScoreResult stored = HoerspielScoreResult.Yes(
                "artist-1", HoerspielDecisionReason.None, 70, "Test");

            cache.Store(stored);
            bool found = cache.TryGet("artist-2", out HoerspielScoreResult? result);

            Assert.False(found);
            Assert.Null(result);
        }

        [Fact]
        public void Store_IsIdempotent_SecondStoreIgnored()
        {
            // Ein zweiter Store für dieselbe ID überschreibt den ersten Eintrag nicht
            HoerspielDecisionCache cache = BuildCache();
            HoerspielScoreResult first = HoerspielScoreResult.Yes("artist-1", HoerspielDecisionReason.None, 80, "Erst");
            HoerspielScoreResult second = HoerspielScoreResult.No("artist-1", HoerspielDecisionReason.NegativeMusicGenre, 10, "Zweit");

            cache.Store(first);
            cache.Store(second);

            _ = cache.TryGet("artist-1", out HoerspielScoreResult? result);

            // Der erste Eintrag bleibt erhalten – TryAdd ignoriert Duplikate
            Assert.True(result!.IsHoerspiel);
            Assert.Equal(80, result.Score);
        }

        [Fact]
        public void Store_MultipleArtists_EachRetrievableIndependently()
        {
            // Mehrere Einträge koexistieren unabhängig im Cache
            HoerspielDecisionCache cache = BuildCache();
            cache.Store(HoerspielScoreResult.Yes("a1", HoerspielDecisionReason.None, 75, ""));
            cache.Store(HoerspielScoreResult.No("a2", HoerspielDecisionReason.NegativeMusicGenre, 5, ""));

            _ = cache.TryGet("a1", out HoerspielScoreResult? r1);
            _ = cache.TryGet("a2", out HoerspielScoreResult? r2);

            Assert.True(r1!.IsHoerspiel);
            Assert.False(r2!.IsHoerspiel);
        }
    }
}

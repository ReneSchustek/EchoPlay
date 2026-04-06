using EchoPlay.Core.Models;
using EchoPlay.LocalLibrary.Matching;

namespace EchoPlay.LocalLibrary.Tests.Matching
{
    /// <summary>
    /// Prüft die Klassifikationslogik von <see cref="TrackMatcher.Classify"/>.
    /// </summary>
    public sealed class TrackMatcherTests
    {
        private readonly TrackMatcher _matcher = new();

        [Fact]
        public void Classify_EqualCountBelowThreshold_ReturnsTbT()
        {
            // 12 lokale = 12 online, beide ≤ 20 → klassisches Track-by-Track-Hörspiel
            TrackMatchKind result = _matcher.Classify(localTrackCount: 12, onlineTrackCount: 12);

            Assert.Equal(TrackMatchKind.TbT, result);
        }

        [Fact]
        public void Classify_BothAboveThreshold_ReturnsStreaming()
        {
            // Beide > 20 → Streaming-Struktur mit vielen kurzen Tracks
            TrackMatchKind result = _matcher.Classify(localTrackCount: 25, onlineTrackCount: 28);

            Assert.Equal(TrackMatchKind.Streaming, result);
        }

        [Fact]
        public void Classify_MismatchedCount_ReturnsCustom()
        {
            // 3 lokal vs. 12 online → kein eindeutiges Muster, manuelle Zuordnung nötig
            TrackMatchKind result = _matcher.Classify(localTrackCount: 3, onlineTrackCount: 12);

            Assert.Equal(TrackMatchKind.Custom, result);
        }

        [Fact]
        public void Classify_NoLocalTracks_ReturnsCustom()
        {
            // 0 lokale Tracks → kein sinnvoller Abgleich möglich
            TrackMatchKind result = _matcher.Classify(localTrackCount: 0, onlineTrackCount: 12);

            Assert.Equal(TrackMatchKind.Custom, result);
        }
    }
}

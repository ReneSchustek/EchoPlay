using EchoPlay.Core.Models;
using EchoPlay.LocalLibrary.Matching;

namespace EchoPlay.LocalLibrary.Tests.Matching
{
    /// <summary>
    /// Ergänzende Tests für Grenzfälle von <see cref="TrackMatcher.Classify"/>.
    /// </summary>
    public sealed class TrackMatcherEdgeCaseTests
    {
        private readonly TrackMatcher _matcher = new();

        [Fact]
        public void Classify_ExactlyTwentyTracks_ReturnsTbT()
        {
            // Grenzwert: genau 20 Tracks auf beiden Seiten → TbT
            TrackMatchKind result = _matcher.Classify(localTrackCount: 20, onlineTrackCount: 20);

            Assert.Equal(TrackMatchKind.TbT, result);
        }

        [Fact]
        public void Classify_TwentyOneEqualTracks_ReturnsStreaming()
        {
            // 21 = 21: beide > 20 → Streaming (Grenzwert exakt oberhalb von 20)
            TrackMatchKind result = _matcher.Classify(localTrackCount: 21, onlineTrackCount: 21);

            Assert.Equal(TrackMatchKind.Streaming, result);
        }

        [Fact]
        public void Classify_UnequalAboveThreshold_ReturnsStreaming()
        {
            // Unterschiedliche Zahlen, beide > 20 → Streaming
            TrackMatchKind result = _matcher.Classify(localTrackCount: 25, onlineTrackCount: 30);

            Assert.Equal(TrackMatchKind.Streaming, result);
        }

        [Fact]
        public void Classify_SingleTrackMatch_ReturnsTbT()
        {
            // 1 lokal = 1 online (ungekürzte Komplett-Fassung als eine Datei)
            TrackMatchKind result = _matcher.Classify(localTrackCount: 1, onlineTrackCount: 1);

            Assert.Equal(TrackMatchKind.TbT, result);
        }
    }
}

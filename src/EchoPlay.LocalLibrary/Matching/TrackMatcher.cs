using EchoPlay.Core.Models;

namespace EchoPlay.LocalLibrary.Matching
{
    /// <summary>
    /// Bestimmt die Art des Abgleichs zwischen lokalen Audiodateien und Online-Trackdaten.
    /// Die Klassifikation entscheidet, wie die lokale Bibliothek mit den Streaming-Metadaten verknüpft wird.
    /// </summary>
    public sealed class TrackMatcher : ITrackMatcher
    {
        // Eine Hörspielfolge ist in wenige lange Kapitel geteilt, beim Anbieter oft in viele
        // kurze Spuren. Zwanzig ist dieselbe Grenze wie in HoerspielAlbumHeuristic: darüber
        // gilt eine Folge als in Streaming-Spuren zerlegt.
        private const int MaxChapterTracks = 20;

        /// <inheritdoc/>
        /// <returns>
        /// <see cref="TrackMatchKind.TbT"/> wenn Anzahl übereinstimmt und beide ≤ 20,
        /// <see cref="TrackMatchKind.Streaming"/> wenn beide > 20,
        /// <see cref="TrackMatchKind.Custom"/> in allen anderen Fällen (inkl. 0 lokale Tracks).
        /// </returns>
        public TrackMatchKind Classify(int localTrackCount, int onlineTrackCount)
        {
            if (localTrackCount == 0)
            {
                return TrackMatchKind.Custom;
            }

            if (localTrackCount == onlineTrackCount && onlineTrackCount <= MaxChapterTracks)
            {
                return TrackMatchKind.TbT;
            }

            if (localTrackCount > MaxChapterTracks && onlineTrackCount > MaxChapterTracks)
            {
                return TrackMatchKind.Streaming;
            }

            return TrackMatchKind.Custom;
        }
    }
}

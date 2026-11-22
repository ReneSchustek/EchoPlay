using EchoPlay.Core.Models;

namespace EchoPlay.LocalLibrary.Matching
{
    /// <summary>
    /// Abgleich zwischen lokalen und Online-Tracks. Die Übernahme der Folgen hängt nur an dieser
    /// Schnittstelle und lässt sich so ohne echte Dateien prüfen.
    /// </summary>
    public interface ITrackMatcher
    {
        /// <summary>
        /// Klassifiziert das Verhältnis zwischen lokalen und Online-Tracks.
        /// </summary>
        /// <param name="localTrackCount">Anzahl der gefundenen lokalen Audiodateien.</param>
        /// <param name="onlineTrackCount">Anzahl der Tracks laut Streaming-Anbieter.</param>
        /// <returns>Die ermittelte <see cref="TrackMatchKind"/>-Klassifikation.</returns>
        TrackMatchKind Classify(int localTrackCount, int onlineTrackCount);
    }
}

using EchoPlay.Core.Models;

namespace EchoPlay.LocalLibrary.Matching
{
    /// <summary>
    /// Definiert den Vertrag für den Abgleich zwischen lokalen und Online-Tracks.
    /// Ermöglicht die Entkopplung des SyncService von der konkreten Implementierung.
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

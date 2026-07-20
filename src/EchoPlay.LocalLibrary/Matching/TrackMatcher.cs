using EchoPlay.Core.Models;

namespace EchoPlay.LocalLibrary.Matching
{
    /// <summary>
    /// Bestimmt die Art des Abgleichs zwischen lokalen Audiodateien und Online-Trackdaten.
    /// Die Klassifikation entscheidet, wie die lokale Bibliothek mit den Streaming-Metadaten verknüpft wird.
    /// </summary>
    public sealed class TrackMatcher : ITrackMatcher
    {
        /// <summary>
        /// Klassifiziert das Verhältnis zwischen lokalen und Online-Tracks.
        /// </summary>
        /// <param name="localTrackCount">Anzahl der gefundenen lokalen Audiodateien.</param>
        /// <param name="onlineTrackCount">Anzahl der Tracks laut Streaming-Anbieter.</param>
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

            // Klassisches Hörspiel: wenige, lange Tracks – lokale und Online-Zahl stimmen überein
            if (localTrackCount == onlineTrackCount && onlineTrackCount <= 20)
            {
                return TrackMatchKind.TbT;
            }

            // Streaming-Struktur: viele kurze Tracks auf beiden Seiten
            if (localTrackCount > 20 && onlineTrackCount > 20)
            {
                return TrackMatchKind.Streaming;
            }

            return TrackMatchKind.Custom;
        }

        /// <summary>
        /// Erstellt eine Hilfsdatei im Episodenordner mit den erwarteten Tracknamen.
        /// Nützlich für Custom-Matching, damit der Nutzer die Zuordnung manuell korrigieren kann.
        /// </summary>
        /// <param name="episodeFolderPath">Absoluter Pfad zum Episodenordner.</param>
        /// <param name="expectedTrackNames">Erwartete Tracknamen in der richtigen Reihenfolge.</param>
        /// <exception cref="IOException">
        /// Wird geworfen, wenn der Ordner nicht beschreibbar ist.
        /// </exception>
        public static void WriteCustomHintFile(string episodeFolderPath, IReadOnlyList<string> expectedTrackNames)
        {
            string filePath = Path.Combine(episodeFolderPath, "expected_tracks.txt");

            IEnumerable<string> lines = expectedTrackNames
                .Select((name, index) => $"{index + 1} {name}");

            File.WriteAllLines(filePath, lines);
        }
    }
}

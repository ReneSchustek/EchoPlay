using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EchoPlay.LocalLibrary.Matching
{
    /// <summary>
    /// Schreibt eine Hilfsdatei mit den erwarteten Tracknamen in einen Episodenordner.
    /// Bewusst getrennt von <see cref="TrackMatcher"/>, damit die Klassifikation zustandslos
    /// und IO-frei bleibt (Datei-IO ist eine eigene Verantwortung).
    /// </summary>
    public static class CustomMatchHintWriter
    {
        /// <summary>
        /// Erstellt eine Hilfsdatei (<c>expected_tracks.txt</c>) im Episodenordner mit den
        /// erwarteten Tracknamen. Nützlich für Custom-Matching zur manuellen Korrektur.
        /// </summary>
        /// <param name="episodeFolderPath">Absoluter Pfad zum Episodenordner.</param>
        /// <param name="expectedTrackNames">Erwartete Tracknamen in der richtigen Reihenfolge.</param>
        /// <exception cref="IOException">Wird geworfen, wenn der Ordner nicht beschreibbar ist.</exception>
        public static void WriteHintFile(string episodeFolderPath, IReadOnlyList<string> expectedTrackNames)
        {
            ArgumentNullException.ThrowIfNull(expectedTrackNames);

            string filePath = Path.Combine(episodeFolderPath, "expected_tracks.txt");

            IEnumerable<string> lines = expectedTrackNames
                .Select((name, index) => $"{index + 1} {name}");

            File.WriteAllLines(filePath, lines);
        }
    }
}

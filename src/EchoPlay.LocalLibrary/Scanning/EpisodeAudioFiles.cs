using EchoPlay.LocalLibrary.Parsing;

namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Findet die Audiodateien einer Folge. Liegen keine direkt im Folgenordner, gehören die in
    /// seinen Unterordnern dazu: Folgen auf mehreren CDs liegen oft als <c>CD 1</c>, <c>CD 2</c>
    /// oder <c>100 A</c>, <c>100 B</c> unter dem Folgenordner.
    /// </summary>
    /// <remarks>
    /// Einlesen, Bericht und Online-Abgleich fragen dieselbe Regel. Zählte der Bericht einen
    /// Ordner als vorhanden, den das Einlesen nicht aufnimmt, meldeten beide verschiedene Lücken.
    /// </remarks>
    public static class EpisodeAudioFiles
    {
        // In der Sammlung liegen CD-Ordner eine Ebene unter dem Folgenordner („CD 1", „100 A");
        // zwei Ebenen lassen Raum für eine Box mit Teil und CD, ohne fremde Ordner einzusammeln.
        private const int MaxDepth = 2;

        /// <summary>
        /// Liefert die Audiodateien der Folge in Wiedergabereihenfolge: zuerst die direkt im
        /// Ordner, sonst die der Unterordner nach Ordnername und darin nach Dateiname.
        /// </summary>
        /// <param name="episodeFolder">Der Folgenordner.</param>
        /// <returns>Die Pfade; leer, wenn der Ordner keine Audiodatei enthält.</returns>
        /// <exception cref="IOException">Wenn der Ordner nicht lesbar ist; der Aufrufer protokolliert.</exception>
        /// <exception cref="UnauthorizedAccessException">Wenn der Zugriff verweigert wird.</exception>
        public static IReadOnlyList<string> Collect(string episodeFolder)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(episodeFolder);

            int? episodeNumber = LocalEpisodeNumbers.ExtractNumber(Path.GetFileName(episodeFolder));
            List<string> tracks = [];
            CollectInto(episodeFolder, episodeNumber, depth: 0, tracks);
            return tracks;
        }

        /// <summary>
        /// Ob der Ordner nach <see cref="Collect"/> eine Folge enthält. Ein unlesbarer Ordner
        /// zählt nicht.
        /// </summary>
        /// <param name="episodeFolder">Der Folgenordner.</param>
        /// <returns><see langword="true"/>, wenn mindestens eine Audiodatei dazugehört.</returns>
        public static bool HasAudio(string episodeFolder)
        {
            try
            {
                return Collect(episodeFolder).Count > 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Für die Frage „liegt die Folge vor" ist ein unlesbarer Ordner wie ein leerer;
                // das Einlesen meldet denselben Ordner mit Grund im Protokoll.
                return false;
            }
        }

        /// <summary>
        /// Wie viele Ordner die Spuren einer Folge umfassen. Mehr als einer heißt: mehrere CDs,
        /// deren Tag-Spurnummern je CD wieder bei 1 beginnen.
        /// </summary>
        /// <param name="trackPaths">Die Spuren einer Folge.</param>
        /// <returns>Die Zahl verschiedener Ordner.</returns>
        public static int FolderCount(IReadOnlyList<string> trackPaths)
        {
            ArgumentNullException.ThrowIfNull(trackPaths);

            return trackPaths.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }

        /// <summary>
        /// Ob ein Unterordner eine eigene Folge ist statt ein Teil dieser. Teile zählen von vorn
        /// („01 (A) …", „1 - …", „CD 2"), eine eigene Folge trägt eine andere, größere Nummer:
        /// „011" in einer Box „010" mit zwei Unterordnern.
        /// </summary>
        private static bool IsOwnEpisode(string subfolder, int? episodeNumber, int siblingCount)
        {
            int? subNumber = LocalEpisodeNumbers.ExtractNumber(Path.GetFileName(subfolder));
            return subNumber is not null
                && episodeNumber is not null
                && subNumber != episodeNumber
                && subNumber > siblingCount;
        }

        private static void CollectInto(string folder, int? episodeNumber, int depth, List<string> tracks)
        {
            List<string> direct = [.. Directory.GetFiles(folder).Where(EchoPlay.Core.AudioExtensions.IsAudioFile).Order(StringComparer.Ordinal)];
            if (direct.Count > 0 || depth == MaxDepth)
            {
                tracks.AddRange(direct);
                return;
            }

            string[] subfolders = [.. Directory.GetDirectories(folder).Order(StringComparer.OrdinalIgnoreCase)];
            foreach (string subfolder in subfolders)
            {
                if (IsOwnEpisode(subfolder, episodeNumber, subfolders.Length))
                {
                    continue;
                }

                CollectInto(subfolder, episodeNumber, depth + 1, tracks);
            }
        }
    }
}

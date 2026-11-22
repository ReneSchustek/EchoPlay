using EchoPlay.App.Helpers;
using EchoPlay.LocalLibrary.Parsing;
using EchoPlay.Logger.Abstractions;
using System.Globalization;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Sucht im Ordner einer Serie die Lücken in der Nummerierung der Folgen: als Zahlen für
    /// den Gesamtbericht und als fertige Zeilen für den Dialog einer einzelnen Serie.
    /// </summary>
    /// <remarks>Läuft im Thread-Pool und darf keine UI-Elemente anfassen.</remarks>
    /// <param name="logger">Protokoll der Prüfung.</param>
    internal sealed class LocalEpisodeGapAnalyzer(ILogger logger)
    {
        /// <summary>
        /// Analysiert lokale Lücken und liefert sowohl die fehlenden Nummern als auch
        /// die höchste gefundene Nummer. Strukturierte Variante für den Gesamtbericht.
        /// Läuft im Thread-Pool und darf keine UI-Elemente anfassen.
        /// </summary>
        /// <param name="seriesFolderPath">Absoluter Pfad des Serienordners.</param>
        public (List<int> Gaps, int MaxNumber) AnalyzeForReport(string seriesFolderPath)
        {
            string[] subfolders;
            try
            {
                subfolders = Directory.GetDirectories(seriesFolderPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Die Serie erscheint ohne Lücken im Bericht; der Grund steht im Protokoll.
                logger.Warning("Serienordner für den Bericht nicht lesbar: {Folder} – {Reason}", EchoPlay.Core.Logging.PathRedactor.Redact(seriesFolderPath), ex.Message);
                return ([], 0);
            }

            List<string> episodeFolderNames = EpisodeFolderNames(subfolders);
            if (episodeFolderNames.Count == 0) return ([], 0);

            IReadOnlyList<int> numbers = LocalEpisodeNumbers.Scan(episodeFolderNames);
            if (numbers.Count == 0) return ([], 0);

            return ([.. LocalEpisodeNumbers.FindGaps(numbers)], numbers[^1]);
        }

        /// <summary>
        /// Analysiert den Serienordner auf fehlende Folgen und gibt Anzeige-Meldungen zurück.
        /// Wird für die Einzelserien-Prüfung verwendet (formatierter Text für den Dialog).
        /// </summary>
        /// <param name="seriesFolderPath">Stammordner der Serie, dessen Unterordner ausgewertet werden.</param>
        public static List<string> Describe(string seriesFolderPath)
        {
            string[] subfolders;
            try
            {
                subfolders = Directory.GetDirectories(seriesFolderPath);
            }
            catch (IOException)
            {
                return [SafeResourceLoader.Get(
                    "MissingEpisodesFolderUnreadable", "Ordner konnte nicht gelesen werden.")];
            }
            catch (UnauthorizedAccessException)
            {
                return [SafeResourceLoader.Get(
                    "MissingEpisodesFolderAccessDenied", "Zugriff auf den Ordner verweigert.")];
            }

            List<string> episodeFolderNames = EpisodeFolderNames(subfolders);
            if (episodeFolderNames.Count == 0)
            {
                return [SafeResourceLoader.Get(
                    "MissingEpisodesNoEpisodeFolders", "Keine Folgenordner mit Audiodateien gefunden.")];
            }

            IReadOnlyList<int> numbers = LocalEpisodeNumbers.Scan(episodeFolderNames);
            return numbers.Count == 0 ? [DescribeUnnumbered(episodeFolderNames.Count)] : DescribeGaps(numbers);
        }

        /// <summary>Folgen sind da, aber keine trägt eine erkennbare Nummer.</summary>
        private static string DescribeUnnumbered(int folderCount) => string.Format(
            CultureInfo.CurrentCulture,
            PluralText.Pattern(
                folderCount,
                "MissingEpisodesNoNumbersSingular",
                "MissingEpisodesNoNumbersPlural",
                "{0} Folge vorhanden (keine Nummerierung erkannt).",
                "{0} Folgen vorhanden (keine Nummerierung erkannt)."),
            folderCount);

        /// <summary>Die Lücken in den Nummern: eine Kopfzeile und je fehlender Folge eine Zeile.</summary>
        private static List<string> DescribeGaps(IReadOnlyList<int> numbers)
        {
            IReadOnlyList<int> gaps = LocalEpisodeNumbers.FindGaps(numbers);
            int minNumber = numbers[0];
            int maxNumber = numbers[^1];

            if (gaps.Count == 0)
            {
                string noGapsPattern = SafeResourceLoader.Get(
                    "MissingEpisodesNoGaps", "Alle Folgen vorhanden ({0}–{1}), keine Lücken.");
                return [string.Format(CultureInfo.CurrentCulture, noGapsPattern, minNumber, maxNumber)];
            }

            string header = string.Format(
                CultureInfo.CurrentCulture,
                PluralText.Pattern(
                    gaps.Count,
                    "MissingEpisodesGapsHeaderSingular",
                    "MissingEpisodesGapsHeaderPlural",
                    "{0} fehlende Folge im Bereich {1}–{2}:",
                    "{0} fehlende Folgen im Bereich {1}–{2}:"),
                gaps.Count, minNumber, maxNumber);

            string gapEntryPattern = SafeResourceLoader.Get("MissingEpisodesGapEntry", "  Folge {0}");
            return [header, string.Empty, .. gaps.Select(gap => string.Format(
                CultureInfo.CurrentCulture,
                gapEntryPattern,
                gap.ToString("D3", CultureInfo.CurrentCulture)))];
        }

        /// <summary>
        /// Die Namen der Unterordner, die Folgen sind. Nur Ordner mit mindestens einer
        /// Audiodatei zählen; Jubiläumsfolgen legen ihr Audio oft in Unterordner (CD1, Teil A),
        /// und dieselbe Regel nutzt das Einlesen.
        /// </summary>
        /// <param name="subfolders">Die Unterordner des Serienordners.</param>
        /// <returns>Die Ordnernamen ohne Pfad.</returns>
        private static List<string> EpisodeFolderNames(IEnumerable<string> subfolders)
            => [.. subfolders
                .Where(EchoPlay.LocalLibrary.Scanning.EpisodeAudioFiles.HasAudio)
                .Select(Path.GetFileName)
                .OfType<string>()];
    }
}

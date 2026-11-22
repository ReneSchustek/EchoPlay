using EchoPlay.Core;
using EchoPlay.LocalLibrary.IO;
using EchoPlay.LocalLibrary.Parsing;

namespace EchoPlay.LocalLibrary.Analysis
{
    /// <summary>
    /// Erkennt das Episodenmuster eines Bibliotheks-Wurzelordners durch Analyse der Episodenordner
    /// aller enthaltenen Serien. Geht zwei Ebenen tief: Root → Serienordner → Episodenordner.
    /// Unterstützt zusätzlich flache Serien, bei denen die Audiodateien direkt im Serienordner liegen.
    /// </summary>
    public sealed class EpisodePatternAnalyzer : IEpisodePatternAnalyzer
    {
        /// <summary>
        /// Feste Kandidaten-Muster für Episodenordner, geordnet nach erwarteter Häufigkeit.
        /// Das Präfix-Muster <c>{*} - {number:000} - {title}</c> deckt Serien ab,
        /// bei denen der Serienname dem Ordnernamen vorangestellt wird.
        /// </summary>
        private static readonly string[] CandidatePatterns =
        [
            "{number:000} - {title}",
            "{*} - {number:000} - {title}",
            "Folge {number:000} - {title}",
            "{number:000}_{title}",
            "{number} - {title}",
            "{title} - {number:000}"
        ];

        /// <summary>
        /// Mindest-Trefferquote, damit ein Muster als Vorschlag gilt. Trifft ein Muster nicht
        /// einmal die Hälfte der Namen, fehlten mit ihm beim Einlesen mehr Nummern, als es liefert.
        /// </summary>
        private const double MinMatchPercentage = 0.5;

        /// <summary>
        /// Maximale Anzahl Serien-Unterordner, die für die Analyse gesampelt werden.
        /// Begrenzt die Laufzeit bei sehr großen Bibliotheken mit hunderten Serien.
        /// </summary>
        private const int MaxSeriesSampleCount = 20;

        /// <inheritdoc/>
        public Task<IReadOnlyList<PatternSuggestion>> AnalyzeAsync(string libraryRootPath)
        {
            if (!Directory.Exists(libraryRootPath))
            {
                return Task.FromResult<IReadOnlyList<PatternSuggestion>>([]);
            }

            // Das Auflisten der Ordner ist synchron und liefe sonst auf dem UI-Thread.
            return Task.Run(() => AnalyzeInternal(libraryRootPath));
        }

        /// <summary>
        /// Kernlogik der Analyse. Sammelt Episodenordner-Namen aus zwei Ebenen und testet
        /// alle Kandidaten-Muster dagegen.
        /// </summary>
        private static IReadOnlyList<PatternSuggestion> AnalyzeInternal(string libraryRootPath)
        {
            List<string> episodeFolderNames = CollectEpisodeFolderNames(libraryRootPath, out bool hasFlatSeries);

            if (episodeFolderNames.Count == 0 && !hasFlatSeries)
            {
                return [];
            }

            List<PatternSuggestion> suggestions = [.. Score(episodeFolderNames, isFlatStructure: false)];

            // Flache Serien liefern Dateinamen statt Ordnernamen. Ein Muster, das schon die
            // Ordner trifft, erscheint nur einmal.
            if (hasFlatSeries)
            {
                foreach (PatternSuggestion flat in Score(CollectFlatSeriesFileNames(libraryRootPath), isFlatStructure: true))
                {
                    if (!suggestions.Any(s => s.Pattern == flat.Pattern))
                    {
                        suggestions.Add(flat);
                    }
                }
            }

            // Bester Treffer zuerst, bei gleicher Quote die Reihenfolge aus CandidatePatterns.
            // „{number:000}" und „{number}" lesen dieselben Namen und treffen deshalb gleich oft;
            // vorn steht „{number:000}", weil es beim Umbenennen dreistellige Nummern schreibt,
            // die im Explorer in der richtigen Reihenfolge stehen.
            return [.. suggestions
                .OrderByDescending(s => s.MatchPercentage)
                .ThenBy(s => Array.IndexOf(CandidatePatterns, s.Pattern))];
        }

        /// <summary>
        /// Prüft jedes Kandidaten-Muster gegen die Namen und liefert die, die oft genug treffen.
        /// </summary>
        private static IEnumerable<PatternSuggestion> Score(List<string> names, bool isFlatStructure)
        {
            if (names.Count == 0)
            {
                yield break;
            }

            foreach (string pattern in CandidatePatterns)
            {
                EpisodeFolderParser parser = new(pattern);
                int matchCount = names.Count(name => parser.TryParse(name, out _, out _));
                double matchPercentage = (double)matchCount / names.Count;

                if (matchPercentage >= MinMatchPercentage)
                {
                    yield return new PatternSuggestion(pattern, matchCount, matchPercentage, isFlatStructure);
                }
            }
        }

        /// <summary>
        /// Sammelt Episodenordner-Namen aus den Serien-Unterordnern des Bibliotheks-Wurzelordners.
        /// Geht zwei Ebenen tief: Root → Serienordner → Episodenordner.
        /// </summary>
        /// <param name="libraryRootPath">Absoluter Pfad zum Bibliotheks-Wurzelordner.</param>
        /// <param name="hasFlatSeries">
        /// Wird auf <see langword="true"/> gesetzt, wenn mindestens eine Serie gefunden wurde,
        /// die keine Unterordner hat, aber direkte Audiodateien enthält (flache Struktur).
        /// </param>
        /// <returns>Liste aller gefundenen Episodenordner-Namen (nur der Name, ohne Pfad).</returns>
        private static List<string> CollectEpisodeFolderNames(string libraryRootPath, out bool hasFlatSeries)
        {
            hasFlatSeries = false;
            List<string> names = [];

            string[] seriesFolders = SafeFileSystem.EnumerateDirectories(libraryRootPath);

            // Zwanzig Serien zeigen das Benennungsschema der Sammlung; jede weitere kostet auf
            // einem Netzlaufwerk spürbar Zeit, ohne den Vorschlag zu ändern.
            foreach (string seriesFolder in seriesFolders.Take(MaxSeriesSampleCount))
            {
                string[] episodeFolders;

                try
                {
                    episodeFolders = Directory.GetDirectories(seriesFolder);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Eine Stichprobe für den Mustervorschlag: Ein unlesbarer Ordner fällt heraus,
                    // das Einlesen meldet denselben Ordner mit Grund im Protokoll.
                    continue;
                }

                if (episodeFolders.Length == 0)
                {
                    hasFlatSeries |= SafeFileSystem.EnumerateFiles(seriesFolder).Any(AudioExtensions.IsAudioFile);
                    continue;
                }

                names.AddRange(episodeFolders.Select(Path.GetFileName).OfType<string>());
            }

            return names;
        }

        /// <summary>
        /// Sammelt Dateinamen (ohne Extension) der Audiodateien aus flachen Serien-Ordnern.
        /// Flache Serien haben keine Episodenunterordner, die Episoden liegen direkt als Audiodateien vor.
        /// </summary>
        private static List<string> CollectFlatSeriesFileNames(string libraryRootPath)
        {
            List<string> names = [];

            string[] seriesFolders = SafeFileSystem.EnumerateDirectories(libraryRootPath);

            foreach (string seriesFolder in seriesFolders.Take(MaxSeriesSampleCount))
            {
                // Nur Ordner ohne Unterordner sind flache Serien
                if (SafeFileSystem.EnumerateDirectories(seriesFolder).Length > 0)
                {
                    continue;
                }

                names.AddRange(SafeFileSystem.EnumerateFiles(seriesFolder)
                    .Where(AudioExtensions.IsAudioFile)
                    .Select(Path.GetFileNameWithoutExtension)
                    .OfType<string>());
            }

            return names;
        }
    }
}

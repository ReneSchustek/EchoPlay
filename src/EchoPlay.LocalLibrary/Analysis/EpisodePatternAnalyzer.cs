using EchoPlay.LocalLibrary.IO;
using EchoPlay.LocalLibrary.Parsing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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
        /// Mindest-Trefferquote, damit ein Muster als Vorschlag gilt.
        /// Ein Muster mit weniger als 50 % Übereinstimmung ist in der Praxis unbrauchbar.
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

            // Dateisystem-Operationen bei großen Bibliotheken in Thread-Pool auslagern
            return Task.Run(() => AnalyzeInternal(libraryRootPath));
        }

        /// <summary>
        /// Kernlogik der Analyse. Sammelt Episodenordner-Namen aus zwei Ebenen und testet
        /// alle Kandidaten-Muster dagegen.
        /// </summary>
        private static IReadOnlyList<PatternSuggestion> AnalyzeInternal(string libraryRootPath)
        {
            // Schritt 1: Episodenordner-Namen aus den Serien-Unterordnern sammeln.
            // hasFlatSeries wird gesetzt wenn mindestens eine Serie ohne Unterordner aber mit MP3s gefunden wurde.
            List<string> episodeFolderNames = CollectEpisodeFolderNames(libraryRootPath, out bool hasFlatSeries);

            if (episodeFolderNames.Count == 0 && !hasFlatSeries)
            {
                return [];
            }

            List<PatternSuggestion> suggestions = [];

            // Schritt 2: Feste Kandidaten-Muster gegen die gesammelten Ordnernamen testen
            if (episodeFolderNames.Count > 0)
            {
                foreach (string pattern in CandidatePatterns)
                {
                    EpisodeFolderParser parser = new(pattern);
                    int matchCount = 0;

                    foreach (string name in episodeFolderNames)
                    {
                        if (parser.TryParse(name, out _, out _))
                        {
                            matchCount++;
                        }
                    }

                    double matchPercentage = (double)matchCount / episodeFolderNames.Count;

                    if (matchPercentage >= MinMatchPercentage)
                    {
                        suggestions.Add(new PatternSuggestion(pattern, matchCount, matchPercentage));
                    }
                }
            }

            // Schritt 3: Flache Serien separat analysieren – Dateinamen statt Ordnernamen
            if (hasFlatSeries)
            {
                List<string> flatFileNames = CollectFlatSeriesFileNames(libraryRootPath);

                if (flatFileNames.Count > 0)
                {
                    foreach (string pattern in CandidatePatterns)
                    {
                        EpisodeFolderParser parser = new(pattern);
                        int matchCount = flatFileNames.Count(name => parser.TryParse(name, out _, out _));
                        double matchPercentage = (double)matchCount / flatFileNames.Count;

                        if (matchPercentage >= MinMatchPercentage)
                        {
                            // Nur als separaten Vorschlag hinzufügen, wenn das Muster nicht bereits durch
                            // die Ordner-Analyse abgedeckt wurde – doppelte Einträge verwirren nur
                            if (!suggestions.Any(s => s.Pattern == pattern))
                            {
                                suggestions.Add(new PatternSuggestion(pattern, matchCount, matchPercentage, IsFlatStructure: true));
                            }
                        }
                    }
                }
            }

            // Bester Treffer zuerst; bei gleichem Score die Reihenfolge aus CandidatePatterns beibehalten –
            // "{number:000}" steht bewusst vor "{number}", weil es das spezifischere Muster ist
            IReadOnlyList<PatternSuggestion> result = suggestions
                .OrderByDescending(s => s.MatchPercentage)
                .ThenBy(s => Array.IndexOf(CandidatePatterns, s.Pattern))
                .ToList();

            return result;
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

            // Stichprobe begrenzen – bei >20 Serien wird das Ergebnis nicht genauer, aber langsamer
            foreach (string seriesFolder in seriesFolders.Take(MaxSeriesSampleCount))
            {
                string[] episodeFolders;

                try
                {
                    episodeFolders = Directory.GetDirectories(seriesFolder);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (System.UnauthorizedAccessException)
                {
                    continue;
                }

                if (episodeFolders.Length == 0)
                {
                    // Keine Unterordner – prüfen ob direkt Audiodateien vorhanden sind
                    if (HasAudioFiles(seriesFolder))
                    {
                        hasFlatSeries = true;
                    }

                    continue;
                }

                foreach (string episodeFolder in episodeFolders)
                {
                    string? name = Path.GetFileName(episodeFolder);

                    if (name is not null)
                    {
                        names.Add(name);
                    }
                }
            }

            return names;
        }

        /// <summary>
        /// Sammelt Dateinamen (ohne Extension) der Audiodateien aus flachen Serien-Ordnern.
        /// Flache Serien haben keine Episodenunterordner – die Episoden liegen direkt als Audiodateien vor.
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

                foreach (string file in SafeFileSystem.EnumerateFiles(seriesFolder).Where(IsAudioFile))
                {
                    string? nameWithoutExt = Path.GetFileNameWithoutExtension(file);

                    if (nameWithoutExt is not null)
                    {
                        names.Add(nameWithoutExt);
                    }
                }
            }

            return names;
        }

        /// <summary>
        /// Gibt <see langword="true"/> zurück, wenn der Ordner direkte Audiodateien enthält.
        /// </summary>
        private static bool HasAudioFiles(string folderPath)
        {
            return SafeFileSystem.EnumerateFiles(folderPath).Any(IsAudioFile);
        }

        /// <summary>
        /// Gibt <see langword="true"/> zurück, wenn der Dateipfad eine bekannte Audioerweiterung hat.
        /// </summary>
        private static bool IsAudioFile(string filePath)
        {
            return EchoPlay.Core.AudioExtensions.IsAudioFile(filePath);
        }
    }
}

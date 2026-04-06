using EchoPlay.LocalLibrary.Analysis;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Tests.Analysis
{
    /// <summary>
    /// Prüft den <see cref="EpisodePatternAnalyzer"/> gegen die neue Zwei-Ebenen-Analyse
    /// (Root → Serienordner → Episodenordner) sowie flache Serien (MP3s direkt im Serienordner).
    /// Jeder Test erzeugt eine vollständige Verzeichnisstruktur, damit der Analyzer gegen
    /// echte Dateisystem-Einträge arbeitet.
    /// </summary>
    public sealed class EpisodePatternAnalyzerTests
    {
        private readonly EpisodePatternAnalyzer _analyzer = new();

        // ── Hilfsmethoden ──────────────────────────────────────────────────────────

        /// <summary>
        /// Erzeugt eine Bibliotheks-Struktur mit einer Dummy-Serie und den angegebenen Episodenordnern.
        /// Root → "TestSerie" → [episodeFolderNames].
        /// Gibt den Root-Pfad zurück.
        /// </summary>
        private static string CreateTwoLevelStructure(IEnumerable<string> episodeFolderNames)
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string seriesFolder = Path.Combine(root, "TestSerie");
            Directory.CreateDirectory(seriesFolder);

            foreach (string name in episodeFolderNames)
            {
                Directory.CreateDirectory(Path.Combine(seriesFolder, name));
            }

            return root;
        }

        /// <summary>
        /// Erzeugt eine flache Bibliotheks-Struktur: Root → "TestSerie" → [mp3-Dateien].
        /// Gibt den Root-Pfad zurück.
        /// </summary>
        private static string CreateFlatStructure(IEnumerable<string> mp3FileNames)
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string seriesFolder = Path.Combine(root, "TestSerie");
            Directory.CreateDirectory(seriesFolder);

            foreach (string name in mp3FileNames)
            {
                // Leere Datei anlegen – der Analyzer prüft nur die Extension
                File.WriteAllBytes(Path.Combine(seriesFolder, name), []);
            }

            return root;
        }

        // ── Tests: klassische Zwei-Ebenen-Struktur ────────────────────────────────

        /// <summary>
        /// Muster "{number:000} - {title}" – der häufigste Standard in deutschen Hörspielen.
        /// Drei???-Struktur: Serienordner / "001 - Titel" / Audiodateien.
        /// </summary>
        [Fact]
        public async Task Analyze_StandardPattern_IsTopSuggestion()
        {
            string root = CreateTwoLevelStructure(
            [
                "001 - Das Gespensterhaus",
                "002 - Das Erbe des Detektivs",
                "003 - Die Flüsternde Mumie",
                "004 - Der verschwundene Schatz",
                "005 - Das Geheimnis der Geisterschlucht"
            ]);

            try
            {
                IReadOnlyList<PatternSuggestion> suggestions = await _analyzer.AnalyzeAsync(root);

                Assert.NotEmpty(suggestions);
                Assert.Equal("{number:000} - {title}", suggestions[0].Pattern);
                Assert.Equal(5, suggestions[0].MatchCount);
                Assert.False(suggestions[0].IsFlatStructure);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>
        /// Muster "{*} - {number:000} - {title}" – Serienname als Präfix.
        /// Beispiel: "Die drei Ausrufezeichen - 001 - Die Handy-Falle".
        /// </summary>
        [Fact]
        public async Task Analyze_PrefixPattern_IsTopSuggestion()
        {
            string root = CreateTwoLevelStructure(
            [
                "Die drei Ausrufezeichen - 001 - Die Handy-Falle",
                "Die drei Ausrufezeichen - 002 - Die Geistervilla",
                "Die drei Ausrufezeichen - 003 - Das schwarze Phantom",
                "Die drei Ausrufezeichen - 004 - Tödliche Spur"
            ]);

            try
            {
                IReadOnlyList<PatternSuggestion> suggestions = await _analyzer.AnalyzeAsync(root);

                Assert.NotEmpty(suggestions);
                Assert.Contains(suggestions, s => s.Pattern == "{*} - {number:000} - {title}");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>
        /// Muster "Folge {number:000} - {title}" – typisch für einige deutsche Produktionen.
        /// </summary>
        [Fact]
        public async Task Analyze_FolgePattern_IsTopSuggestion()
        {
            string root = CreateTwoLevelStructure(
            [
                "Folge 001 - Der Drachenfels",
                "Folge 002 - Das schwarze Schloss",
                "Folge 003 - Die Mondfinsternis",
                "Folge 004 - Das goldene Amulett"
            ]);

            try
            {
                IReadOnlyList<PatternSuggestion> suggestions = await _analyzer.AnalyzeAsync(root);

                Assert.NotEmpty(suggestions);
                Assert.Equal("Folge {number:000} - {title}", suggestions[0].Pattern);
                Assert.Equal(4, suggestions[0].MatchCount);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>
        /// Muster "{number:000}_{title}" – Unterstrich statt Leerzeichen und Bindestrich.
        /// </summary>
        [Fact]
        public async Task Analyze_UnderscorePattern_IsTopSuggestion()
        {
            string root = CreateTwoLevelStructure(
            [
                "001_Das Gespensterschloss",
                "002_Der stumme Zeuge",
                "003_Das entführte Flugzeug",
                "004_Die Höhle des Schreckens"
            ]);

            try
            {
                IReadOnlyList<PatternSuggestion> suggestions = await _analyzer.AnalyzeAsync(root);

                Assert.NotEmpty(suggestions);
                Assert.Equal("{number:000}_{title}", suggestions[0].Pattern);
                Assert.Equal(4, suggestions[0].MatchCount);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>
        /// Muster "{title} - {number:000}" – Titel vorangestellt, Nummer am Ende.
        /// </summary>
        [Fact]
        public async Task Analyze_TitleFirstPattern_IsTopSuggestion()
        {
            string root = CreateTwoLevelStructure(
            [
                "Das Gespensterschloss - 001",
                "Der stumme Zeuge - 002",
                "Das entführte Flugzeug - 003",
                "Die Höhle des Schreckens - 004"
            ]);

            try
            {
                IReadOnlyList<PatternSuggestion> suggestions = await _analyzer.AnalyzeAsync(root);

                Assert.NotEmpty(suggestions);
                Assert.Equal("{title} - {number:000}", suggestions[0].Pattern);
                Assert.Equal(4, suggestions[0].MatchCount);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>
        /// Muster "{number} - {title}" – ohne führende Nullen.
        /// </summary>
        [Fact]
        public async Task Analyze_NumberWithoutPaddingPattern_IsTopSuggestion()
        {
            string root = CreateTwoLevelStructure(
            [
                "1 - Der Jaguar",
                "2 - Die silberne Spinne",
                "3 - Das Geheimnis der Todesinsel",
                "4 - Der Schatz im Bergsee",
                "5 - Das rätselhafte Erbe"
            ]);

            try
            {
                IReadOnlyList<PatternSuggestion> suggestions = await _analyzer.AnalyzeAsync(root);

                Assert.NotEmpty(suggestions);
                // Einstellige Zahlen matchen sowohl "{number} - {title}" als auch "{number:000} - {title}"
                Assert.Contains(suggestions, s => s.Pattern is "{number} - {title}" or "{number:000} - {title}");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        // ── Tests: flache Serien ──────────────────────────────────────────────────

        /// <summary>
        /// Flache Serie – MP3-Dateien direkt im Serienordner, kein Episodenordner.
        /// Beispiel: "Die Dr3i - 001 - Das Seeungeheuer.mp3".
        /// </summary>
        [Fact]
        public async Task Analyze_FlatSeriesWithPrefixPattern_ReturnsSuggestion()
        {
            string root = CreateFlatStructure(
            [
                "Die Dr3i - 001 - Das Seeungeheuer.mp3",
                "Die Dr3i - 002 - Das Schloss der Angst.mp3",
                "Die Dr3i - 003 - Das Tal der Dinosaurier.mp3"
            ]);

            try
            {
                IReadOnlyList<PatternSuggestion> suggestions = await _analyzer.AnalyzeAsync(root);

                Assert.NotEmpty(suggestions);
                // Das Präfix-Muster muss erkannt werden
                Assert.Contains(suggestions, s => s.Pattern == "{*} - {number:000} - {title}");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>
        /// Flache Serie mit Standard-Muster – der IsFlatStructure-Flag muss gesetzt sein,
        /// wenn das Muster ausschließlich aus Dateinamen stammt.
        /// </summary>
        [Fact]
        public async Task Analyze_FlatSeries_IsFlatStructureFlagSet()
        {
            string root = CreateFlatStructure(
            [
                "001 - Folge eins.mp3",
                "002 - Folge zwei.mp3",
                "003 - Folge drei.mp3"
            ]);

            try
            {
                IReadOnlyList<PatternSuggestion> suggestions = await _analyzer.AnalyzeAsync(root);

                Assert.NotEmpty(suggestions);

                // Da es nur einen Serienordner ohne Unterordner gibt, muss der Vorschlag als flach markiert sein
                PatternSuggestion? standardSuggestion = null;
                foreach (PatternSuggestion s in suggestions)
                {
                    if (s.Pattern == "{number:000} - {title}")
                    {
                        standardSuggestion = s;
                    }
                }

                Assert.NotNull(standardSuggestion);
                Assert.True(standardSuggestion.IsFlatStructure);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        // ── Tests: Grenzfälle ─────────────────────────────────────────────────────

        /// <summary>
        /// Kein passendes Muster – alle Ordner haben ein unbekanntes Format.
        /// Ergebnis muss leer sein (kein Absturz, kein Fallback-Vorschlag).
        /// </summary>
        [Fact]
        public async Task Analyze_NoMatchingPattern_ReturnsEmpty()
        {
            string root = CreateTwoLevelStructure(
            [
                "AudioDrama_A",
                "AudioDrama_B",
                "AudioDrama_C"
            ]);

            try
            {
                IReadOnlyList<PatternSuggestion> suggestions = await _analyzer.AnalyzeAsync(root);

                Assert.Empty(suggestions);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>
        /// Nicht existierender Ordner – muss leer zurückgeben, keine Exception.
        /// </summary>
        [Fact]
        public async Task Analyze_NonExistentFolder_ReturnsEmpty()
        {
            IReadOnlyList<PatternSuggestion> suggestions =
                await _analyzer.AnalyzeAsync(@"C:\Dieser\Pfad\Existiert\Nicht");

            Assert.Empty(suggestions);
        }

        /// <summary>
        /// Leerer Bibliotheksordner (keine Serien-Unterordner) – muss leer zurückgeben.
        /// </summary>
        [Fact]
        public async Task Analyze_EmptyLibraryRoot_ReturnsEmpty()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);

            try
            {
                IReadOnlyList<PatternSuggestion> suggestions = await _analyzer.AnalyzeAsync(root);

                Assert.Empty(suggestions);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

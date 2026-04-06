using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Tests.Scanning
{
    /// <summary>
    /// Tests für <see cref="LocalLibraryScanner"/>.
    /// Nutzt temporäre Verzeichnisse als echtes Dateisystem – kein Mocking nötig.
    /// Jeder Test legt seine eigene Verzeichnisstruktur an und räumt sie im Teardown auf.
    /// </summary>
    public sealed class LocalLibraryScannerTests : IDisposable
    {
        private readonly string _root;
        private readonly LocalLibraryScanner _scanner;

        /// <summary>
        /// Erstellt ein temporäres Wurzelverzeichnis und den Scanner für jeden Test.
        /// Der <see cref="FakeTagTitleReader"/> gibt standardmäßig leere Strings zurück,
        /// sodass der Ordnername als Titelquelle greift.
        /// </summary>
        public LocalLibraryScannerTests()
        {
            _root    = Directory.CreateTempSubdirectory("echoplay_scanner_").FullName;
            _scanner = new LocalLibraryScanner(new FakeLoggerFactory(), new FakeTagTitleReader());
        }

        /// <summary>
        /// Löscht das temporäre Verzeichnis nach jedem Test.
        /// </summary>
        public void Dispose()
        {
            Directory.Delete(_root, recursive: true);
        }

        [Fact]
        public async Task ScanSeriesAsync_ReturnsEmpty_WhenRootDoesNotExist()
        {
            // Nicht existierendes Verzeichnis liefert leere Liste statt Exception
            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync("/not/existing/path", "{number}");

            Assert.Empty(results);
        }

        [Fact]
        public async Task ScanSeriesAsync_ReturnsEmpty_WhenRootIsEmpty()
        {
            // Leeres Wurzelverzeichnis enthält keine Serien
            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number}");

            Assert.Empty(results);
        }

        [Fact]
        public async Task ScanSeriesAsync_FindsSeries_ByFolderName()
        {
            // Serienordner = direkter Unterordner des Root
            string seriesPath  = Directory.CreateDirectory(Path.Combine(_root, "TKKG")).FullName;
            string episodePath = Directory.CreateDirectory(Path.Combine(seriesPath, "001")).FullName;
            File.WriteAllText(Path.Combine(episodePath, "track.mp3"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000}");

            Assert.Single(results);
            Assert.Equal("TKKG", results[0].SeriesName);
        }

        [Fact]
        public async Task ScanSeriesAsync_ParsesEpisodeNumber_FromFolderName()
        {
            // Episodennummer wird korrekt aus dem Ordnernamen extrahiert
            string seriesPath  = Directory.CreateDirectory(Path.Combine(_root, "TKKG")).FullName;
            string episodePath = Directory.CreateDirectory(Path.Combine(seriesPath, "042")).FullName;
            File.WriteAllText(Path.Combine(episodePath, "track.mp3"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000}");

            Assert.Equal(42, results[0].Episodes[0].ParsedNumber);
        }

        [Fact]
        public async Task ScanSeriesAsync_CollectsAllSupportedAudioFormats()
        {
            // mp3, m4a, flac, ogg werden alle erkannt – andere Dateien werden ignoriert
            string seriesPath  = Directory.CreateDirectory(Path.Combine(_root, "Serie")).FullName;
            string episodePath = Directory.CreateDirectory(Path.Combine(seriesPath, "001")).FullName;

            File.WriteAllText(Path.Combine(episodePath, "track1.mp3"),  string.Empty);
            File.WriteAllText(Path.Combine(episodePath, "track2.m4a"),  string.Empty);
            File.WriteAllText(Path.Combine(episodePath, "track3.flac"), string.Empty);
            File.WriteAllText(Path.Combine(episodePath, "track4.ogg"),  string.Empty);
            File.WriteAllText(Path.Combine(episodePath, "cover.jpg"),   string.Empty); // ignoriert

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000}");

            Assert.Equal(4, results[0].Episodes[0].TrackPaths.Count);
        }

        [Fact]
        public async Task ScanSeriesAsync_IgnoresNonAudioFiles()
        {
            // Nicht-Audio-Dateien (.txt, .jpg, .pdf) werden nicht als Tracks gezählt
            string seriesPath  = Directory.CreateDirectory(Path.Combine(_root, "Serie")).FullName;
            string episodePath = Directory.CreateDirectory(Path.Combine(seriesPath, "001")).FullName;

            File.WriteAllText(Path.Combine(episodePath, "track.mp3"),  string.Empty);
            File.WriteAllText(Path.Combine(episodePath, "info.txt"),   string.Empty);
            File.WriteAllText(Path.Combine(episodePath, "cover.jpg"),  string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000}");

            Assert.Single(results[0].Episodes[0].TrackPaths);
        }

        [Fact]
        public async Task ScanSeriesAsync_IncludesUnmatchedEpisodeFolders_WithNullNumber()
        {
            // Ordner ohne Muster-Treffer werden trotzdem importiert – ParsedNumber bleibt null,
            // der Ordnername dient als Titel. So gehen keine Hörspiele verloren.
            string seriesPath = Directory.CreateDirectory(Path.Combine(_root, "TKKG")).FullName;
            string matched    = Directory.CreateDirectory(Path.Combine(seriesPath, "001")).FullName;
            string unmatched  = Directory.CreateDirectory(Path.Combine(seriesPath, "extras")).FullName;

            File.WriteAllText(Path.Combine(matched, "track.mp3"),   string.Empty);
            File.WriteAllText(Path.Combine(unmatched, "bonus.mp3"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000}");

            Assert.Single(results);
            Assert.Equal(2, results[0].Episodes.Count);

            // Der gematchte Ordner hat eine Nummer, der ungematchte hat null
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 1);
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber is null);
        }

        [Fact]
        public async Task ScanSeriesAsync_ExcludesSeriesWithNoMatchingEpisodes()
        {
            // Serienordner ohne erkannte Episodenordner erscheinen nicht in den Ergebnissen
            Directory.CreateDirectory(Path.Combine(_root, "LeererOrdner"));

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000}");

            Assert.Empty(results);
        }

        [Fact]
        public async Task ScanSeriesAsync_FindsMultipleSeries()
        {
            // Mehrere Serienordner werden alle gefunden
            foreach (string series in new[] { "TKKG", "Die drei Fragezeichen", "FAMOUS FIVE" })
            {
                string seriesPath  = Directory.CreateDirectory(Path.Combine(_root, series)).FullName;
                string episodePath = Directory.CreateDirectory(Path.Combine(seriesPath, "001")).FullName;
                File.WriteAllText(Path.Combine(episodePath, "track.mp3"), string.Empty);
            }

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000}");

            Assert.Equal(3, results.Count);
        }

        [Fact]
        public async Task ScanSeriesAsync_TracksAreSortedAlphabetically()
        {
            // Tracks werden sortiert zurückgegeben – Wiedergabereihenfolge muss korrekt sein
            string seriesPath  = Directory.CreateDirectory(Path.Combine(_root, "Serie")).FullName;
            string episodePath = Directory.CreateDirectory(Path.Combine(seriesPath, "001")).FullName;

            File.WriteAllText(Path.Combine(episodePath, "track03.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(episodePath, "track01.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(episodePath, "track02.mp3"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000}");

            IReadOnlyList<string> tracks = results[0].Episodes[0].TrackPaths;
            Assert.Equal("track01.mp3", Path.GetFileName(tracks[0]));
            Assert.Equal("track02.mp3", Path.GetFileName(tracks[1]));
            Assert.Equal("track03.mp3", Path.GetFileName(tracks[2]));
        }

        [Fact]
        public async Task ScanSeriesAsync_UsesFolderName_OverTagTitle()
        {
            // Tag.Title enthält bei Hörspielen den Kapitel-Titel ("Inhaltsangabe", "Teil 1" o.Ä.),
            // nicht den Episodentitel. Der Ordnername ist daher die verlässlichere Quelle.
            string seriesPath  = Directory.CreateDirectory(Path.Combine(_root, "TKKG")).FullName;
            string episodePath = Directory.CreateDirectory(Path.Combine(seriesPath, "001 - 42 Titelaus Ordner")).FullName;
            string trackPath   = Path.Combine(episodePath, "track.mp3");
            File.WriteAllText(trackPath, string.Empty);

            FakeTagTitleReader tagReader = new(
                tagsByPath: new Dictionary<string, (string, string)>
                {
                    // Tag.Title = Kapitel-Titel → wird ignoriert; Ordnername "42 Titelaus Ordner" → "Titelaus Ordner"
                    [trackPath] = ("Kapitel 1 – Der Einbruch", "TKKG")
                });

            LocalLibraryScanner scanner = new(new FakeLoggerFactory(), tagReader);

            IReadOnlyList<LocalScanResult> results =
                await scanner.ScanSeriesAsync(_root, "{number:000} - {title}");

            Assert.Equal("Titelaus Ordner", results[0].Episodes[0].ParsedTitle);
        }

        [Fact]
        public async Task ScanSeriesAsync_FallsBackToFolderName_WhenTagTitleIsEmpty()
        {
            // Kein Tag-Titel verfügbar → Ordnername wird verwendet
            string seriesPath  = Directory.CreateDirectory(Path.Combine(_root, "TKKG")).FullName;
            string episodePath = Directory.CreateDirectory(Path.Combine(seriesPath, "007 - Sherlock Holmes")).FullName;
            File.WriteAllText(Path.Combine(episodePath, "track.mp3"), string.Empty);

            // FakeTagTitleReader ohne Konfiguration liefert leere Strings → Fallback
            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000} - {title}");

            Assert.Equal("Sherlock Holmes", results[0].Episodes[0].ParsedTitle);
        }

        [Fact]
        public async Task ScanSeriesAsync_StripsLeadingSequenceNumber_FromFolderDerivedTitle()
        {
            // Ordnernamen wie "001 - 42 Der Schatz" enthalten zwei Zahlen:
            // "001" = lokaler Sortierpräfix, "42" = eigentliche Episodennummer im Titel.
            // Nach dem Parsen landet "42 Der Schatz" als Titel – das führende "42 " wird bereinigt.
            string seriesPath  = Directory.CreateDirectory(Path.Combine(_root, "Serie")).FullName;
            string episodePath = Directory.CreateDirectory(Path.Combine(seriesPath, "001 - 42 Der Schatz")).FullName;
            File.WriteAllText(Path.Combine(episodePath, "track.mp3"), string.Empty);

            // Kein Tag-Titel → Ordnername mit Strip
            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000} - {title}");

            // "42 Der Schatz" → Strip "42 " → "Der Schatz"
            Assert.Equal("Der Schatz", results[0].Episodes[0].ParsedTitle);
        }

        [Fact]
        public async Task ScanSeriesAsync_FolderName_AlwaysPreferred_OverTagTitle()
        {
            // Auch wenn Tag.Title gesetzt ist, gewinnt der Ordnername –
            // "Der Schatz" aus dem Ordner ist der Episodentitel, "Teil 1" der Kapitel-Titel.
            string seriesPath  = Directory.CreateDirectory(Path.Combine(_root, "Serie")).FullName;
            string episodePath = Directory.CreateDirectory(Path.Combine(seriesPath, "001 - 42 Der Schatz")).FullName;
            string trackPath   = Path.Combine(episodePath, "track.mp3");
            File.WriteAllText(trackPath, string.Empty);

            FakeTagTitleReader tagReader = new(
                tagsByPath: new Dictionary<string, (string, string)>
                {
                    // Tag.Title = Kapitel → ignoriert; Ordner "42 Der Schatz" → Strip → "Der Schatz"
                    [trackPath] = ("Teil 1", string.Empty)
                });

            LocalLibraryScanner scanner = new(new FakeLoggerFactory(), tagReader);

            IReadOnlyList<LocalScanResult> results =
                await scanner.ScanSeriesAsync(_root, "{number:000} - {title}");

            Assert.Equal("Der Schatz", results[0].Episodes[0].ParsedTitle);
        }
        [Fact]
        public void GetSeriesFolders_ReturnsEmpty_WhenRootDoesNotExist()
        {
            // Nicht existierendes Verzeichnis liefert leere Liste statt Exception
            IReadOnlyList<string> folders = _scanner.GetSeriesFolders("/not/existing/path");

            Assert.Empty(folders);
        }

        [Fact]
        public void GetSeriesFolders_ReturnsEmpty_WhenRootHasNoSubdirectories()
        {
            // Root ohne Unterordner enthält keine Serien
            IReadOnlyList<string> folders = _scanner.GetSeriesFolders(_root);

            Assert.Empty(folders);
        }

        [Fact]
        public void GetSeriesFolders_ReturnsSeriesFolders_WithoutEpisodeScan()
        {
            // Nur Ordner-Listing – keine Audiodateien nötig, kein Deep-Scan
            Directory.CreateDirectory(Path.Combine(_root, "TKKG"));
            Directory.CreateDirectory(Path.Combine(_root, "Die drei Fragezeichen"));

            IReadOnlyList<string> folders = _scanner.GetSeriesFolders(_root);

            Assert.Equal(2, folders.Count);
            Assert.Contains(folders, f => Path.GetFileName(f) == "TKKG");
            Assert.Contains(folders, f => Path.GetFileName(f) == "Die drei Fragezeichen");
        }

        [Fact]
        public async Task ScanSeriesAsync_FiresOnSeriesScanned_PerSeries()
        {
            // onSeriesScanned wird pro erkannter Serie genau einmal aufgerufen
            string seriesA = Directory.CreateDirectory(Path.Combine(_root, "TKKG")).FullName;
            Directory.CreateDirectory(Path.Combine(seriesA, "001"));
            File.WriteAllText(Path.Combine(seriesA, "001", "track.mp3"), string.Empty);

            string seriesB = Directory.CreateDirectory(Path.Combine(_root, "Fünf Freunde")).FullName;
            Directory.CreateDirectory(Path.Combine(seriesB, "001"));
            File.WriteAllText(Path.Combine(seriesB, "001", "track.mp3"), string.Empty);

            List<string> reported = [];
            IProgress<LocalScanResult> onSeriesScanned = new Progress<LocalScanResult>(r =>
                reported.Add(r.SeriesName));

            await _scanner.ScanSeriesAsync(_root, "{number:000}", onSeriesScanned: onSeriesScanned);

            // Kurze Wartezeit, damit Progress-Callbacks verarbeitet werden können
            await Task.Delay(50);

            Assert.Equal(2, reported.Count);
            Assert.Contains("TKKG", reported);
            Assert.Contains("Fünf Freunde", reported);
        }

        // ── Flache Dateistrukturen (Typ B) ─────────────────────────────────────

        [Fact]
        public async Task ScanSeriesAsync_FlatFiles_RecognizesSeriesNameNumberTitle()
        {
            // Typ B1: "Serie - 001 - Titel.mp3" direkt im Serienordner
            string seriesPath = Directory.CreateDirectory(Path.Combine(_root, "Karl May")).FullName;
            File.WriteAllText(Path.Combine(seriesPath, "Karl May - 001 - Durch die Wüste.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(seriesPath, "Karl May - 002 - Durchs wilde Kurdistan.mp3"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000} - {title}");

            Assert.Single(results);
            Assert.Equal(2, results[0].Episodes.Count);
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 1);
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 2);
        }

        [Fact]
        public async Task ScanSeriesAsync_FlatFiles_NumberSpaceTitle()
        {
            // Typ B3: "01 Titel.mp3" – Nummer + Leerzeichen + Titel
            string seriesPath = Directory.CreateDirectory(Path.Combine(_root, "Serie")).FullName;
            File.WriteAllText(Path.Combine(seriesPath, "01 Erster Fall.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(seriesPath, "02 Zweiter Fall.mp3"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000} - {title}");

            Assert.Single(results);
            Assert.Equal(2, results[0].Episodes.Count);
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 1);
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 2);
        }

        [Fact]
        public async Task ScanSeriesAsync_FlatFiles_NoPattern_UsesFilenameAsTitle()
        {
            // Typ B4: kein erkennbares Nummernmuster – Dateiname als Titel
            string seriesPath = Directory.CreateDirectory(Path.Combine(_root, "Michel")).FullName;
            File.WriteAllText(Path.Combine(seriesPath, "Astrid Lindgren - Immer dieser Michel.mp3"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000} - {title}");

            Assert.Single(results);
            Assert.Single(results[0].Episodes);
            // Kein Muster erkannt → ParsedNumber ist null, Dateiname als Titel
            Assert.Null(results[0].Episodes[0].ParsedNumber);
            Assert.NotNull(results[0].Episodes[0].ParsedTitle);
        }

        [Fact]
        public async Task ScanSeriesAsync_FlatFiles_IgnoresNonAudioFiles()
        {
            // Nicht-Audio-Dateien (jpg, nfo, txt) werden nicht als Episoden gezählt
            string seriesPath = Directory.CreateDirectory(Path.Combine(_root, "Serie")).FullName;
            File.WriteAllText(Path.Combine(seriesPath, "Karl May - 001 - Titel.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(seriesPath, "cover.jpg"), string.Empty);
            File.WriteAllText(Path.Combine(seriesPath, "Thumbs.db"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000} - {title}");

            Assert.Single(results);
            Assert.Single(results[0].Episodes);
        }

        [Fact]
        public async Task ScanSeriesAsync_FlatFiles_MixedWithSubfolder_StillScansFlat()
        {
            // Mischform: Unterordner ohne Audio + Dateien direkt im Ordner
            // (wie Pumuckl mit "Kinderparty"-Ordner ohne eigene Audio)
            string seriesPath = Directory.CreateDirectory(Path.Combine(_root, "Pumuckel")).FullName;
            Directory.CreateDirectory(Path.Combine(seriesPath, "Kinderparty"));
            File.WriteAllText(Path.Combine(seriesPath, "01 Spuk in der Werkstatt.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(seriesPath, "02 Das verkaufte Bett.mp3"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000} - {title}");

            Assert.Single(results);
            Assert.Equal(2, results[0].Episodes.Count);
        }

        [Fact]
        public async Task ScanSeriesAsync_FlatFiles_PrefersFolderStructure_OverFlatScan()
        {
            // Wenn Unterordner MIT Audio existieren, wird die flache Erkennung NICHT ausgelöst
            string seriesPath = Directory.CreateDirectory(Path.Combine(_root, "TKKG")).FullName;
            string episodePath = Directory.CreateDirectory(Path.Combine(seriesPath, "001")).FullName;
            File.WriteAllText(Path.Combine(episodePath, "track.mp3"), string.Empty);
            // Lose Datei direkt im Serienordner – soll ignoriert werden
            File.WriteAllText(Path.Combine(seriesPath, "bonus.mp3"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000}");

            Assert.Single(results);
            // Nur die Episode aus dem Unterordner, nicht die lose Datei
            Assert.Single(results[0].Episodes);
            Assert.Equal(1, results[0].Episodes[0].ParsedNumber);
        }

        // ── Kassetten-Rips (Typ C) ─────────────────────────────────────────────

        [Fact]
        public async Task ScanSeriesAsync_CassetteRip_NumberSideTitle()
        {
            // Pumuckl-Muster: "01a Spuk in der Werkstatt.mp3"
            // Seite a = ungerade Episodennummer, Seite b = gerade
            string seriesPath = Directory.CreateDirectory(Path.Combine(_root, "Pumuckel")).FullName;
            File.WriteAllText(Path.Combine(seriesPath, "01a Spuk in der Werkstatt.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(seriesPath, "01b Das verkaufte Bett.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(seriesPath, "02a Das neue Badezimmer.mp3"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000} - {title}");

            Assert.Single(results);
            Assert.Equal(3, results[0].Episodes.Count);
            // 01a → 1, 01b → 2, 02a → 3
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 1);
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 2);
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 3);
        }

        [Fact]
        public async Task ScanSeriesAsync_CassetteRip_SeriesDashNumberSideDashTitle()
        {
            // Black-Beauty-Muster: "Black Beauty - 01a - Kindheit auf Gut Birtwick.mp3"
            string seriesPath = Directory.CreateDirectory(Path.Combine(_root, "Black Beauty")).FullName;
            File.WriteAllText(Path.Combine(seriesPath, "Black Beauty - 01a - Kindheit.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(seriesPath, "Black Beauty - 01b - Kindheit.mp3"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000} - {title}");

            Assert.Single(results);
            Assert.Equal(2, results[0].Episodes.Count);
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 1 && e.ParsedTitle == "Kindheit");
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 2 && e.ParsedTitle == "Kindheit");
        }

        [Fact]
        public async Task ScanSeriesAsync_CassetteRip_WithoutTitle()
        {
            // Dateien ohne Titel: "11a.mp3", "11b.mp3"
            string seriesPath = Directory.CreateDirectory(Path.Combine(_root, "Pumuckel")).FullName;
            File.WriteAllText(Path.Combine(seriesPath, "11a.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(seriesPath, "11b.mp3"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000} - {title}");

            Assert.Single(results);
            Assert.Equal(2, results[0].Episodes.Count);
            // 11a → 21, 11b → 22
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 21);
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 22);
        }

        [Fact]
        public async Task ScanSeriesAsync_CassetteRip_WithPrefix()
        {
            // Weihnachts-Sonderfolge mit Präfix: "W03a Titel.mp3"
            string seriesPath = Directory.CreateDirectory(Path.Combine(_root, "Pumuckel")).FullName;
            File.WriteAllText(Path.Combine(seriesPath, "W03a Pumuckl und die Christbaumkugeln.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(seriesPath, "W03b Pumuckl und die Schatulle.mp3"), string.Empty);

            IReadOnlyList<LocalScanResult> results =
                await _scanner.ScanSeriesAsync(_root, "{number:000} - {title}");

            Assert.Single(results);
            Assert.Equal(2, results[0].Episodes.Count);
            // W03a → Kassette 3, Seite a → 5; W03b → 6
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 5);
            Assert.Contains(results[0].Episodes, e => e.ParsedNumber == 6);
        }
    }
}

using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Tests.Fakes;
using EchoPlay.LocalLibrary.Tests.Infrastructure;

namespace EchoPlay.LocalLibrary.Tests.Scanning
{
    /// <summary>
    /// Prüft den Scan, wenn ein Serien- oder Folgenordner nicht lesbar ist: Der Ordner gilt als
    /// leer, der Scan läuft weiter, und das Protokoll nennt den Grund.
    /// </summary>
    /// <remarks>
    /// Die Tests arbeiten auf echten Ordnern im Temp-Verzeichnis und nehmen jede Sperre wieder zurück.
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public sealed class LocalLibraryScannerUnreadableTests : IDisposable
    {
        private const string Pattern = "{number:000} - {title}";
        private readonly string _root;

        public LocalLibraryScannerUnreadableTests()
        {
            _root = Directory.CreateTempSubdirectory("echoplay_scan_").FullName;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Fact]
        public async Task Scan_WithAnUnreadableSeriesFolder_SkipsItAndWarns()
        {
            using LockedFolder locked = LockedFolder.Create(_root, "Gesperrte Serie");
            _ = CreateEpisode("Lesbare Serie", "001 - Erste Folge");

            CapturingLoggerFactory log = new();
            IReadOnlyList<LocalScanResult> results = await new LocalLibraryScanner(log)
                .ScanSeriesAsync(_root, Pattern, ct: TestContext.Current.CancellationToken);

            LocalScanResult result = Assert.Single(results);
            Assert.Equal("Lesbare Serie", result.SeriesName);
            Assert.Contains(log.Entries, entry => entry.Level == "Warning" && entry.Message.Contains("Zugriff verweigert", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Scan_WithAnUnreadableEpisodeFolder_KeepsTheOtherEpisodes()
        {
            string series = CreateEpisode("Serie", "001 - Erste Folge");
            using LockedFolder locked = LockedFolder.Create(series, "002 - Gesperrte Folge");

            CapturingLoggerFactory log = new();
            IReadOnlyList<LocalScanResult> results = await new LocalLibraryScanner(log)
                .ScanSeriesAsync(_root, Pattern, ct: TestContext.Current.CancellationToken);

            LocalScanResult result = Assert.Single(results);
            LocalEpisodeScan episode = Assert.Single(result.Episodes);
            Assert.Equal(1, episode.ParsedNumber);
            Assert.Contains(log.Entries, entry => entry.Level == "Warning");
        }

        private string CreateEpisode(string seriesName, string episodeName)
        {
            string episode = Path.Combine(_root, seriesName, episodeName);
            _ = Directory.CreateDirectory(episode);
            File.WriteAllBytes(Path.Combine(episode, "01.mp3"), []);
            return Path.Combine(_root, seriesName);
        }
    }
}

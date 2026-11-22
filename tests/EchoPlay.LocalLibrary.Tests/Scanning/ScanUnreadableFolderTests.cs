using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Tests.Fakes;
using EchoPlay.LocalLibrary.Tests.Infrastructure;

namespace EchoPlay.LocalLibrary.Tests.Scanning
{
    /// <summary>
    /// Prüft den Einlesevorgang, wenn ein Ordner zwar da ist, sich aber nicht lesen lässt,
    /// und die Zuordnung der Phasen im Fortschritt.
    /// </summary>
    /// <remarks>
    /// Der Einlesevorgang läuft über die ganze Mediathek. Ein einzelner Ordner ohne
    /// Leserechte (ein getrenntes Netzlaufwerk, ein Ordner eines anderen Kontos) darf
    /// ihn nicht abbrechen: Der Anwender bekäme sonst nach minutenlangem Warten gar
    /// nichts, obwohl alle übrigen Serien lesbar sind.
    ///
    /// Die Tests arbeiten auf echten Ordnern im Temp-Verzeichnis und nehmen jede Sperre
    /// wieder zurück.
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public sealed class ScanUnreadableFolderTests : IDisposable
    {
        private readonly string _root;

        public ScanUnreadableFolderTests()
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
        public void SeriesFolders_OfAnUnreadableRoot_AreEmpty()
        {
            using LockedFolder locked = LockedFolder.Create(_root);
            LocalLibraryScanner sut = new(new FakeLoggerFactory());

            IReadOnlyList<string> folder = sut.GetSeriesFolders(locked.Path);

            Assert.Empty(folder);
        }

        [Fact]
        public async Task Scan_WithAnUnreadableSeriesFolder_ReturnsWithoutThrowing()
        {
            using LockedFolder locked = LockedFolder.Create(_root, "TKKG");
            LocalLibraryScanner sut = new(new FakeLoggerFactory());

            IReadOnlyList<LocalScanResult> results = await sut.ScanSeriesAsync(
                _root, "{number}", ct: TestContext.Current.CancellationToken);

            // Die Serie liefert keine Folgen, aber der Lauf endet ordentlich.
            Assert.NotNull(results);
        }

        [Fact]
        public async Task Scan_WithAnUnreadableEpisodeFolder_KeepsTheOtherEpisodes()
        {
            string series = Directory.CreateDirectory(Path.Combine(_root, "TKKG")).FullName;
            string good = Directory.CreateDirectory(Path.Combine(series, "001 - Der Fall")).FullName;
            _ = AudioTestFiles.CreateMp3(good);

            using LockedFolder locked = LockedFolder.Create(series, "002 - Nicht lesbar");

            LocalLibraryScanner sut = new(new FakeLoggerFactory());

            IReadOnlyList<LocalScanResult> results = await sut.ScanSeriesAsync(
                _root, "{number} - {title}", ct: TestContext.Current.CancellationToken);

            // Genau darum geht es: Eine unlesbare Folge kostet ihre eigene Zeile, nicht
            // den ganzen Bestand.
            LocalScanResult seriesResult = Assert.Single(results);
            Assert.NotEmpty(seriesResult.Episodes);
        }

        [Fact]
        public async Task Scan_ForAFlatSeries_StillFindsTheTrack()
        {
            string series = Directory.CreateDirectory(Path.Combine(_root, "TKKG")).FullName;
            _ = AudioTestFiles.CreateMp3(series);

            LocalLibraryScanner sut = new(new FakeLoggerFactory());

            IReadOnlyList<LocalScanResult> results = await sut.ScanSeriesAsync(
                _root, "{number} - {title}", ct: TestContext.Current.CancellationToken);

            // Liegen die Dateien ohne Folgenordner direkt in der Serie, darf der
            // Einlesevorgang sie nicht übergehen, sonst fällt eine ganze Serie durch.
            LocalScanResult seriesResult = Assert.Single(results);
            LocalEpisodeScan episode = Assert.Single(seriesResult.Episodes);
            Assert.Equal(1, episode.TrackCount);
        }

        [Fact]
        public async Task Orchestrator_PassesThePhasesOfTheScannerOutwards()
        {
            ReportingScanner scanner = new();
            ScanOrchestrator sut = new(scanner);

            List<ScanProgress> reported = [];
            TaskCompletionSource bothPresent = new(TaskCreationOptions.RunContinuationsAsynchronously);

            SynchronousProgress progress = new(p =>
            {
                lock (reported)
                {
                    reported.Add(p);
                    if (reported.Exists(x => x.Phase == 3) && reported.Exists(x => x.Phase == 4))
                    {
                        _ = bothPresent.TrySetResult();
                    }
                }
            });

            _ = await sut.ScanAsync(_root, "{number}", progress, TestContext.Current.CancellationToken);
            await bothPresent.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            // Der Anwender sieht „Phase 3 von 4". Ohne die Zuordnung stünde dort immer
            // dieselbe Zahl, während der Balken minutenlang läuft.
            lock (reported)
            {
                Assert.Contains(reported, p => p.Phase == 3 && p.PhaseLabel.Length > 0);
                Assert.Contains(reported, p => p.Phase == 4 && p.PhaseLabel.Length > 0);
            }
        }

        [Fact]
        public async Task Orchestrator_WithAnUnreadableRoot_StillRunsTheScan()
        {
            using LockedFolder locked = LockedFolder.Create(_root);
            FakeLocalLibraryScanner scanner = new();
            ScanOrchestrator sut = new(scanner);

            _ = await sut.ScanAsync(locked.Path, "{number}", cancellationToken: TestContext.Current.CancellationToken);

            // Ein unlesbarer Wurzelordner hält den Orchestrator nicht auf; den Fehler meldet
            // der Scanner selbst, und der Balken bleibt ohne Prozentangabe.
            Assert.Equal(1, scanner.ScanCallCount);
        }

        /// <summary>Fortschrittsempfänger ohne eigenen Faden; meldet sofort im Aufrufer.</summary>
        private sealed class SynchronousProgress : IProgress<ScanProgress>
        {
            private readonly Action<ScanProgress> _handler;

            public SynchronousProgress(Action<ScanProgress> handler) => _handler = handler;

            public void Report(ScanProgress value) => _handler(value);
        }

        /// <summary>Einleser, der einen Folgen- und einen Spurenfortschritt meldet.</summary>
        private sealed class ReportingScanner : EchoPlay.LocalLibrary.Abstractions.ILocalLibraryScanner
        {
            public IReadOnlyList<string> GetSeriesFolders(string rootPath) => [];

            public Task<IReadOnlyList<LocalScanResult>> ScanSeriesAsync(
                string rootPath,
                string folderPattern,
                IProgress<ScanProgress>? progress = null,
                IProgress<LocalScanResult>? onSeriesScanned = null,
                CancellationToken ct = default)
            {
                progress?.Report(new ScanProgress { TotalSeries = 3, ProcessedSeries = 1, StatusText = "Serien" });
                progress?.Report(new ScanProgress { TotalFiles = 12, ProcessedFiles = 4, StatusText = "Spuren" });

                return Task.FromResult<IReadOnlyList<LocalScanResult>>([]);
            }
        }
    }
}

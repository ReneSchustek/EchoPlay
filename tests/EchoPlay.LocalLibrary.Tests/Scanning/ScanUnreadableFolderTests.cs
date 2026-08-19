using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Tests.Fakes;
using EchoPlay.LocalLibrary.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Tests.Scanning
{
    /// <summary>
    /// Prüft den Einlesevorgang, wenn ein Ordner zwar da ist, sich aber nicht lesen lässt,
    /// und die Zuordnung der Phasen im Fortschritt.
    /// </summary>
    /// <remarks>
    /// Der Einlesevorgang läuft über die ganze Mediathek. Ein einzelner Ordner ohne
    /// Leserechte — ein getrenntes Netzlaufwerk, ein Ordner eines anderen Kontos — darf
    /// ihn nicht abbrechen: Der Anwender bekäme sonst nach minutenlangem Warten gar
    /// nichts, obwohl alle übrigen Serien lesbar sind.
    ///
    /// Die Tests arbeiten auf echten Ordnern im Temp-Verzeichnis und nehmen jede Sperre
    /// wieder zurück.
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public sealed class ScanUnreadableFolderTests : IDisposable
    {
        private readonly string _wurzel;

        public ScanUnreadableFolderTests()
        {
            _wurzel = Directory.CreateTempSubdirectory("echoplay_scan_").FullName;
        }

        public void Dispose()
        {
            if (Directory.Exists(_wurzel))
            {
                Directory.Delete(_wurzel, recursive: true);
            }
        }

        [Fact]
        public void SeriesFolders_OfAnUnreadableRoot_AreEmpty()
        {
            using LockedFolder gesperrt = LockedFolder.Create(_wurzel);
            LocalLibraryScanner sut = new(new FakeLoggerFactory(), new FakeTagTitleReader());

            IReadOnlyList<string> ordner = sut.GetSeriesFolders(gesperrt.Path);

            Assert.Empty(ordner);
        }

        [Fact]
        public async Task Scan_WithAnUnreadableSeriesFolder_ReturnsWithoutThrowing()
        {
            using LockedFolder gesperrt = LockedFolder.Create(_wurzel, "TKKG");
            LocalLibraryScanner sut = new(new FakeLoggerFactory(), new FakeTagTitleReader());

            IReadOnlyList<LocalScanResult> ergebnisse = await sut.ScanSeriesAsync(
                _wurzel, "{number}", ct: TestContext.Current.CancellationToken);

            // Die Serie liefert keine Folgen — aber der Lauf endet ordentlich.
            Assert.NotNull(ergebnisse);
        }

        [Fact]
        public async Task Scan_WithAnUnreadableEpisodeFolder_KeepsTheOtherEpisodes()
        {
            string serie = Directory.CreateDirectory(Path.Combine(_wurzel, "TKKG")).FullName;
            string gut = Directory.CreateDirectory(Path.Combine(serie, "001 - Der Fall")).FullName;
            _ = AudioTestFiles.CreateMp3(gut);

            using LockedFolder gesperrt = LockedFolder.Create(serie, "002 - Nicht lesbar");

            LocalLibraryScanner sut = new(new FakeLoggerFactory(), new FakeTagTitleReader());

            IReadOnlyList<LocalScanResult> ergebnisse = await sut.ScanSeriesAsync(
                _wurzel, "{number} - {title}", ct: TestContext.Current.CancellationToken);

            // Genau darum geht es: Eine unlesbare Folge kostet ihre eigene Zeile, nicht
            // den ganzen Bestand.
            LocalScanResult serienErgebnis = Assert.Single(ergebnisse);
            Assert.NotEmpty(serienErgebnis.Episodes);
        }

        [Fact]
        public async Task Scan_ForAFlatSeries_StillFindsTheTrack()
        {
            string serie = Directory.CreateDirectory(Path.Combine(_wurzel, "TKKG")).FullName;
            string spur = AudioTestFiles.CreateMp3(serie);

            LocalLibraryScanner sut = new(
                new FakeLoggerFactory(),
                new FakeTagTitleReader(new Dictionary<string, (string, string)>
                {
                    [spur] = ("Kapitel 1", "Folge 001 - Der Fall"),
                }));

            IReadOnlyList<LocalScanResult> ergebnisse = await sut.ScanSeriesAsync(
                _wurzel, "{number} - {title}", ct: TestContext.Current.CancellationToken);

            // Liegen die Dateien ohne Folgenordner direkt in der Serie, darf der
            // Einlesevorgang sie nicht übergehen — sonst fällt eine ganze Serie durch.
            LocalScanResult serienErgebnis = Assert.Single(ergebnisse);
            LocalEpisodeScan folge = Assert.Single(serienErgebnis.Episodes);
            Assert.Equal(1, folge.TrackCount);
        }

        [Fact]
        public async Task Orchestrator_PassesThePhasesOfTheScannerOutwards()
        {
            ReportingScanner scanner = new();
            ScanOrchestrator sut = new(scanner);

            List<ScanProgress> gemeldet = [];
            TaskCompletionSource beideDa = new(TaskCreationOptions.RunContinuationsAsynchronously);

            SynchronousProgress fortschritt = new(p =>
            {
                lock (gemeldet)
                {
                    gemeldet.Add(p);
                    if (gemeldet.Exists(x => x.Phase == 3) && gemeldet.Exists(x => x.Phase == 4))
                    {
                        _ = beideDa.TrySetResult();
                    }
                }
            });

            _ = await sut.ScanAsync(_wurzel, "{number}", fortschritt, TestContext.Current.CancellationToken);
            await beideDa.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            // Der Anwender sieht „Phase 3 von 4". Ohne die Zuordnung stünde dort immer
            // dieselbe Zahl, während der Balken minutenlang läuft.
            lock (gemeldet)
            {
                Assert.Contains(gemeldet, p => p.Phase == 3 && p.PhaseLabel.Length > 0);
                Assert.Contains(gemeldet, p => p.Phase == 4 && p.PhaseLabel.Length > 0);
            }
        }

        [Fact]
        public async Task Orchestrator_WithAnUnreadableRoot_StillRunsTheScan()
        {
            using LockedFolder gesperrt = LockedFolder.Create(_wurzel);
            FakeLocalLibraryScanner scanner = new();
            ScanOrchestrator sut = new(scanner);

            _ = await sut.ScanAsync(gesperrt.Path, "{number}", cancellationToken: TestContext.Current.CancellationToken);

            // Die Vorab-Zählung ist nur für den Fortschrittsbalken da. Scheitert sie,
            // läuft der Einlesevorgang trotzdem — eben ohne Prozentangabe.
            Assert.Equal(1, scanner.ScanCallCount);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        /// <summary>Fortschrittsempfänger ohne eigenen Faden — meldet sofort im Aufrufer.</summary>
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

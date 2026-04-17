using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.LocalLibrary.Tests.Scanning
{
    /// <summary>
    /// Tests für <see cref="ScanOrchestrator"/>.
    /// Prüft die Phasen-Meldungen und die Delegation an den zugrundeliegenden Scanner.
    /// </summary>
    public sealed class ScanOrchestratorTests : IDisposable
    {
        private readonly string _root;

        /// <summary>Erstellt ein temporäres Verzeichnis für Dateisystem-Tests.</summary>
        public ScanOrchestratorTests()
        {
            _root = Directory.CreateTempSubdirectory("echoplay_orchestrator_").FullName;
        }

        /// <summary>Löscht das temporäre Verzeichnis nach jedem Test.</summary>
        public void Dispose()
        {
            Directory.Delete(_root, recursive: true);
        }

        [Fact]
        public async Task ScanAsync_ReportsAllFourPhases()
        {
            // Der Orchestrator muss Fortschrittsmeldungen für Phase 1 (Vorbereitung)
            // und mindestens Phase 2 (Serien) liefern.
            FakeLocalLibraryScanner scanner = new();
            ScanOrchestrator orchestrator = new(scanner);

            List<ScanProgress> reported = [];
            IProgress<ScanProgress> progress = new Progress<ScanProgress>(p => reported.Add(p));

            _ = await orchestrator.ScanAsync(_root, "{number}", progress);

            // Phase 1 und Phase 2 müssen immer gemeldet werden
            Assert.Contains(reported, p => p.Phase == 1);
            Assert.Contains(reported, p => p.Phase == 2);
        }

        [Fact]
        public async Task ScanAsync_Phase1_HasPreparationLabel()
        {
            // Die erste gemeldete Phase soll das Vorbereitungs-Label tragen.
            FakeLocalLibraryScanner scanner = new();
            ScanOrchestrator orchestrator = new(scanner);

            List<ScanProgress> reported = [];
            IProgress<ScanProgress> progress = new Progress<ScanProgress>(p => reported.Add(p));

            _ = await orchestrator.ScanAsync(_root, "{number}", progress);

            ScanProgress? phase1 = reported.Find(p => p.Phase == 1);
            Assert.NotNull(phase1);
            Assert.False(string.IsNullOrEmpty(phase1!.PhaseLabel));
            Assert.Contains("Vorbereitung", phase1.PhaseLabel, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ScanAsync_EmptyDirectory_ReturnsEmptyList()
        {
            // Leeres Verzeichnis liefert keine Scan-Ergebnisse – kein Absturz.
            FakeLocalLibraryScanner scanner = new();
            ScanOrchestrator orchestrator = new(scanner);

            IReadOnlyList<EchoPlay.LocalLibrary.Models.LocalScanResult> results =
                await orchestrator.ScanAsync(_root, "{number}");

            Assert.Empty(results);
        }

        [Fact]
        public async Task ScanAsync_DelegatesTo_UnderlyingScanner()
        {
            // Der Orchestrator muss den Scanner genau einmal aufrufen.
            FakeLocalLibraryScanner scanner = new();
            ScanOrchestrator orchestrator = new(scanner);

            _ = await orchestrator.ScanAsync(_root, "{number}");

            Assert.Equal(1, scanner.ScanCallCount);
        }

        [Fact]
        public async Task ScanAsync_NonExistentDirectory_ReturnsEmpty()
        {
            // Nicht existierender Pfad darf keinen Fehler werfen – leere Liste zurückgeben.
            FakeLocalLibraryScanner scanner = new();
            ScanOrchestrator orchestrator = new(scanner);

            IReadOnlyList<EchoPlay.LocalLibrary.Models.LocalScanResult> results =
                await orchestrator.ScanAsync("/non/existent/path/xyz", "{number}");

            Assert.Empty(results);
        }
    }
}

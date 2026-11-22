using BenchmarkDotNet.Attributes;
using EchoPlay.Benchmarks.Cases.Infrastructure;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;

namespace EchoPlay.Benchmarks.Cases.Benchmarks
{
    /// <summary>
    /// Der Scan der lokalen Mediathek über einen Ordnerbaum in der Größe des Bestands.
    /// </summary>
    /// <remarks>
    /// 94 lokale Serien mit je 32 Folgenordnern ergeben rund 3.000 Folgen wie im Bestand; zwei
    /// Dateien je Folge entsprechen dem Mittel der Spuren. Der Baum liegt im Temp-Verzeichnis;
    /// das Ergebnis hängt deshalb auch am Laufwerk, und Zahlen verschiedener Rechner sind nicht
    /// vergleichbar.
    /// </remarks>
    [MemoryDiagnoser]
    public class ScanBenchmarks
    {
        private LibraryTree? _tree;

        /// <summary>Legt den Ordnerbaum einmal je Messlauf an.</summary>
        [GlobalSetup]
        public void CreateTree() => _tree = LibraryTree.Create(series: LibraryShape.LocalSeriesCount, episodesPerSeries: 32, tracksPerEpisode: 2);

        /// <summary>Löscht den Ordnerbaum.</summary>
        [GlobalCleanup]
        public void DeleteTree() => _tree?.Dispose();

        /// <summary>Ein vollständiger Scan mit Zählung, Mustererkennung und Fortschritt.</summary>
        /// <returns>Anzahl der gefundenen Folgen.</returns>
        [Benchmark]
        public async Task<int> ScanLibrary()
        {
            LibraryTree tree = _tree ?? throw new InvalidOperationException("GlobalSetup ist nicht gelaufen.");
            LocalLibraryScanner scanner = new(new SilentLoggerFactory());

            IReadOnlyList<LocalScanResult> results = await scanner.ScanSeriesAsync(tree.Root, LibraryTree.FolderPattern).ConfigureAwait(false);
            int episodes = results.Sum(r => r.Episodes.Count);

            return episodes * 2 == tree.FileCount
                ? episodes
                : throw new InvalidOperationException($"Erwartet {tree.FileCount / 2} Folgen, gefunden {episodes}.");
        }
    }
}

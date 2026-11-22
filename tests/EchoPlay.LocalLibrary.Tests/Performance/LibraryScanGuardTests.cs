using EchoPlay.Benchmarks.Cases.Infrastructure;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Tests.Fakes;
using System.Diagnostics;

namespace EchoPlay.LocalLibrary.Tests.Performance
{
    /// <summary>
    /// Grober Wächter für den Scan der lokalen Mediathek. Er schlägt erst an, wenn der Scan um eine
    /// Größenordnung langsamer wird; feine Messungen macht das Messprojekt.
    /// </summary>
    public sealed class LibraryScanGuardTests
    {
        // Ein Fünftel des Bestands (20 Serien zu je 30 Folgen mit zwei Dateien), damit der Wächter
        // den Testlauf nicht spürbar verlängert.
        private const int Series = 20;
        private const int EpisodesPerSeries = 30;
        private const int TracksPerEpisode = 2;

        // Messlauf 20.11.2026 (BenchmarkDotNet, SSD): 339 ms für 6.016 Dateien, für diesen Baum also
        // etwa 70 ms. 2 s lassen Luft für langsame Laufwerke und den parallelen Testlauf und fangen
        // trotzdem ein Abrutschen um eine Größenordnung, etwa ein Lesen der Tags je Datei.
        private static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);

        [Fact]
        public async Task Scan_OfAFifthOfTheLibrary_StaysWellUnderTheLimit()
        {
            using LibraryTree tree = LibraryTree.Create(Series, EpisodesPerSeries, TracksPerEpisode);
            LocalLibraryScanner scanner = new(new FakeLoggerFactory());

            Stopwatch watch = Stopwatch.StartNew();
            IReadOnlyList<LocalScanResult> results = await scanner.ScanSeriesAsync(
                tree.Root, LibraryTree.FolderPattern, ct: TestContext.Current.CancellationToken);
            watch.Stop();

            Assert.Equal(Series * EpisodesPerSeries, results.Sum(r => r.Episodes.Count));
            Assert.True(watch.Elapsed < Limit, $"Scan über {tree.FileCount} Dateien: {watch.Elapsed.TotalMilliseconds:F0} ms, Grenze {Limit.TotalMilliseconds:F0} ms.");
        }
    }
}

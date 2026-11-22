using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Data.Replication;

namespace EchoPlay.Benchmarks.Cases.Benchmarks
{
    /// <summary>
    /// Packen und Auspacken der Mediathek bei gleicher Dateizahl und verschieden großen Dateien.
    /// </summary>
    /// <remarks>
    /// Zwei Fragen: wie schnell es geht und ob der Speicher mit der Größe wächst. Bleibt die
    /// zugewiesene Menge bei zehnmal größeren Dateien gleich, ist belegt, dass gestreamt wird und nie
    /// eine ganze Datei im Speicher liegt. Die Dateien sind Zufallsbytes wie echte Audiodaten, die sich
    /// nicht weiter packen lassen. Gemessen wird mit drei Durchläufen ohne Aufwärmen, weil jeder
    /// Gigabyte schreibt; die Zeit gilt für die Platte, auf der sie entstand.
    /// </remarks>
    [SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 0, iterationCount: 3)]
    [MemoryDiagnoser]
    public class LibraryArchiveBenchmarks
    {
        private const int FileCount = 200;

        private string _directory = string.Empty;
        private string _library = string.Empty;
        private string _dataPackage = string.Empty;
        private string _archive = string.Empty;

        /// <summary>Größe jeder Datei in Megabyte.</summary>
        [Params(1, 10)]
        public int FileSizeMegabytes { get; set; }

        /// <summary>Legt die Mediathek und ein Archiv zum Auspacken einmal je Messlauf an.</summary>
        [GlobalSetup]
        public void CreateLibrary()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"echoplay_bench_library_{Guid.NewGuid():N}");
            _library = Path.Combine(_directory, "Mediathek");
            _dataPackage = Path.Combine(_directory, "daten" + PackageFormat.FileExtension);
            _archive = Path.Combine(_directory, "vorlage" + LibraryArchiveFormat.FileExtension);

            byte[] content = RandomNumberGenerator.GetBytes(FileSizeMegabytes * 1024 * 1024);
            for (int i = 0; i < FileCount; i++)
            {
                string folder = Path.Combine(_library, $"Serie {i / 20:D2}", $"{i:D3} – Folge");
                _ = Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "01.mp3"), content);
            }

            File.WriteAllBytes(_dataPackage, RandomNumberGenerator.GetBytes(1024 * 1024));
            _ = PackAsync(_archive).GetAwaiter().GetResult();
        }

        /// <summary>Räumt alles weg.</summary>
        [GlobalCleanup]
        public void DeleteLibrary() => Directory.Delete(_directory, recursive: true);

        /// <summary>Die Mediathek packen, wie „Mediathek packen“ es tut.</summary>
        /// <returns>Die Zahl der gepackten Dateien.</returns>
        [Benchmark]
        public async Task<int> Pack()
        {
            string target = Path.Combine(_directory, $"messung-{Guid.NewGuid():N}{LibraryArchiveFormat.FileExtension}");
            try
            {
                return await PackAsync(target).ConfigureAwait(false);
            }
            finally
            {
                File.Delete(target);
            }
        }

        /// <summary>Das Archiv in einen leeren Ordner auspacken, wie „Mediathek auspacken“ es tut.</summary>
        /// <returns>Die Zahl der ausgepackten Dateien.</returns>
        [Benchmark]
        public async Task<int> Unpack()
        {
            string target = Path.Combine(_directory, $"ausgepackt-{Guid.NewGuid():N}");
            string data = Path.Combine(_directory, $"daten-{Guid.NewGuid():N}{PackageFormat.FileExtension}");
            try
            {
                LibraryExtraction extraction = await LibraryArchiveExtractor
                    .ExtractAsync(_archive, target, data, null, CancellationToken.None).ConfigureAwait(false);
                return extraction.Manifest.Files.Count == FileCount
                    ? extraction.Manifest.Files.Count
                    : throw new InvalidOperationException($"Erwartet {FileCount} Dateien, ausgepackt {extraction.Manifest.Files.Count}.");
            }
            finally
            {
                Directory.Delete(target, recursive: true);
                File.Delete(data);
            }
        }

        private async Task<int> PackAsync(string target)
        {
            LibraryManifest manifest = await LibraryArchiveWriter
                .WriteAsync(_library, _dataPackage, target, TimeProvider.System, null, CancellationToken.None).ConfigureAwait(false);
            return manifest.Files.Count;
        }
    }
}

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using EchoPlay.Benchmarks.Cases.Infrastructure;
using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Data.Context;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Replication;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Benchmarks.Cases.Benchmarks
{
    /// <summary>
    /// Export und Prüfen eines Pakets bei einer Datenbank in Produktivgröße, samt Covern.
    /// </summary>
    /// <remarks>
    /// Jeder Durchlauf schreibt über ein Gigabyte. Gemessen wird deshalb mit drei Durchläufen ohne
    /// Aufwärmen (<see cref="RunStrategy.Monitoring"/>): Es geht um die Größenordnung, die der Nutzer
    /// erlebt, nicht um Mikrosekunden. Die Zeit hängt an der Platte; die Zahl gilt für den Rechner, auf
    /// dem sie entstand.
    /// </remarks>
    [SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 0, iterationCount: 3)]
    [MemoryDiagnoser]
    public class PackageBenchmarks
    {
        private string _directory = string.Empty;
        private string _databasePath = string.Empty;
        private string _package = string.Empty;
        private string _target = string.Empty;

        /// <summary>Legt Bestand und ein Paket für das Prüfen einmal je Messlauf an.</summary>
        [GlobalSetup]
        public void CreateDatabase()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"echoplay_bench_package_{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(_directory);
            _databasePath = Path.Combine(_directory, "echoplay.db");
            _package = Path.Combine(_directory, "vorlage" + PackageFormat.FileExtension);
            _target = Path.Combine(_directory, "ziel" + PackageFormat.FileExtension);

            using (EchoPlayDbContext context = CreateContext())
            {
                context.Database.Migrate();
                _ = LibrarySeed.Fill(context);
                _ = CoverSeed.Fill(context, CoverSeed.CreateImages());
            }

            _ = ExportAsync(_package).GetAwaiter().GetResult();
        }

        /// <summary>Räumt Bestand und Pakete weg.</summary>
        [GlobalCleanup]
        public void DeleteDatabase()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, recursive: true);
        }

        /// <summary>Das Paket schreiben, wie „Daten exportieren“ es tut.</summary>
        /// <returns>Die Größe des Pakets.</returns>
        [Benchmark]
        public async Task<long> Export()
        {
            long length = await ExportAsync(_target).ConfigureAwait(false);
            return length > 1_000_000_000 ? length : throw new InvalidOperationException($"Paket nur {length} Byte groß: Der Bestand hat nicht die Produktivgröße.");
        }

        /// <summary>Ein Paket prüfen und entpacken, wie „Daten einspielen“ es vor dem Neustart tut.</summary>
        /// <returns>Die Zahl der Folgen im Paket.</returns>
        [Benchmark]
        public async Task<int> Stage()
        {
            using EchoPlayDbContext context = CreateContext();
            StagedPackage staged = await new PackageStager(context, new DatabaseFileLocation(_databasePath))
                .StageAsync(_package, CancellationToken.None).ConfigureAwait(false);
            File.Delete(staged.StagedDatabase);
            return staged.Preview.PackageEpisodes == LibraryShape.EpisodeCount
                ? staged.Preview.PackageEpisodes
                : throw new InvalidOperationException($"Erwartet {LibraryShape.EpisodeCount} Folgen im Paket, gefunden {staged.Preview.PackageEpisodes}.");
        }

        private async Task<long> ExportAsync(string target)
        {
            using EchoPlayDbContext context = CreateContext();
            PackageExporter exporter = new(context, new DatabaseFileLocation(_databasePath), new FileDeviceIdentity(_directory));
            return await exporter.ExportAsync(target, "messung", null, null, CancellationToken.None).ConfigureAwait(false);
        }

        private EchoPlayDbContext CreateContext() => new(new DbContextOptionsBuilder<EchoPlayDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options);
    }
}

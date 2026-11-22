using BenchmarkDotNet.Attributes;
using EchoPlay.Benchmarks.Cases.Infrastructure;
using EchoPlay.Data.Context;
using EchoPlay.Data.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Benchmarks.Cases.Benchmarks
{
    /// <summary>
    /// Der Datenbankstart, wie die Anwendung ihn über <see cref="DatabaseInitializer"/> nimmt: die
    /// ganze Kette der Migrationen auf einer leeren Datei und die Prüfung beim gewöhnlichen Start auf
    /// einer Datei im Produktivbestand.
    /// </summary>
    /// <remarks>
    /// Die erste Messung ist der erste Start nach der Installation, einmal im Leben einer Datei. Die
    /// zweite läuft bei jedem Start, auch wenn nichts zu migrieren ist, und hält das Startbild auf.
    /// Eine einzelne neue Migration auf großem Bestand fehlt: Dafür müsste die Datei im Stand vor
    /// dieser Migration befüllt werden, also mit dem alten Schema. Der Messbestand entsteht aber aus
    /// den heutigen Entitäten. Das bleibt die Probe gegen eine Kopie der echten Datenbank, die vor
    /// jeder Migration ohnehin ansteht.
    /// </remarks>
    [MemoryDiagnoser]
    public class MigrationBenchmarks
    {
        private string _emptyDirectory = string.Empty;
        private string _productionPath = string.Empty;
        private int _migrationCount;

        /// <summary>Legt die Datei im Produktivbestand einmal je Messlauf an.</summary>
        [GlobalSetup]
        public void CreateProductionFile()
        {
            _productionPath = Path.Combine(Path.GetTempPath(), $"echoplay_bench_migration_{Guid.NewGuid():N}.db");
            using EchoPlayDbContext context = CreateContext(_productionPath);
            context.Database.Migrate();
            _migrationCount = context.Database.GetMigrations().Count();
            _ = LibrarySeed.Fill(context);

            // Die Messung soll den gewöhnlichen Start treffen, bei dem nichts aussteht.
            int pending = context.Database.GetPendingMigrations().Count();
            if (pending != 0)
            {
                throw new InvalidOperationException($"Erwartet keine ausstehende Migration, gefunden {pending}.");
            }
        }

        /// <summary>Löscht die Datei im Produktivbestand.</summary>
        [GlobalCleanup]
        public void DeleteProductionFile()
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_productionPath);
        }

        /// <summary>Ein eigenes Verzeichnis je Durchgang, denn der erste Start legt dort auch eine Sicherung an.</summary>
        [IterationSetup(Target = nameof(FullChainOnEmptyFile))]
        public void CreateEmptyDirectory()
        {
            _emptyDirectory = Path.Combine(Path.GetTempPath(), $"echoplay_bench_empty_{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(_emptyDirectory);
        }

        /// <summary>Prüft, dass die Kette vollständig gelaufen ist, und räumt das Verzeichnis ab.</summary>
        [IterationCleanup(Target = nameof(FullChainOnEmptyFile))]
        public void CheckAndDeleteEmptyDirectory()
        {
            string path = EmptyFilePath;
            using (EchoPlayDbContext context = CreateContext(path))
            {
                int applied = context.Database.GetAppliedMigrations().Count();
                if (applied != _migrationCount)
                {
                    throw new InvalidOperationException($"Erwartet {_migrationCount} Migrationen, angewendet {applied}.");
                }
            }

            SqliteConnection.ClearAllPools();
            Directory.Delete(_emptyDirectory, recursive: true);
        }

        /// <summary>Der erste Start: alle Migrationen auf einer leeren Datei.</summary>
        /// <returns>Der Task ist abgeschlossen, wenn das Schema steht.</returns>
        [Benchmark]
        [InvocationCount(1)]
        public async Task FullChainOnEmptyFile()
        {
            string path = EmptyFilePath;
            using EchoPlayDbContext context = CreateContext(path);
            DatabaseInitializer initializer = new(context, new DatabaseFileLocation(path));
            await initializer.InitializeAsync().ConfigureAwait(false);
        }

        /// <summary>Jeder weitere Start: nichts ausstehend, die Prüfung läuft trotzdem.</summary>
        /// <returns>Die Zahl der angewendeten Migrationen.</returns>
        [Benchmark]
        public async Task<int> StartCheckOnProductionFile()
        {
            using EchoPlayDbContext context = CreateContext(_productionPath);
            DatabaseInitializer initializer = new(context, new DatabaseFileLocation(_productionPath));
            await initializer.InitializeAsync().ConfigureAwait(false);
            return _migrationCount;
        }

        private string EmptyFilePath => Path.Combine(_emptyDirectory, "echoplay.db");

        private static EchoPlayDbContext CreateContext(string path)
        {
            DbContextOptions<EchoPlayDbContext> options = new DbContextOptionsBuilder<EchoPlayDbContext>()
                .UseSqlite($"Data Source={path}")
                .AddInterceptors(new SqlitePragmaInterceptor())
                .Options;
            return new EchoPlayDbContext(options);
        }
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Data.Context;
using EchoPlay.Logger.Models;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Replication;
using EchoPlay.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Replication
{
    /// <summary>
    /// Randfälle von Prüfen, Einspielen und Abschluss: präparierte Pakete, fehlende Teile, alte
    /// Bestände ohne Einstellungen, Wertumwandlung.
    /// </summary>
    public sealed class PackageEdgeCaseTests : IDisposable
    {
        private const string CurrentMigration = "20261120130408_AddChangeStamp";

        private static readonly EchoPlay.Logger.Abstractions.ILoggerFactory Loggers =
            new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"echoplay_edge_{Guid.NewGuid():N}");
        private readonly string _install;

        public PackageEdgeCaseTests()
        {
            _install = Path.Combine(_root, "ziel");
            _ = Directory.CreateDirectory(_install);
        }

        [Fact]
        public async Task NotAZipFile_IsRejected()
        {
            string package = Path.Combine(_root, "kaputt.echoplay");
            await File.WriteAllTextAsync(package, "kein Archiv", TestContext.Current.CancellationToken);

            PackageRejectedException ex = await StageRejectedAsync(package);
            Assert.Contains("kein Paket", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ManifestNotUtf8_IsRejected()
        {
            string package = Zip(("manifest.json", [0xFF, 0xFE, 0x00]), (PackageFormat.DatabaseEntry, [1]));

            PackageRejectedException ex = await StageRejectedAsync(package);
            Assert.Contains("UTF-8", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task UnknownOlderMigration_IsRejected()
        {
            byte[] database = SqliteBytes();
            string package = Package(database, "20200101000000_Unbekannt");

            PackageRejectedException ex = await StageRejectedAsync(package);
            Assert.Contains("nicht kennt", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DatabaseThatIsNoSqliteFile_IsRejected()
        {
            byte[] notSqlite = Encoding.UTF8.GetBytes("das ist keine Datenbank, nur Text mit genug Länge");
            string package = Package(notSqlite, CurrentMigration);

            PackageRejectedException ex = await StageRejectedAsync(package);
            Assert.Contains("SQLite", ex.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(_install, PackageStager.StagedFileName)));
        }

        [Fact]
        public async Task EntryLongerThanDeclared_IsRejected()
        {
            byte[] database = SqliteBytes();
            string package = Package(database, CurrentMigration, declaredLength: database.Length - 1);

            PackageRejectedException ex = await StageRejectedAsync(package);
            Assert.Contains("länger", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task EntryShorterThanDeclared_IsRejected()
        {
            byte[] database = SqliteBytes();
            string package = Package(database, CurrentMigration, declaredLength: database.Length + 1);

            PackageRejectedException ex = await StageRejectedAsync(package);
            Assert.Contains("kürzer", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CredentialsInManifestButMissing_AreRejected()
        {
            byte[] database = SqliteBytes();
            string package = Package(database, CurrentMigration, credentials: [1, 2, 3], writeCredentials: false);

            PackageRejectedException ex = await StageRejectedAsync(package);
            Assert.Contains("fehlt", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CredentialsWithWrongHash_AreRejected()
        {
            byte[] database = SqliteBytes();
            string package = Package(database, CurrentMigration, credentials: [1, 2, 3], credentialsHash: new string('0', 64));

            PackageRejectedException ex = await StageRejectedAsync(package);
            Assert.Contains("Prüfsumme", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CredentialsInPackage_AreStagedUnchanged()
        {
            byte[] database = SqliteBytes();
            string package = Package(database, CurrentMigration, credentials: [7, 8, 9]);
            await using EchoPlayDbContext context = await OpenMigratedAsync();

            StagedPackage staged = await new PackageStager(context, Location).StageAsync(package, TestContext.Current.CancellationToken);

            Assert.Equal(new byte[] { 7, 8, 9 }, staged.SealedCredentials.ToArray());
        }

        [Fact]
        public async Task Export_ToARelativeTarget_IsRefused()
        {
            await using EchoPlayDbContext context = await OpenMigratedAsync();
            PackageExporter exporter = new(context, Location, new FileDeviceIdentity(_install));

            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => exporter.ExportAsync("relativ.echoplay", "1.0.0", null, null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Export_WithCredentials_WritesTheSealedEntry()
        {
            await using EchoPlayDbContext context = await OpenMigratedAsync();
            PackageExporter exporter = new(context, Location, new FileDeviceIdentity(_install));
            string target = Path.Combine(_root, "mit-zugang" + PackageFormat.FileExtension);
            List<double> reported = [];

            _ = await exporter.ExportAsync(target, "1.0.0", [4, 5, 6], new SyncProgress(reported), TestContext.Current.CancellationToken);

            await using ZipArchive archive = await ZipFile.OpenReadAsync(target, TestContext.Current.CancellationToken);
            Assert.NotNull(archive.GetEntry(PackageFormat.CredentialsEntry));
            Assert.Equal(1, reported[^1]);
            Assert.False(File.Exists(target + ".partial"));
        }

        [Fact]
        public async Task Apply_OnABareDatabaseWithoutTables_ReplacesItAndKeepsNothing()
        {
            // Eine leere SQLite-Datei: keine Einstellungen, keine Zugangsdaten, nichts zu erhalten.
            string databasePath = Location.FilePath;
            await using (SqliteConnection connection = new($"Data Source={databasePath}"))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE \"Fremd\" (\"X\" INTEGER);";
                _ = await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            SqliteConnection.ClearAllPools();
            await File.WriteAllBytesAsync(Path.Combine(_install, PackageStager.StagedFileName), SqliteBytes(), TestContext.Current.CancellationToken);
            new PendingImport(null, Guid.NewGuid()).Write(_install);

            ImportCarryOver? carry = await PendingImportApplier.ApplyAsync(databasePath, TestContext.Current.CancellationToken);

            Assert.NotNull(carry);
            Assert.False(carry!.HadSettings);
            Assert.Empty(carry.Secrets);
            Assert.NotNull(carry.BackupPath);
        }

        [Fact]
        public async Task Apply_NamesTheBackupAfterTheClock()
        {
            string databasePath = Location.FilePath;
            await File.WriteAllBytesAsync(databasePath, SqliteBytes(), TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(_install, PackageStager.StagedFileName), SqliteBytes(), TestContext.Current.CancellationToken);
            new PendingImport(null, Guid.NewGuid()).Write(_install);
            Microsoft.Extensions.Time.Testing.FakeTimeProvider clock = new(new DateTimeOffset(2031, 5, 17, 8, 9, 10, TimeSpan.Zero));

            ImportCarryOver? carry = await PendingImportApplier.ApplyAsync(databasePath, clock, TestContext.Current.CancellationToken);

            Assert.EndsWith("echoplay.db.before-import-20310517-080910", carry?.BackupPath, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Finish_WithoutSettingsInTheOldInstall_KeepsThePackagesSettings()
        {
            await using EchoPlayDbContext context = await OpenMigratedAsync();
            _ = context.AppSettings.Add(new AppSettings { LocalLibraryRootPath = @"D:\Paket", Volume = 0.4 });
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            ImportCarryOver carry = new(new PendingImport(@"D:\Paket", Guid.NewGuid()), HadSettings: false,
                new Dictionary<string, object?> { ["Volume"] = 0.9 }, [], BackupPath: null);

            ImportFinished finished = await Finisher(context).FinishAsync(carry, TestContext.Current.CancellationToken);

            Assert.Equal(0, finished.RestoredSettings);
            Assert.Equal(0.4, (await context.AppSettings.SingleAsync(TestContext.Current.CancellationToken)).Volume);
        }

        [Fact]
        public async Task Finish_DeviceWithoutRoot_KeepsThePackageRoot_AndRebasesNothing()
        {
            await using EchoPlayDbContext context = await OpenMigratedAsync();
            _ = context.AppSettings.Add(new AppSettings { LocalLibraryRootPath = @"D:\Paket" });
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            ImportCarryOver carry = new(new PendingImport(@"D:\Paket", Guid.NewGuid()), HadSettings: true,
                new Dictionary<string, object?> { ["LocalLibraryRootPath"] = null, ["MinimumLogLevel"] = 3L, ["LastAppStart"] = "2026-11-20 12:00:00" },
                [], BackupPath: "x");

            ImportFinished finished = await Finisher(context).FinishAsync(carry, TestContext.Current.CancellationToken);

            AppSettings settings = await context.AppSettings.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(@"D:\Paket", settings.LocalLibraryRootPath);
            Assert.Equal(LogLevel.Warning, settings.MinimumLogLevel);
            Assert.Equal(0, finished.Rebased);
            Assert.Equal(2, finished.RestoredSettings);
        }

        [Theory]
        [InlineData("2026-11-20 12:00:00", typeof(DateTime?))]
        [InlineData(4L, typeof(LogLevel))]
        public void TryConvert_DateAndEnum_Work(object raw, Type type)
        {
            Assert.True(ImportFinisher.TryConvert(raw, type, out object? value));
            Assert.NotNull(value);
        }

        [Theory]
        [InlineData("kein Datum", typeof(DateTime))]
        [InlineData("abc", typeof(int))]
        [InlineData(long.MaxValue, typeof(int))]
        [InlineData(1L, typeof(Guid))]
        public void TryConvert_Unconvertible_IsRefused(object raw, Type type)
        {
            Assert.False(ImportFinisher.TryConvert(raw, type, out _));
        }

        [Fact]
        public void TryConvert_BlobIntoBool_IsRefused()
        {
            Assert.False(ImportFinisher.TryConvert(new byte[] { 1 }, typeof(bool), out _));
        }

        [Fact]
        public async Task PendingImport_Unreadable_IsTreatedAsAbsent()
        {
            await File.WriteAllTextAsync(Path.Combine(_install, PendingImport.FileName), "{ kaputt", TestContext.Current.CancellationToken);

            Assert.Null(PendingImport.TryRead(_install));
            Assert.Null(PendingImport.TryRead(Path.Combine(_root, "gibt-es-nicht")));
        }

        [Fact]
        public void SpaceCheck_TooLittleSpace_NamesBothNumbers()
        {
            InsufficientSpaceException ex = Assert.Throws<InsufficientSpaceException>(() => SpaceCheck.Require(_root, long.MaxValue));

            Assert.Equal(long.MaxValue, ex.RequiredBytes);
            Assert.True(ex.AvailableBytes > 0);
        }

        [Fact]
        public void SpaceCheck_NetworkShare_IsNotChecked()
        {
            SpaceCheck.Require(@"\\server\freigabe\ordner", long.MaxValue);
        }

        [Fact]
        public void Exceptions_HaveTheStandardConstructors()
        {
            InvalidOperationException inner = new("innen");

            Assert.Equal("x", new InsufficientSpaceException("x").Message);
            Assert.Same(inner, new InsufficientSpaceException("x", inner).InnerException);
            Assert.NotNull(new InsufficientSpaceException().Message);
            Assert.Equal("y", new PackageRejectedException("y").Message);
            Assert.Same(inner, new PackageRejectedException("y", inner).InnerException);
            Assert.NotNull(new PackageRejectedException().Message);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private DatabaseFileLocation Location => new(Path.Combine(_install, "echoplay.db"));

        private static ImportFinisher Finisher(EchoPlayDbContext context) =>
            new(context, new LibraryRootRebaser(context, Loggers), new SecureSettingsDataService(context, Loggers), Loggers);

        private async Task<EchoPlayDbContext> OpenMigratedAsync()
        {
            EchoPlayDbContext context = new(new DbContextOptionsBuilder<EchoPlayDbContext>()
                .UseSqlite($"Data Source={Location.FilePath}")
                .Options);
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            return context;
        }

        private async Task<PackageRejectedException> StageRejectedAsync(string package)
        {
            await using EchoPlayDbContext context = await OpenMigratedAsync();
            PackageStager stager = new(context, Location);
            return await Assert.ThrowsAsync<PackageRejectedException>(() => stager.StageAsync(package, TestContext.Current.CancellationToken));
        }

        /// <summary>Eine kleine, gültige SQLite-Datei als Bytes.</summary>
        private byte[] SqliteBytes()
        {
            string path = Path.Combine(_root, $"klein-{Guid.NewGuid():N}.db");
            using (SqliteConnection connection = new($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE "Series" ("IsDeleted" INTEGER NOT NULL);
                    CREATE TABLE "Episodes" ("IsDeleted" INTEGER NOT NULL);
                    CREATE TABLE "PlaybackStates" ("IsDeleted" INTEGER NOT NULL, "IsCompleted" INTEGER NOT NULL);
                    """;
                _ = command.ExecuteNonQuery();
            }

            return File.ReadAllBytes(path);
        }

        private string Package(
            byte[] database,
            string migration,
            long? declaredLength = null,
            byte[]? credentials = null,
            bool writeCredentials = true,
            string? credentialsHash = null)
        {
            List<PackageEntry> entries = [new(PackageFormat.DatabaseEntry, declaredLength ?? database.Length, Hex(database))];
            List<(string, byte[])> files = [(PackageFormat.DatabaseEntry, database)];
            if (credentials is not null)
            {
                entries.Add(new(PackageFormat.CredentialsEntry, credentials.Length, credentialsHash ?? Hex(credentials)));
                if (writeCredentials)
                {
                    files.Add((PackageFormat.CredentialsEntry, credentials));
                }
            }

            PackageManifest manifest = new(1, "1.0.0", migration, Guid.NewGuid(), DateTimeOffset.UtcNow, null, entries);
            files.Add((PackageFormat.ManifestEntry, Encoding.UTF8.GetBytes(manifest.ToJson())));
            return Zip([.. files]);
        }

        private string Zip(params (string Name, byte[] Data)[] files)
        {
            string path = Path.Combine(_root, $"paket-{Guid.NewGuid():N}{PackageFormat.FileExtension}");
            using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach ((string name, byte[] data) in files)
            {
                using Stream stream = archive.CreateEntry(name).Open();
                stream.Write(data);
            }

            return path;
        }

        private static string Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

        /// <summary>Ein Fortschritt, der ohne Synchronisierungskontext sofort mitschreibt.</summary>
        private sealed class SyncProgress(List<double> values) : IProgress<double>
        {
            public void Report(double value) => values.Add(value);
        }
    }
}

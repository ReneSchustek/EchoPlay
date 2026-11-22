using System.IO.Compression;
using System.Security.Cryptography;
using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Replication;
using EchoPlay.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EchoPlay.Data.Tests.Replication
{
    /// <summary>
    /// Export, Prüfen, Einspielen und Abschluss über echte Dateien, wie die Anwendung sie beim
    /// Neustart nacheinander ausführt.
    /// </summary>
    public sealed class PackageRoundTripTests : IDisposable
    {
        private const string SourceRoot = @"D:\Mp3\Hörspiele";
        private const string TargetRoot = @"F:\Medien\Hörspiele";

        private static readonly EchoPlay.Logger.Abstractions.ILoggerFactory Loggers =
            new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"echoplay_transfer_{Guid.NewGuid():N}");

        public PackageRoundTripTests() => _ = Directory.CreateDirectory(_root);

        [Fact]
        public async Task Export_ThenImportOnAnotherInstall_KeepsTheLibraryAndTheTargetsOwnDeviceData()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            string source = await CreateInstallAsync("quelle", SourceRoot, volume: 0.3, secret: [1, 2, 3], withLibrary: true);
            string target = await CreateInstallAsync("ziel", TargetRoot, volume: 0.8, secret: [9, 9], withLibrary: false);

            string package = await ExportAsync(source, ct);
            await StageAndScheduleAsync(target, package, ct);
            ImportFinished finished = await ApplyAndFinishAsync(target, ct);

            await using EchoPlayDbContext context = Open(target);
            Series series = await context.Series.SingleAsync(ct);
            Assert.Equal(TargetRoot + @"\TKKG", series.LocalFolderPath);
            Assert.Equal(1, await context.PlaybackStates.CountAsync(p => p.IsCompleted, ct));
            AppSettings settings = await context.AppSettings.SingleAsync(ct);
            Assert.Equal(0.8, settings.Volume);
            Assert.Equal(TargetRoot, settings.LocalLibraryRootPath);
            Assert.Equal(new byte[] { 9, 9 }, (await context.SecureSettings.SingleAsync(ct)).EncryptedValue);
            Assert.True(finished.Rebased >= 3);
            _ = Assert.Single(Directory.GetFiles(target, "echoplay.db.before-import-*"));
            Assert.Empty(await context.Database.GetPendingMigrationsAsync(ct));
        }

        [Fact]
        public async Task ImportWithUnpackedLibrary_MakesTheUnpackFolderTheRoot()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            const string unpackedRoot = @"G:\Umzug\Hörspiele";
            string source = await CreateInstallAsync("quelle", SourceRoot, volume: 0.3, secret: [1], withLibrary: true);
            string target = await CreateInstallAsync("ziel", TargetRoot, volume: 0.8, secret: [9], withLibrary: false);

            string package = await ExportAsync(source, ct);
            await StageAndScheduleAsync(target, package, ct, libraryRootOverride: unpackedRoot);
            ImportFinished finished = await ApplyAndFinishAsync(target, ct);

            await using EchoPlayDbContext context = Open(target);
            Assert.Equal(unpackedRoot + @"\TKKG", (await context.Series.SingleAsync(ct)).LocalFolderPath);
            Assert.Equal(unpackedRoot + @"\TKKG\001\01.mp3", (await context.LocalTracks.SingleAsync(ct)).FilePath);
            AppSettings settings = await context.AppSettings.SingleAsync(ct);
            Assert.Equal(unpackedRoot, settings.LocalLibraryRootPath);
            Assert.Equal(0.8, settings.Volume);
            Assert.True(finished.Rebased >= 3);
        }

        [Fact]
        public async Task PackageFromAnOlderSchema_IsMigratedAfterImport()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            string source = Path.Combine(_root, "alt");
            _ = Directory.CreateDirectory(source);
            const string olderMigration = "20260901133740_DoppelteFolgenEntfernen";
            await using (EchoPlayDbContext old = Open(source))
            {
                await old.GetService<IMigrator>().MigrateAsync(olderMigration, ct);
            }

            string package = WriteManualPackage(Path.Combine(source, "echoplay.db"), olderMigration);
            string target = await CreateInstallAsync("ziel", TargetRoot, volume: 0.5, secret: [1], withLibrary: false);

            await StageAndScheduleAsync(target, package, ct);
            _ = await ApplyAndFinishAsync(target, ct);

            await using EchoPlayDbContext context = Open(target);
            Assert.Empty(await context.Database.GetPendingMigrationsAsync(ct));
            Assert.Contains("20261120130408_AddChangeStamp", await context.Database.GetAppliedMigrationsAsync(ct));
        }

        [Fact]
        public async Task PackageFromANewerVersion_IsRejected()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            string source = await CreateInstallAsync("quelle", SourceRoot, volume: 0.3, secret: [1], withLibrary: true);
            string package = WriteManualPackage(Path.Combine(source, "echoplay.db"), "29991231000000_AusDerZukunft");
            string target = await CreateInstallAsync("ziel", TargetRoot, volume: 0.5, secret: [1], withLibrary: false);

            await using EchoPlayDbContext context = Open(target);
            PackageStager stager = new(context, new DatabaseFileLocation(Path.Combine(target, "echoplay.db")));

            PackageRejectedException ex = await Assert.ThrowsAsync<PackageRejectedException>(() => stager.StageAsync(package, ct));
            Assert.Contains("neueren Fassung", ex.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(target, PackageStager.StagedFileName)));
        }

        [Fact]
        public async Task DamagedPackage_IsRejected_AndLeavesNothingBehind()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            string source = await CreateInstallAsync("quelle", SourceRoot, volume: 0.3, secret: [1], withLibrary: true);
            string package = WriteManualPackage(Path.Combine(source, "echoplay.db"), "20261120130408_AddChangeStamp", wrongHash: true);
            string target = await CreateInstallAsync("ziel", TargetRoot, volume: 0.5, secret: [1], withLibrary: false);

            await using EchoPlayDbContext context = Open(target);
            PackageStager stager = new(context, new DatabaseFileLocation(Path.Combine(target, "echoplay.db")));

            _ = await Assert.ThrowsAsync<PackageRejectedException>(() => stager.StageAsync(package, ct));
            Assert.False(File.Exists(Path.Combine(target, PackageStager.StagedFileName)));
        }

        [Fact]
        public async Task ApplyWithoutStagedDatabase_DiscardsTheMarker()
        {
            string target = Path.Combine(_root, "leer");
            _ = Directory.CreateDirectory(target);
            new PendingImport(SourceRoot, Guid.NewGuid()).Write(target);

            ImportCarryOver? carry = await PendingImportApplier.ApplyAsync(Path.Combine(target, "echoplay.db"), TestContext.Current.CancellationToken);

            Assert.Null(carry);
            Assert.False(File.Exists(Path.Combine(target, PendingImport.FileName)));
        }

        [Theory]
        [InlineData(1L, typeof(bool), true)]
        [InlineData(0L, typeof(bool), false)]
        [InlineData(30L, typeof(int), 30)]
        [InlineData(0.25, typeof(double), 0.25)]
        [InlineData("D:\\x", typeof(string), "D:\\x")]
        public void TryConvert_SqliteValues_FitThePropertyTypes(object raw, Type type, object expected)
        {
            Assert.True(ImportFinisher.TryConvert(raw, type, out object? value));
            Assert.Equal(expected, value);
        }

        [Fact]
        public void TryConvert_NullIntoNonNullableValue_IsRefused()
        {
            Assert.False(ImportFinisher.TryConvert(null, typeof(int), out _));
            Assert.True(ImportFinisher.TryConvert(null, typeof(DateTime?), out _));
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

        private async Task<string> CreateInstallAsync(string name, string root, double volume, byte[] secret, bool withLibrary)
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            string directory = Path.Combine(_root, name);
            _ = Directory.CreateDirectory(directory);
            await using EchoPlayDbContext context = Open(directory);
            await context.Database.MigrateAsync(ct);

            _ = context.AppSettings.Add(new AppSettings { LocalLibraryRootPath = root, Volume = volume });
            await new SecureSettingsDataService(context, Loggers).SaveAsync("Spotify:ClientId", secret, ct);

            if (withLibrary)
            {
                Series series = new() { Title = "TKKG", LocalFolderPath = root + @"\TKKG" };
                Episode episode = new() { Series = series, Title = "Folge 1", EpisodeNumber = 1, LocalFolderPath = root + @"\TKKG\001" };
                _ = context.Series.Add(series);
                _ = context.Episodes.Add(episode);
                _ = context.LocalTracks.Add(new LocalTrack { Episode = episode, FilePath = root + @"\TKKG\001\01.mp3", TrackNumber = 1 });
                _ = context.PlaybackStates.Add(new PlaybackState { Episode = episode, IsCompleted = true });
            }

            _ = await context.SaveChangesAsync(ct);
            return directory;
        }

        private async Task<string> ExportAsync(string install, CancellationToken ct)
        {
            string package = Path.Combine(_root, "paket" + PackageFormat.FileExtension);
            await using EchoPlayDbContext context = Open(install);
            PackageExporter exporter = new(context, new DatabaseFileLocation(Path.Combine(install, "echoplay.db")), new FileDeviceIdentity(install));
            _ = await exporter.ExportAsync(package, "1.5.3", sealedCredentials: null, progress: null, ct);
            return package;
        }

        private static async Task StageAndScheduleAsync(string install, string package, CancellationToken ct, string? libraryRootOverride = null)
        {
            await using EchoPlayDbContext context = Open(install);
            PackageStager stager = new(context, new DatabaseFileLocation(Path.Combine(install, "echoplay.db")));
            StagedPackage staged = await stager.StageAsync(package, ct);
            new PendingImport(staged.Manifest.LibraryRoot, staged.Manifest.DeviceId, libraryRootOverride).Write(install);
        }

        private static async Task<ImportFinished> ApplyAndFinishAsync(string install, CancellationToken ct)
        {
            SqliteConnection.ClearAllPools();
            ImportCarryOver carry = (await PendingImportApplier.ApplyAsync(Path.Combine(install, "echoplay.db"), ct))!;
            Assert.NotNull(carry);

            await using EchoPlayDbContext context = Open(install);
            await context.Database.MigrateAsync(ct);
            ImportFinisher finisher = new(context, new LibraryRootRebaser(context, Loggers), new SecureSettingsDataService(context, Loggers), Loggers);
            return await finisher.FinishAsync(carry, ct);
        }

        /// <summary>Baut ein Paket von Hand, um Fassung und Prüfsumme frei zu wählen.</summary>
        private string WriteManualPackage(string databasePath, string lastMigration, bool wrongHash = false)
        {
            SqliteConnection.ClearAllPools();
            byte[] database = File.ReadAllBytes(databasePath);
            string hash = wrongHash ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(database));
            PackageManifest manifest = new(
                PackageFormat.CurrentFormatVersion, "1.0.0", lastMigration, Guid.NewGuid(), DateTimeOffset.UtcNow, SourceRoot,
                [new PackageEntry(PackageFormat.DatabaseEntry, database.Length, hash)]);

            string package = Path.Combine(_root, $"hand-{Guid.NewGuid():N}{PackageFormat.FileExtension}");
            using ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create);
            using (Stream entry = archive.CreateEntry(PackageFormat.DatabaseEntry).Open())
            {
                entry.Write(database);
            }

            using (StreamWriter writer = new(archive.CreateEntry(PackageFormat.ManifestEntry).Open()))
            {
                writer.Write(manifest.ToJson());
            }

            return package;
        }

        private static EchoPlayDbContext Open(string directory) => new(new DbContextOptionsBuilder<EchoPlayDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "echoplay.db")}")
            .Options);
    }
}

using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Replication;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="LibraryTransferService"/>: Packen mit eingebettetem Datenpaket, Auspacken
    /// samt Vorbereitung, und das Aufräumen, wenn das Datenpaket abgelehnt wird.
    /// </summary>
    public sealed class LibraryTransferServiceTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"echoplay_app_library_{Guid.NewGuid():N}");
        private readonly ScriptedTransfer _transfer = new();
        private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 20, 21, 30, 0, TimeSpan.Zero));

        public LibraryTransferServiceTests() => _ = Directory.CreateDirectory(_root);

        private string Library => Path.Combine(_root, "Mediathek");

        private string Output => Path.Combine(_root, "Ausgabe");

        private string DataDirectory => Path.Combine(_root, "Daten");

        [Fact]
        public async Task Pack_WithoutLibraryRoot_ReturnsNullAndExportsNothing()
        {
            LibraryTransferService service = Create(new AppSettings { LocalLibraryEnabled = true, LocalLibraryRootPath = null });

            (string Path, long Length)? packed = await service.PackAsync(Output, null, null, TestContext.Current.CancellationToken);

            Assert.Null(packed);
            Assert.Equal(0, _transfer.Exports);
        }

        [Fact]
        public async Task Pack_WithLibraryDisabled_ReturnsNull()
        {
            CreateLibrary();
            LibraryTransferService service = Create(new AppSettings { LocalLibraryEnabled = false, LocalLibraryRootPath = Library });

            Assert.Null(await service.PackAsync(Output, null, null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Pack_WritesTheArchiveAndRemovesTheIntermediateDataPackage()
        {
            CreateLibrary();
            LibraryTransferService service = Create(new AppSettings { LocalLibraryEnabled = true, LocalLibraryRootPath = Library });
            List<double> reported = [];

            (string Path, long Length)? packed = await service.PackAsync(Output, "Kennwort1", new Recorder(reported), TestContext.Current.CancellationToken);

            _ = Assert.NotNull(packed);
            Assert.EndsWith(LibraryArchiveFormat.FileExtension, packed.Value.Path, StringComparison.Ordinal);
            Assert.Contains("2026-11-20", packed.Value.Path, StringComparison.Ordinal);
            Assert.True(packed.Value.Length > 0);
            Assert.Equal("Kennwort1", _transfer.LastPassword);
            Assert.False(File.Exists(_transfer.LastExport));
            _ = Assert.Single(Directory.GetFileSystemEntries(Output));
            Assert.Equal(1, reported[^1]);
            Assert.All(reported, value => Assert.InRange(value, 0, 1));
        }

        [Fact]
        public async Task Pack_LeavesAnEarlierDataExportInTheFolderAlone()
        {
            CreateLibrary();
            _ = Directory.CreateDirectory(Output);
            string earlier = Path.Combine(Output, "EchoPlay-Daten.echoplay");
            await File.WriteAllTextAsync(earlier, "eigener Export", TestContext.Current.CancellationToken);
            LibraryTransferService service = Create(new AppSettings { LocalLibraryEnabled = true, LocalLibraryRootPath = Library });

            _ = await service.PackAsync(Output, null, null, TestContext.Current.CancellationToken);

            Assert.Equal("eigener Export", await File.ReadAllTextAsync(earlier, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Unpack_StagesTheEmbeddedPackageAndDeletesItAfterwards()
        {
            string archive = await PackAsync();
            LibraryTransferService service = Create(new AppSettings());
            string target = Path.Combine(_root, "Neu");

            UnpackedLibrary unpacked = await service.UnpackAsync(archive, target, null, TestContext.Current.CancellationToken);

            Assert.Equal(target, unpacked.LibraryRoot);
            Assert.Equal(2, unpacked.Files);
            Assert.True(File.Exists(Path.Combine(target, "TKKG", "01.mp3")));
            Assert.True(_transfer.Discarded);
            Assert.False(File.Exists(Path.Combine(DataDirectory, LibraryTransferService.UnpackedDataPackageName)));
            Assert.Equal("Datenpaket", _transfer.StagedContent);
        }

        [Fact]
        public async Task Unpack_RejectedDataPackage_RemovesTheUnpackedFiles()
        {
            string archive = await PackAsync();
            _transfer.StageFailure = new PackageRejectedException("Das Paket stammt aus einer neueren Fassung.");
            LibraryTransferService service = Create(new AppSettings());
            string target = Path.Combine(_root, "Neu");

            _ = await Assert.ThrowsAsync<PackageRejectedException>(() => service.UnpackAsync(archive, target, null, TestContext.Current.CancellationToken));

            Assert.False(Directory.Exists(target));
            Assert.False(File.Exists(Path.Combine(DataDirectory, LibraryTransferService.UnpackedDataPackageName)));
        }

        [Fact]
        public void Scale_MapsTheSectionOntoTheWhole()
        {
            List<double> reported = [];
            IProgress<double> scaled = LibraryTransferService.Scale(new Recorder(reported), 0.1, 1)!;

            scaled.Report(0);
            scaled.Report(0.5);
            scaled.Report(1);

            Assert.Equal(3, reported.Count);
            Assert.Equal(0.1, reported[0], 10);
            Assert.Equal(0.55, reported[1], 10);
            Assert.Equal(1, reported[2], 10);
            Assert.Null(LibraryTransferService.Scale(null, 0, 1));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private LibraryTransferService Create(AppSettings settings)
        {
            ServiceCollection services = new();
            _ = services.AddSingleton<IAppSettingsDataService>(new FakeAppSettingsDataService(settings));
            ServiceProvider provider = services.BuildServiceProvider();
            _ = Directory.CreateDirectory(DataDirectory);
            _ = Directory.CreateDirectory(Output);
            return new LibraryTransferService(
                provider.GetRequiredService<IServiceScopeFactory>(), _transfer, new DatabaseFileLocation(Path.Combine(DataDirectory, "echoplay.db")), _clock);
        }

        private async Task<string> PackAsync()
        {
            CreateLibrary();
            LibraryTransferService service = Create(new AppSettings { LocalLibraryEnabled = true, LocalLibraryRootPath = Library });
            (string Path, long Length)? packed = await service.PackAsync(Output, null, null, TestContext.Current.CancellationToken);
            return packed!.Value.Path;
        }

        private void CreateLibrary()
        {
            _ = Directory.CreateDirectory(Path.Combine(Library, "TKKG"));
            File.WriteAllText(Path.Combine(Library, "TKKG", "01.mp3"), "Spur 1");
            File.WriteAllText(Path.Combine(Library, "TKKG", "cover.jpg"), "Bild");
        }

        /// <summary>Datenexport und Einspielen als Attrappe: schreibt eine kleine Datei und merkt sich die Aufrufe.</summary>
        private sealed class ScriptedTransfer : IDataTransferService
        {
            public int Exports { get; private set; }

            public string? LastPassword { get; private set; }

            public string? LastExport { get; private set; }

            public string? StagedContent { get; private set; }

            public Exception? StageFailure { get; set; }

            public bool Discarded { get; private set; }

            public async Task<(string Path, long Length)> ExportAsync(string folder, string? credentialsPassword, IProgress<double>? progress, CancellationToken cancellationToken)
            {
                Exports++;
                LastPassword = credentialsPassword;
                LastExport = Path.Combine(folder, "EchoPlay-Daten.echoplay");
                await File.WriteAllTextAsync(LastExport, "Datenpaket", cancellationToken);
                progress?.Report(1);
                return (LastExport, 10L);
            }

            public async Task<StagedPackage> StageAsync(string packagePath, CancellationToken cancellationToken)
            {
                StagedContent = await File.ReadAllTextAsync(packagePath, cancellationToken);
                return StageFailure is null
                    ? new StagedPackage(
                        new PackageManifest(1, "1.5.3", "20261120130408_AddChangeStamp", Guid.NewGuid(), DateTimeOffset.UtcNow, null,
                            [new PackageEntry(PackageFormat.DatabaseEntry, 1, new string('a', 64))]),
                        @"C:\staged", ReadOnlyMemory<byte>.Empty, new ImportPreview(1, 2, 3, 4, 5, 6))
                    : throw StageFailure;
            }

            public bool ScheduleImport(StagedPackage staged, string? credentialsPassword, string? libraryRoot = null) => true;

            public void DiscardImport() => Discarded = true;
        }

        /// <summary>Hält jeden gemeldeten Fortschritt fest, ohne Synchronisationskontext.</summary>
        private sealed class Recorder(List<double> values) : IProgress<double>
        {
            public void Report(double value) => values.Add(value);
        }
    }
}

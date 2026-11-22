using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Data.Replication;

namespace EchoPlay.Data.Tests.Replication
{
    /// <summary>
    /// Packen und Auspacken der Mediathek über echte Dateien: Sonderzeichen, tiefe und leere Ordner,
    /// Prüfsummen, Abbruch und präparierte Archive.
    /// </summary>
    public sealed class LibraryArchiveTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"echoplay_library_{Guid.NewGuid():N}");
        private readonly TimeProvider _clock = TimeProvider.System;

        public LibraryArchiveTests() => _ = Directory.CreateDirectory(_root);

        private string Library => Path.Combine(_root, "Hörspiele");

        private string DataPackage => Path.Combine(_root, "daten.echoplay");

        private string Archive => Path.Combine(_root, "ausgabe", "mediathek" + LibraryArchiveFormat.FileExtension);

        [Fact]
        public async Task PackThenUnpack_RestoresEveryFileFolderAndTheDataPackage()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            CreateLibrary();
            List<double> reported = [];

            LibraryManifest packed = await LibraryArchiveWriter.WriteAsync(Library, DataPackage, Archive, _clock, new Recorder(reported), ct);
            string target = Path.Combine(_root, "Ziel", "Neue Mediathek");
            string data = Path.Combine(_root, "entpackt.echoplay");
            LibraryExtraction extraction = await LibraryArchiveExtractor.ExtractAsync(Archive, target, data, progress: null, ct);

            Assert.Equal(4, packed.Files.Count);
            Assert.True(extraction.CreatedTargetRoot);
            Assert.Equal(Library, extraction.Manifest.SourceRoot);
            foreach (LibraryFile file in packed.Files)
            {
                string original = Path.Combine(Library, file.Path.Replace('/', Path.DirectorySeparatorChar));
                string copy = Path.Combine(target, file.Path.Replace('/', Path.DirectorySeparatorChar));
                Assert.Equal(await File.ReadAllBytesAsync(original, ct), await File.ReadAllBytesAsync(copy, ct));
            }

            Assert.True(Directory.Exists(Path.Combine(target, "Leer", "Noch leerer")));
            Assert.Equal(await File.ReadAllBytesAsync(DataPackage, ct), await File.ReadAllBytesAsync(data, ct));
            // ZIP kennt kein Datum vor 1980; die Datei von 1975 kommt mit dem frühesten möglichen zurück.
            Assert.Equal(1980, File.GetLastWriteTime(Path.Combine(target, "alt.txt")).Year);
            Assert.Equal(1, reported[^1]);
            Assert.False(File.Exists(Archive + ".partial"));
        }

        [Fact]
        public async Task PackedEntries_AreStoredNotCompressed()
        {
            CreateLibrary();
            _ = await LibraryArchiveWriter.WriteAsync(Library, DataPackage, Archive, _clock, null, TestContext.Current.CancellationToken);

            ZipArchive archive = await ZipFile.OpenReadAsync(Archive, TestContext.Current.CancellationToken);
            await using ConfiguredAsyncDisposable disposing = archive.ConfigureAwait(false);
            ZipArchiveEntry track = archive.Entries.Single(e => e.FullName.EndsWith("01 – Anfang.mp3", StringComparison.Ordinal));
            Assert.Equal(track.Length, track.CompressedLength);
        }

        [Fact]
        public async Task Pack_TargetInsideTheLibrary_IsRefused()
        {
            CreateLibrary();
            string inside = Path.Combine(Library, "archiv" + LibraryArchiveFormat.FileExtension);

            _ = await Assert.ThrowsAsync<ArgumentException>(() => LibraryArchiveWriter.WriteAsync(
                Library, DataPackage, inside, _clock, null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Pack_MissingLibrary_SaysSo()
        {
            await File.WriteAllTextAsync(DataPackage, "x", TestContext.Current.CancellationToken);

            _ = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => LibraryArchiveWriter.WriteAsync(
                Path.Combine(_root, "fehlt"), DataPackage, Archive, _clock, null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Pack_Cancelled_LeavesNoArchiveAndNoPartialFile()
        {
            CreateLibrary();
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LibraryArchiveWriter.WriteAsync(
                Library, DataPackage, Archive, _clock, null, cancellation.Token));

            Assert.False(File.Exists(Archive));
            Assert.False(File.Exists(Archive + ".partial"));
        }

        [Fact]
        public async Task Unpack_IntoAFolderWithContent_IsRefusedAndTouchesNothing()
        {
            CreateLibrary();
            _ = await LibraryArchiveWriter.WriteAsync(Library, DataPackage, Archive, _clock, null, TestContext.Current.CancellationToken);
            string target = Path.Combine(_root, "belegt");
            _ = Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "fremd.txt"), "bleibt", TestContext.Current.CancellationToken);

            _ = await Assert.ThrowsAsync<IOException>(() => LibraryArchiveExtractor.ExtractAsync(
                Archive, target, Path.Combine(_root, "d.echoplay"), null, TestContext.Current.CancellationToken));

            Assert.Equal("bleibt", await File.ReadAllTextAsync(Path.Combine(target, "fremd.txt"), TestContext.Current.CancellationToken));
            _ = Assert.Single(Directory.EnumerateFileSystemEntries(target));
        }

        [Fact]
        public async Task Unpack_IntoAnExistingEmptyFolder_KeepsTheFolderWhenItFails()
        {
            string target = Path.Combine(_root, "vorhanden");
            _ = Directory.CreateDirectory(target);
            string archive = WriteHandmadeArchive(corruptFile: true);

            _ = await Assert.ThrowsAsync<PackageRejectedException>(() => LibraryArchiveExtractor.ExtractAsync(
                archive, target, Path.Combine(_root, "d.echoplay"), null, TestContext.Current.CancellationToken));

            Assert.True(Directory.Exists(target));
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
            Assert.False(File.Exists(Path.Combine(_root, "d.echoplay")));
        }

        [Fact]
        public async Task Unpack_DamagedFile_NamesItAndRemovesEverythingWritten()
        {
            string archive = WriteHandmadeArchive(corruptFile: true);
            string target = Path.Combine(_root, "neu");

            PackageRejectedException ex = await Assert.ThrowsAsync<PackageRejectedException>(() => LibraryArchiveExtractor.ExtractAsync(
                archive, target, Path.Combine(_root, "d.echoplay"), null, TestContext.Current.CancellationToken));

            Assert.Contains("a/b.mp3", ex.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(target));
        }

        [Theory]
        [InlineData("media/../../Windows/x.dll")]
        [InlineData("media/C:/x.dll")]
        [InlineData("anderes.txt")]
        public async Task Unpack_EntryOutsideTheFormat_IsRejectedBeforeWriting(string name)
        {
            string archive = WriteHandmadeArchive(extraEntry: name);
            string target = Path.Combine(_root, "neu");

            _ = await Assert.ThrowsAsync<PackageRejectedException>(() => LibraryArchiveExtractor.ExtractAsync(
                archive, target, Path.Combine(_root, "d.echoplay"), null, TestContext.Current.CancellationToken));

            Assert.False(Directory.Exists(target));
        }

        [Fact]
        public async Task Unpack_EntryMissingFromTheManifest_IsRejected()
        {
            string archive = WriteHandmadeArchive(extraEntry: "media/nicht-im-manifest.mp3");

            PackageRejectedException ex = await Assert.ThrowsAsync<PackageRejectedException>(() => LibraryArchiveExtractor.ExtractAsync(
                archive, Path.Combine(_root, "neu"), Path.Combine(_root, "d.echoplay"), null, TestContext.Current.CancellationToken));

            Assert.Contains("nicht im Manifest", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Unpack_NotAZip_IsRejected()
        {
            string archive = Path.Combine(_root, "kaputt" + LibraryArchiveFormat.FileExtension);
            await File.WriteAllTextAsync(archive, "kein Archiv", TestContext.Current.CancellationToken);

            _ = await Assert.ThrowsAsync<PackageRejectedException>(() => LibraryArchiveExtractor.ExtractAsync(
                archive, Path.Combine(_root, "neu"), Path.Combine(_root, "d.echoplay"), null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Unpack_ManifestNotUtf8_IsRejected()
        {
            string archive = WriteHandmadeArchive(manifestBytes: [0xC3, 0x28]);

            PackageRejectedException ex = await Assert.ThrowsAsync<PackageRejectedException>(() => LibraryArchiveExtractor.ExtractAsync(
                archive, Path.Combine(_root, "neu"), Path.Combine(_root, "d.echoplay"), null, TestContext.Current.CancellationToken));

            Assert.Contains("UTF-8", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Unpack_InvalidManifest_IsRejected()
        {
            string archive = WriteHandmadeArchive(manifestBytes: Encoding.UTF8.GetBytes("{}"));

            _ = await Assert.ThrowsAsync<PackageRejectedException>(() => LibraryArchiveExtractor.ExtractAsync(
                archive, Path.Combine(_root, "neu"), Path.Combine(_root, "d.echoplay"), null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Unpack_LeftoverDataPackage_IsReplaced()
        {
            string archive = WriteHandmadeArchive();
            string data = Path.Combine(_root, "d.echoplay");
            await File.WriteAllTextAsync(data, "von einem abgebrochenen Lauf", TestContext.Current.CancellationToken);

            _ = await LibraryArchiveExtractor.ExtractAsync(archive, Path.Combine(_root, "neu"), data, null, TestContext.Current.CancellationToken);

            Assert.Equal("Datenpaket", await File.ReadAllTextAsync(data, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Unpack_DamagedDataPackage_RemovesItAndEveryFileInTheExistingFolder()
        {
            string target = Path.Combine(_root, "vorhanden");
            _ = Directory.CreateDirectory(target);
            string data = Path.Combine(_root, "d.echoplay");
            string archive = WriteHandmadeArchive(corruptData: true, filePath: "oben.mp3");

            PackageRejectedException ex = await Assert.ThrowsAsync<PackageRejectedException>(() => LibraryArchiveExtractor.ExtractAsync(
                archive, target, data, null, TestContext.Current.CancellationToken));

            Assert.Contains(LibraryArchiveFormat.DataPackageEntry, ex.Message, StringComparison.Ordinal);
            Assert.True(Directory.Exists(target));
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
            Assert.False(File.Exists(data));
        }

        [Fact]
        public void RemoveExtracted_MissingFolder_DoesNothing()
            => LibraryArchiveExtractor.RemoveExtracted(Path.Combine(_root, "gibt-es-nicht"), createdTargetRoot: true);

        [Fact]
        public async Task Pack_NameThatCannotBeRecreated_IsRefusedWithTheName()
        {
            CreateLibrary();

            // Ein Punkt am Ende: Windows legt so eine Datei nur über den Langpfad-Präfix an und
            // schnitte den Punkt beim Auspacken wieder ab.
            string odd = @"\\?\" + Path.Combine(Library, "Folge.");
            await File.WriteAllTextAsync(odd, "x", TestContext.Current.CancellationToken);

            ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() => LibraryArchiveWriter.WriteAsync(
                Library, DataPackage, Archive, _clock, null, TestContext.Current.CancellationToken));
            Assert.Contains("Folge.", ex.Message, StringComparison.Ordinal);
            File.Delete(odd);
        }

        [Fact]
        public void ClampToZipRange_KeepsDatesZipCanStore()
        {
            DateTimeOffset early = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset late = new(2200, 1, 1, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset normal = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

            Assert.Equal(1980, LibraryArchiveWriter.ClampToZipRange(early).Year);
            Assert.Equal(2107, LibraryArchiveWriter.ClampToZipRange(late).Year);
            Assert.Equal(normal, LibraryArchiveWriter.ClampToZipRange(normal));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        /// <summary>Eine kleine Mediathek: Umlaute, Gedankenstrich, tiefe Ordner, ein leerer Ordner, ein altes Datum.</summary>
        private void CreateLibrary()
        {
            string episode = Path.Combine(Library, "Die drei Fragezeichen Kids", "001 – Spuk im Hotel");
            _ = Directory.CreateDirectory(episode);
            File.WriteAllBytes(Path.Combine(episode, "01 – Anfang.mp3"), RandomNumberGenerator.GetBytes(300_000));
            File.WriteAllBytes(Path.Combine(episode, "02 – Ende.mp3"), RandomNumberGenerator.GetBytes(1000));

            string deep = Path.Combine(Library, "Ä", "B", "C", "D", "E", "Fünf Freunde");
            _ = Directory.CreateDirectory(deep);
            File.WriteAllText(Path.Combine(deep, "cover.jpg"), "Bild");

            _ = Directory.CreateDirectory(Path.Combine(Library, "Leer", "Noch leerer"));

            string old = Path.Combine(Library, "alt.txt");
            File.WriteAllText(old, "alt");
            File.SetLastWriteTime(old, new DateTime(1975, 6, 1));

            _ = Directory.CreateDirectory(Path.GetDirectoryName(Archive)!);
            File.WriteAllBytes(DataPackage, RandomNumberGenerator.GetBytes(5000));
        }

        /// <summary>Baut ein Archiv von Hand, um einzelne Teile gezielt falsch zu machen.</summary>
        private string WriteHandmadeArchive(
            bool corruptFile = false, string? extraEntry = null, byte[]? manifestBytes = null, bool corruptData = false, string filePath = "a/b.mp3")
        {
            byte[] content = Encoding.UTF8.GetBytes("Spur");
            byte[] data = Encoding.UTF8.GetBytes("Datenpaket");
            string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

            LibraryManifest manifest = new(
                LibraryArchiveFormat.CurrentFormatVersion, DateTimeOffset.UtcNow, @"D:\Mp3",
                [new LibraryFile(filePath, content.Length, corruptFile ? new string('0', 64) : Hash(content))],
                ["a"],
                new PackageEntry(LibraryArchiveFormat.DataPackageEntry, data.Length, corruptData ? new string('0', 64) : Hash(data)));

            string path = Path.Combine(_root, $"hand-{Guid.NewGuid():N}{LibraryArchiveFormat.FileExtension}");
            using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
            Write(archive, "media/a/", []);
            Write(archive, LibraryArchiveFormat.FileEntryName(filePath), content);
            Write(archive, LibraryArchiveFormat.DataPackageEntry, data);
            if (extraEntry is not null)
            {
                Write(archive, extraEntry, content);
            }

            Write(archive, LibraryArchiveFormat.ManifestEntry, manifestBytes ?? Encoding.UTF8.GetBytes(manifest.ToJson()));
            return path;
        }

        private static void Write(ZipArchive archive, string name, byte[] content)
        {
            using Stream stream = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
            stream.Write(content);
        }

        /// <summary>Hält jeden gemeldeten Fortschritt fest, ohne Synchronisationskontext.</summary>
        private sealed class Recorder(List<double> values) : IProgress<double>
        {
            public void Report(double value) => values.Add(value);
        }
    }
}

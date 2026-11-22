using EchoPlay.Core.Replication.Packaging;

namespace EchoPlay.Core.Tests.Replication
{
    /// <summary>
    /// Tests für das Mediathek-Archiv: welche relativen Pfade gelten, wie streng das Manifest gelesen
    /// wird und was die Archivprüfung vor dem Auspacken abweist.
    /// </summary>
    public sealed class LibraryArchiveFormatTests
    {
        private static readonly string Sha = new('a', 64);

        private static LibraryManifest Sample(IReadOnlyList<LibraryFile>? files = null, IReadOnlyList<string>? directories = null) => new(
            LibraryArchiveFormat.CurrentFormatVersion,
            new DateTimeOffset(2026, 11, 20, 13, 0, 0, TimeSpan.Zero),
            @"D:\Mp3\Hörspiele",
            files ?? [new LibraryFile("TKKG/001 – Anfang/01.mp3", 10, Sha)],
            directories ?? ["TKKG", "TKKG/001 – Anfang", "Leer"],
            new PackageEntry(LibraryArchiveFormat.DataPackageEntry, 20, Sha));

        private static List<ArchiveEntryInfo> EntriesOf(LibraryManifest manifest)
        {
            List<ArchiveEntryInfo> entries =
            [
                new(LibraryArchiveFormat.ManifestEntry, 100, 300),
                new(LibraryArchiveFormat.DataPackageEntry, manifest.DataPackage.Length, manifest.DataPackage.Length),
            ];
            entries.AddRange(manifest.Files.Select(f => new ArchiveEntryInfo(LibraryArchiveFormat.FileEntryName(f.Path), f.Length, f.Length)));
            entries.AddRange(manifest.Directories.Select(d => new ArchiveEntryInfo(LibraryArchiveFormat.DirectoryEntryName(d), 0, 0)));
            return entries;
        }

        [Theory]
        [InlineData("TKKG/001 – Anfang/01.mp3")]
        [InlineData("Fünf Freunde/cover.jpg")]
        [InlineData("a")]
        [InlineData("CONSOLE/x.mp3")]
        [InlineData(".hidden")]
        public void IsSafe_OrdinaryPaths(string path) => Assert.True(LibraryRelativePath.IsSafe(path));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("/absolut")]
        [InlineData("a//b")]
        [InlineData("../x")]
        [InlineData("a/../../x")]
        [InlineData("a/./b")]
        [InlineData(@"a\b")]
        [InlineData("C:/x")]
        [InlineData("a/datei.mp3:strom")]
        [InlineData("NUL")]
        [InlineData("a/com1.txt")]
        [InlineData("Folge.")]
        [InlineData("Folge ")]
        [InlineData("a/b\u0001")]
        [InlineData("a?b")]
        [InlineData("a/")]
        public void IsSafe_RejectsWhatWindowsWouldReinterpret(string? path) => Assert.False(LibraryRelativePath.IsSafe(path));

        [Fact]
        public void IsSafe_RejectsOverlongPaths()
            => Assert.False(LibraryRelativePath.IsSafe(new string('a', LibraryArchiveFormat.MaxRelativePathLength + 1)));

        [Fact]
        public void FromFullPath_UsesForwardSlashes()
            => Assert.Equal("TKKG/001/01.mp3", LibraryRelativePath.FromFullPath(@"D:\Mp3", @"D:\Mp3\TKKG\001\01.mp3"));

        [Theory]
        [InlineData(@"D:\Mp3")]
        [InlineData(@"D:\Andere\x.mp3")]
        [InlineData(@"E:\x.mp3")]
        public void FromFullPath_OutsideTheRoot_Throws(string fullPath)
            => _ = Assert.Throws<ArgumentException>(() => LibraryRelativePath.FromFullPath(@"D:\Mp3", fullPath));

        [Fact]
        public void Resolve_StaysUnderTheRoot()
            => Assert.Equal(@"F:\Neu\TKKG\01.mp3", LibraryRelativePath.Resolve(@"F:\Neu\", "TKKG/01.mp3"));

        [Theory]
        [InlineData("../x")]
        [InlineData("C:/Windows")]
        public void Resolve_UnsafePath_Throws(string path)
            => _ = Assert.Throws<ArgumentException>(() => LibraryRelativePath.Resolve(@"F:\Neu", path));

        [Fact]
        public void Manifest_RoundTripsThroughJson()
        {
            LibraryManifest original = Sample();

            Assert.True(LibraryManifest.TryParse(original.ToJson(), out LibraryManifest? parsed, out string? error), error);

            Assert.Equal(original.SourceRoot, parsed!.SourceRoot);
            Assert.Equal(original.Files, parsed.Files);
            Assert.Equal(original.Directories, parsed.Directories);
            Assert.Equal(original.DataPackage, parsed.DataPackage);
            Assert.Equal(original.CreatedUtc, parsed.CreatedUtc);
            Assert.Equal(10, parsed.TotalFileLength);
            Assert.Contains("Hörspiele", original.ToJson(), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(null, "fehlt")]
        [InlineData("", "fehlt")]
        [InlineData("{kein json", "JSON")]
        [InlineData("null", "leer")]
        [InlineData("""{"formatVersion":2}""", "Archivformat 2")]
        [InlineData("""{"formatVersion":1,"createdUtc":"gestern"}""", "Zeitpunkt")]
        [InlineData("""{"formatVersion":1,"createdUtc":"2026-11-20T13:00:00Z","sourceRoot":"relativ"}""", "Stammpfad")]
        [InlineData("""{"formatVersion":1,"createdUtc":"2026-11-20T13:00:00Z","sourceRoot":"D:\\x"}""", "Datenpaket")]
        public void Manifest_RejectsIncompleteDocuments(string? json, string expected)
        {
            Assert.False(LibraryManifest.TryParse(json, out LibraryManifest? manifest, out string? error));
            Assert.Null(manifest);
            Assert.Contains(expected, error, StringComparison.Ordinal);
        }

        [Fact]
        public void Manifest_RejectsUnsafeAndDuplicatePaths()
        {
            Assert.False(LibraryManifest.TryParse(Sample(files: [new LibraryFile("../x", 1, Sha)]).ToJson(), out _, out string? unsafeError));
            Assert.Contains("../x", unsafeError, StringComparison.Ordinal);

            LibraryManifest duplicate = Sample(files: [new LibraryFile("a.mp3", 1, Sha), new LibraryFile("A.MP3", 1, Sha)]);
            Assert.False(LibraryManifest.TryParse(duplicate.ToJson(), out _, out _));

            LibraryManifest clash = Sample(files: [new LibraryFile("a", 1, Sha)], directories: ["a"]);
            Assert.False(LibraryManifest.TryParse(clash.ToJson(), out _, out string? clashError));
            Assert.Contains("Ordner", clashError, StringComparison.Ordinal);
        }

        [Fact]
        public void Manifest_RejectsBadLengthsAndHashes()
        {
            Assert.False(LibraryManifest.TryParse(Sample(files: [new LibraryFile("a", -1, Sha)]).ToJson(), out _, out _));
            Assert.False(LibraryManifest.TryParse(Sample(files: [new LibraryFile("a", 1, "XYZ")]).ToJson(), out _, out _));
            Assert.False(LibraryManifest.TryParse(Sample(files: [new LibraryFile("a", LibraryArchiveFormat.MaxFileLength + 1, Sha)]).ToJson(), out _, out _));
        }

        [Fact]
        public void Manifest_RejectsMissingLists()
        {
            const string withoutFiles = """{"formatVersion":1,"createdUtc":"2026-11-20T13:00:00Z","sourceRoot":"D:\\x","dataPackage":{"length":1,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}""";
            Assert.False(LibraryManifest.TryParse(withoutFiles, out _, out string? filesError));
            Assert.Contains("Dateiliste", filesError, StringComparison.Ordinal);

            string withoutDirectories = withoutFiles.Replace("\"dataPackage\"", "\"files\":[],\"dataPackage\"", StringComparison.Ordinal);
            Assert.False(LibraryManifest.TryParse(withoutDirectories, out _, out string? directoriesError));
            Assert.Contains("Ordnerliste", directoriesError, StringComparison.Ordinal);
        }

        [Fact]
        public void Manifest_RejectsOversizedDocuments()
            => Assert.False(LibraryManifest.TryParse(new string(' ', LibraryArchiveFormat.MaxManifestLength + 1), out _, out _));

        [Fact]
        public void Guard_AcceptsAnArchiveMatchingItsManifest()
        {
            LibraryManifest manifest = Sample();
            List<ArchiveEntryInfo> entries = EntriesOf(manifest);

            Assert.Null(LibraryArchiveGuard.CheckStructure(entries));
            Assert.Null(LibraryArchiveGuard.CheckAgainstManifest(entries, manifest));
        }

        [Fact]
        public void Guard_Structure_RejectsMissingManifestOrDataPackage()
        {
            Assert.NotNull(LibraryArchiveGuard.CheckStructure([new(LibraryArchiveFormat.ManifestEntry, 1, 1)]));
            Assert.NotNull(LibraryArchiveGuard.CheckStructure([new(LibraryArchiveFormat.ManifestEntry, 1, 1), new("media/a", 1, 1)]));
            Assert.NotNull(LibraryArchiveGuard.CheckStructure([new(LibraryArchiveFormat.DataPackageEntry, 1, 1), new("media/a", 1, 1)]));
        }

        [Fact]
        public void Guard_Structure_RejectsAnOversizedManifest()
        {
            List<ArchiveEntryInfo> entries =
            [
                new(LibraryArchiveFormat.ManifestEntry, 1, (LibraryArchiveFormat.MaxManifestLength * 4L) + 1),
                new(LibraryArchiveFormat.DataPackageEntry, 1, 1),
            ];

            Assert.Contains("Manifest", LibraryArchiveGuard.CheckStructure(entries), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("media/../x")]
        [InlineData("media/C:/x")]
        [InlineData("andere.txt")]
        [InlineData("media/")]
        public void Guard_Structure_RejectsUnknownEntries(string name)
        {
            List<ArchiveEntryInfo> entries = EntriesOf(Sample());
            entries.Add(new ArchiveEntryInfo(name, 1, 1));

            Assert.Contains("Unerwarteter Eintrag", LibraryArchiveGuard.CheckStructure(entries), StringComparison.Ordinal);
        }

        [Fact]
        public void Guard_Manifest_RejectsWrongLengthDuplicatesAndGaps()
        {
            LibraryManifest manifest = Sample();
            List<ArchiveEntryInfo> entries = EntriesOf(manifest);

            List<ArchiveEntryInfo> wrongLength = [.. entries.Select(e => e.FullName.EndsWith(".mp3", StringComparison.Ordinal) ? e with { Length = 11 } : e)];
            Assert.Contains("Länge", LibraryArchiveGuard.CheckAgainstManifest(wrongLength, manifest), StringComparison.Ordinal);

            List<ArchiveEntryInfo> duplicate = [.. entries, entries[^1]];
            Assert.Contains("doppelt", LibraryArchiveGuard.CheckAgainstManifest(duplicate, manifest), StringComparison.Ordinal);

            List<ArchiveEntryInfo> gap = [.. entries.Where(e => !e.FullName.EndsWith(".mp3", StringComparison.Ordinal))];
            Assert.Contains("nicht alle", LibraryArchiveGuard.CheckAgainstManifest(gap, manifest), StringComparison.Ordinal);

            List<ArchiveEntryInfo> unlisted = [.. entries, new("media/fremd.mp3", 1, 1)];
            Assert.Contains("nicht im Manifest", LibraryArchiveGuard.CheckAgainstManifest(unlisted, manifest), StringComparison.Ordinal);
        }

        [Fact]
        public void Guard_Manifest_RejectsAnImplausibleRatio()
        {
            const long big = 10L * 1024 * 1024;
            LibraryManifest manifest = Sample(files: [new LibraryFile("big.mp3", big, Sha)], directories: []);
            List<ArchiveEntryInfo> entries = [.. EntriesOf(manifest).Select(e => e.FullName.EndsWith(".mp3", StringComparison.Ordinal) ? e with { CompressedLength = 1000 } : e)];

            Assert.Contains("unplausibel", LibraryArchiveGuard.CheckAgainstManifest(entries, manifest), StringComparison.Ordinal);
        }

        [Fact]
        public void EntryNames_FollowTheFormat()
        {
            Assert.Equal("media/a/b.mp3", LibraryArchiveFormat.FileEntryName("a/b.mp3"));
            Assert.Equal("media/a/", LibraryArchiveFormat.DirectoryEntryName("a"));
            Assert.Equal("data.echoplay", LibraryArchiveFormat.DataPackageEntry);
        }
    }
}

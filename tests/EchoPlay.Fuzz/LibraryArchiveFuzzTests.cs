using EchoPlay.Core.Replication.Packaging;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace EchoPlay.Fuzz
{
    /// <summary>
    /// Eigenschaftstests für das Mediathek-Archiv. Pfade und Manifest kommen aus einer fremden Datei;
    /// was sie auch enthalten, nichts darf abstürzen, und nichts Angenommenes darf aus dem Zielordner
    /// hinausführen.
    /// </summary>
    public sealed class LibraryArchiveFuzzTests
    {
        private const string Target = @"F:\Ziel\Mediathek";

        /// <summary>Pfade aus Bausteinen, die Angriffe und Windows-Eigenheiten enthalten.</summary>
        private static Gen<string> Paths()
        {
            Gen<string> segment = Gen.OneOf(
                Gen.Elements("..", ".", "", "a", "NUL", "com1.txt", "C:", "x:y", "Folge.", "Folge ", "ä", "\\", "..\\..", "\u0001"),
                ArbMap.Default.GeneratorFor<string>().Where(s => s is not null));
            return segment.ListOf().Select(parts => string.Join('/', parts));
        }

        [Property(MaxTest = 5000)]
        public Property Resolve_AcceptedPath_StaysUnderTheTarget()
            => Prop.ForAll(Paths().ToArbitrary(), path =>
            {
                if (!LibraryRelativePath.IsSafe(path))
                {
                    return true;
                }

                string full = LibraryRelativePath.Resolve(Target, path);
                return full.StartsWith(Target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            });

        [Property(MaxTest = 5000)]
        public bool Manifest_AnyText_NeverThrows(string? json)
        {
            _ = LibraryManifest.TryParse(json, out _, out _);
            return true;
        }

        [Property(MaxTest = 2000)]
        public Property Manifest_Accepted_HasOnlySafeUniquePaths()
            => Prop.ForAll(Paths().ListOf().ToArbitrary(), paths =>
            {
                LibraryManifest candidate = new(
                    LibraryArchiveFormat.CurrentFormatVersion, DateTimeOffset.UtcNow, @"D:\Mp3",
                    [.. paths.Select(p => new LibraryFile(p, 1, new string('a', 64)))], [],
                    new PackageEntry(LibraryArchiveFormat.DataPackageEntry, 1, new string('a', 64)));

                if (!LibraryManifest.TryParse(candidate.ToJson(), out LibraryManifest? parsed, out _))
                {
                    return true;
                }

                return parsed!.Files.All(f => LibraryRelativePath.IsSafe(f.Path))
                    && parsed.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == parsed.Files.Count;
            });

        [Property(MaxTest = 3000)]
        public Property Guard_AnyEntries_NeverThrows_AndKnowsEveryAcceptedName()
        {
            Gen<ArchiveEntryInfo> entries =
                from name in Gen.OneOf(
                    Gen.Elements(LibraryArchiveFormat.ManifestEntry, LibraryArchiveFormat.DataPackageEntry, "media/a", "media/../x", "media/", "x"),
                    Paths().Select(p => LibraryArchiveFormat.MediaPrefix + p))
                from compressed in Gen.Choose(-5, 10_000_000).Select(n => (long)n)
                from length in Gen.Choose(-5, 2_000_000_000).Select(n => (long)n)
                select new ArchiveEntryInfo(name, compressed, length);

            return Prop.ForAll(entries.ListOf().ToArbitrary(), list =>
            {
                string? problem = LibraryArchiveGuard.CheckStructure([.. list]);
                return problem is not null || list.All(e =>
                    e.FullName is LibraryArchiveFormat.ManifestEntry or LibraryArchiveFormat.DataPackageEntry
                    || e.FullName.StartsWith(LibraryArchiveFormat.MediaPrefix, StringComparison.Ordinal));
            });
        }
    }
}

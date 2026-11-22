using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Core.Security;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace EchoPlay.Fuzz
{
    /// <summary>
    /// Eigenschaftstests für Manifest, Archivprüfung und Zugangsdaten. Ein Paket kommt von außen;
    /// was es auch enthält, das Lesen darf nicht abstürzen, und was angenommen wird, muss gültig sein.
    /// </summary>
    public sealed class PackageFuzzTests
    {
        [Property(MaxTest = 5000)]
        public bool Manifest_AnyText_NeverThrows(string? json)
        {
            _ = PackageManifest.TryParse(json, out _, out _);
            return true;
        }

        [Property(MaxTest = 2000)]
        public bool Manifest_Accepted_HasADatabaseAndOnlyKnownEntries(string? json)
        {
            if (!PackageManifest.TryParse(json, out PackageManifest? manifest, out _))
            {
                return true;
            }

            return manifest!.Entries.Any(e => e.Name == PackageFormat.DatabaseEntry)
                && manifest.Entries.All(e => PackageFormat.IsContentEntry(e.Name));
        }

        [Property(MaxTest = 5000)]
        public Property Guard_AnyEntries_NeverThrows_AndRejectsUnknownNames()
        {
            Gen<string> names = Gen.OneOf(
                Gen.Elements(PackageFormat.ManifestEntry, PackageFormat.DatabaseEntry, PackageFormat.CredentialsEntry, "../x", "a/b", "", "manifest.json/"),
                ArbMap.Default.GeneratorFor<string>().Where(s => s is not null));
            Gen<ArchiveEntryInfo> entries =
                from name in names
                from compressed in Gen.Choose(-5, 10_000_000).Select(n => (long)n)
                from length in Gen.Choose(-5, 2_000_000_000).Select(n => (long)n)
                select new ArchiveEntryInfo(name, compressed, length);

            return Prop.ForAll(entries.ListOf().ToArbitrary(), list =>
            {
                string? problem = ArchiveGuard.Check([.. list]);
                bool allKnown = list.All(e => e.FullName == PackageFormat.ManifestEntry || PackageFormat.IsContentEntry(e.FullName));
                return problem is not null || allKnown;
            });
        }

        [Property(MaxTest = 300)]
        public bool Sealer_AnyBytes_NeverThrows(byte[] data, NonEmptyString password)
        {
            _ = CredentialSealer.TryOpen(data ?? [], password.Get, out _);
            return true;
        }
    }
}

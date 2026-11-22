using System.Text;
using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Core.Security;

namespace EchoPlay.Core.Tests.Replication
{
    /// <summary>
    /// Tests für Manifest, Archivprüfung und die Kennwort-Verschlüsselung der Zugangsdaten.
    /// </summary>
    public sealed class PackagingTests
    {
        private static readonly string Sha = new('a', 64);

        private static PackageManifest Sample(params PackageEntry[] entries) => new(
            PackageFormat.CurrentFormatVersion, "1.5.3", "20261120130408_AddChangeStamp",
            new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), new DateTimeOffset(2026, 11, 20, 13, 0, 0, TimeSpan.Zero),
            @"D:\Mp3\Hörspiele",
            entries.Length > 0 ? entries : [new PackageEntry(PackageFormat.DatabaseEntry, 1234, Sha)]);

        [Fact]
        public void Manifest_RoundTripsThroughJson()
        {
            PackageManifest original = Sample();

            Assert.True(PackageManifest.TryParse(original.ToJson(), out PackageManifest? parsed, out string? error), error);
            Assert.Equal(original.LastMigration, parsed!.LastMigration);
            Assert.Equal(original.DeviceId, parsed.DeviceId);
            Assert.Equal(original.CreatedUtc, parsed.CreatedUtc);
            Assert.Equal(original.LibraryRoot, parsed.LibraryRoot);
            Assert.Equal(original.Entries, parsed.Entries);
        }

        [Theory]
        [InlineData("formatVersion\": 1", "formatVersion\": 2")]
        [InlineData("20261120130408_AddChangeStamp", "neueste")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "00000000000000000000000000000000")]
        [InlineData("database.sqlite", "../database.sqlite")]
        [InlineData("D:\\\\Mp3\\\\Hörspiele", "relativ\\\\pfad")]
        public void Manifest_WithOneBadField_IsRejected(string field, string replacement)
        {
            string json = Sample().ToJson();
            Assert.Contains(field, json, StringComparison.Ordinal);

            Assert.False(PackageManifest.TryParse(json.Replace(field, replacement, StringComparison.Ordinal), out _, out string? error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void Manifest_WithoutDatabase_IsRejected()
        {
            string json = Sample(new PackageEntry(PackageFormat.CredentialsEntry, 10, Sha)).ToJson();

            Assert.False(PackageManifest.TryParse(json, out _, out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("[]")]
        [InlineData("null")]
        public void Manifest_Garbage_IsRejected(string? json)
        {
            Assert.False(PackageManifest.TryParse(json, out _, out _));
        }

        [Fact]
        public void Guard_AcceptsAWellFormedPackage()
        {
            Assert.Null(ArchiveGuard.Check(
            [
                new ArchiveEntryInfo(PackageFormat.ManifestEntry, 400, 900),
                new ArchiveEntryInfo(PackageFormat.DatabaseEntry, 1_300_000_000, 1_400_000_000),
                new ArchiveEntryInfo(PackageFormat.CredentialsEntry, 200, 200),
            ]));
        }

        [Theory]
        [InlineData("../database.sqlite")]
        [InlineData(@"..\..\Windows\evil.dll")]
        [InlineData("sub/database.sqlite")]
        [InlineData("C:/database.sqlite")]
        [InlineData("anderes.txt")]
        public void Guard_UnexpectedOrEscapingName_IsRejected(string name)
        {
            Assert.NotNull(ArchiveGuard.Check(
            [
                new ArchiveEntryInfo(PackageFormat.ManifestEntry, 400, 900),
                new ArchiveEntryInfo(name, 100, 100),
            ]));
        }

        [Fact]
        public void Guard_ArchiveBomb_IsRejected()
        {
            Assert.NotNull(ArchiveGuard.Check(
            [
                new ArchiveEntryInfo(PackageFormat.ManifestEntry, 400, 900),
                new ArchiveEntryInfo(PackageFormat.DatabaseEntry, 1_000_000, 10_000_000_000),
            ]));
        }

        [Fact]
        public void Guard_DuplicateOrTooManyEntries_AreRejected()
        {
            Assert.NotNull(ArchiveGuard.Check(
            [
                new ArchiveEntryInfo(PackageFormat.ManifestEntry, 400, 900),
                new ArchiveEntryInfo(PackageFormat.DatabaseEntry, 100, 100),
                new ArchiveEntryInfo(PackageFormat.DatabaseEntry, 100, 100),
            ]));
            Assert.NotNull(ArchiveGuard.Check([.. Enumerable.Repeat(new ArchiveEntryInfo(PackageFormat.ManifestEntry, 1, 1), 4)]));
        }

        [Fact]
        public void Sealer_RoundTrips_AndRejectsAWrongPassword()
        {
            byte[] plain = Encoding.UTF8.GetBytes("""{"clientId":"abc","clientSecret":"geheim"}""");
            byte[] sealedData = CredentialSealer.Seal(plain, "richtig-langes-Kennwort");

            Assert.True(CredentialSealer.TryOpen(sealedData, "richtig-langes-Kennwort", out byte[] opened));
            Assert.Equal(plain, opened);
            Assert.False(CredentialSealer.TryOpen(sealedData, "falsches-Kennwort", out _));
        }

        [Fact]
        public void Sealer_TamperedContent_IsRejected()
        {
            byte[] sealedData = CredentialSealer.Seal("inhalt"u8, "Kennwort123");
            sealedData[^1] ^= 0x01;

            Assert.False(CredentialSealer.TryOpen(sealedData, "Kennwort123", out _));
        }

        [Fact]
        public void Sealer_ForeignIterationCount_IsRejected()
        {
            // Ein präpariertes Paket mit zehn Durchläufen machte das Kennwort schutzlos, eines mit
            // einer Milliarde die Anwendung lahm.
            byte[] sealedData = CredentialSealer.Seal("inhalt"u8, "Kennwort123");
            BitConverter.GetBytes(10).CopyTo(sealedData, 4);

            Assert.False(CredentialSealer.TryOpen(sealedData, "Kennwort123", out _));
        }

        [Fact]
        public void Sealer_SamePlaintextTwice_GivesDifferentOutput()
        {
            Assert.NotEqual(CredentialSealer.Seal("x"u8, "Kennwort123"), CredentialSealer.Seal("x"u8, "Kennwort123"));
        }
    }
}

using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Core.Security;

namespace EchoPlay.Core.Tests.Replication
{
    /// <summary>
    /// Randfälle von Manifest, Archivprüfung und Zugangsdaten.
    /// </summary>
    public sealed class PackagingEdgeCaseTests
    {
        private static readonly string Sha = new('b', 64);

        private static string Json(string appVersion = "1.5.3", string created = "2026-11-20T13:00:00Z", string? entries = null)
        {
            string list = !string.IsNullOrEmpty(entries) ? entries : $$"""[{ "name": "database.sqlite", "length": 1, "sha256": "{{Sha}}" }]""";
            return $$"""
                {
                  "formatVersion": 1,
                  "appVersion": "{{appVersion}}",
                  "lastMigration": "20261120130408_AddChangeStamp",
                  "deviceId": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "createdUtc": "{{created}}",
                  "libraryRoot": null,
                  "entries": {{list}}
                }
                """;
        }

        [Fact]
        public void Manifest_WithTheBaseline_IsAccepted()
        {
            Assert.True(PackageManifest.TryParse(Json(), out PackageManifest? manifest, out string? error), error);
            Assert.Null(manifest!.LibraryRoot);
        }

        [Theory]
        [InlineData("")]
        [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
        public void Manifest_BadAppVersion_IsRejected(string appVersion)
        {
            Assert.False(PackageManifest.TryParse(Json(appVersion: appVersion), out _, out _));
        }

        [Fact]
        public void Manifest_BadDate_IsRejected()
        {
            Assert.False(PackageManifest.TryParse(Json(created: "gestern"), out _, out _));
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("""[{ "name": "database.sqlite", "length": -1, "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }]""")]
        [InlineData("""[{ "name": "database.sqlite", "length": 1, "sha256": "XYZ" }]""")]
        [InlineData("""[{ "name": "database.sqlite", "length": 1, "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }, { "name": "database.sqlite", "length": 1, "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }]""")]
        public void Manifest_BadEntries_AreRejected(string entries)
        {
            Assert.False(PackageManifest.TryParse(Json(entries: entries), out _, out _));
        }

        [Fact]
        public void Manifest_TooLong_IsRejected()
        {
            Assert.False(PackageManifest.TryParse(new string(' ', PackageFormat.MaxManifestLength + 1), out _, out _));
        }

        [Theory]
        [InlineData(-1, 10)]
        [InlineData(10, -1)]
        [InlineData(10, PackageFormat.MaxCredentialsLength + 1)]
        public void Guard_NegativeOrOversizedEntry_IsRejected(long compressed, long length)
        {
            Assert.NotNull(ArchiveGuard.Check(
            [
                new ArchiveEntryInfo(PackageFormat.ManifestEntry, 400, 900),
                new ArchiveEntryInfo(PackageFormat.DatabaseEntry, 100, 100),
                new ArchiveEntryInfo(PackageFormat.CredentialsEntry, compressed, length),
            ]));
        }

        [Fact]
        public void Guard_LargeEntryWithZeroCompressedLength_IsRejected()
        {
            Assert.NotNull(ArchiveGuard.Check(
            [
                new ArchiveEntryInfo(PackageFormat.ManifestEntry, 400, 900),
                new ArchiveEntryInfo(PackageFormat.DatabaseEntry, 0, 5_000_000),
            ]));
        }

        [Fact]
        public void Guard_WithoutDatabase_IsRejected()
        {
            Assert.NotNull(ArchiveGuard.Check([new ArchiveEntryInfo(PackageFormat.ManifestEntry, 400, 900)]));
        }

        [Fact]
        public void Guard_MaxLengths_FollowTheFormat()
        {
            Assert.Equal(PackageFormat.MaxDatabaseLength, ArchiveGuard.MaxLengthOf(PackageFormat.DatabaseEntry));
            Assert.Equal(PackageFormat.MaxCredentialsLength, ArchiveGuard.MaxLengthOf(PackageFormat.CredentialsEntry));
            Assert.True(ArchiveGuard.MaxLengthOf(PackageFormat.ManifestEntry) >= PackageFormat.MaxManifestLength);
        }

        [Theory]
        [InlineData(new byte[0])]
        [InlineData(new byte[] { (byte)'E', (byte)'P', (byte)'S', (byte)'1' })]
        [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52 })]
        public void Sealer_ShortOrForeignData_IsRejected(byte[] data)
        {
            Assert.False(CredentialSealer.TryOpen(data, "Kennwort123", out _));
        }

        [Fact]
        public void Sealer_EmptyPassword_IsRejected()
        {
            byte[] sealedData = CredentialSealer.Seal("x"u8, "Kennwort123");

            Assert.False(CredentialSealer.TryOpen(sealedData, string.Empty, out _));
            _ = Assert.Throws<ArgumentException>(() => CredentialSealer.Seal("x"u8, string.Empty));
        }
    }
}

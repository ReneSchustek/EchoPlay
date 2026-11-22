using EchoPlay.Data.Replication;
using System.IO.Compression;

namespace EchoPlay.Data.Tests.Replication
{
    /// <summary>Tests für <see cref="RequiredEntry"/>.</summary>
    public sealed class RequiredEntryTests
    {
        [Fact]
        public void Of_PresentEntry_ReturnsIt()
        {
            using MemoryStream stream = new();
            using ZipArchive archive = new(stream, ZipArchiveMode.Update);
            _ = archive.CreateEntry("manifest.json");

            Assert.Equal("manifest.json", RequiredEntry.Of(archive, "manifest.json").FullName);
        }

        [Fact]
        public void Of_MissingEntry_RejectsThePackageWithItsName()
        {
            using MemoryStream stream = new();
            using ZipArchive archive = new(stream, ZipArchiveMode.Update);

            PackageRejectedException ex = Assert.Throws<PackageRejectedException>(() => RequiredEntry.Of(archive, "manifest.json"));

            Assert.Contains("manifest.json", ex.Message, StringComparison.Ordinal);
        }
    }
}

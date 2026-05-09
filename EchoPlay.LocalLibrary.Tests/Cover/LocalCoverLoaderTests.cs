using EchoPlay.LocalLibrary.Cover;
using System;
using System.IO;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Tests.Cover
{
    /// <summary>
    /// Tests für <see cref="LocalCoverLoader"/>.
    /// Arbeiten gegen ein temporäres Verzeichnis, weil der Loader das echte Dateisystem liest.
    /// </summary>
    public sealed class LocalCoverLoaderTests : IDisposable
    {
        private readonly string _tempRoot;

        public LocalCoverLoaderTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "EchoPlay-LocalCoverLoaderTests-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }

        [Fact]
        public async Task LoadAsync_CoverJpgInFolder_ReturnsBytes()
        {
            string coverPath = Path.Combine(_tempRoot, "cover.jpg");
            byte[] expectedBytes = [0x01, 0x02, 0x03, 0x04];
            await File.WriteAllBytesAsync(coverPath, expectedBytes, cancellationToken: TestContext.Current.CancellationToken);
            LocalCoverLoader loader = new();

            byte[]? result = await loader.LoadAsync(_tempRoot, firstTrackPath: null);

            Assert.NotNull(result);
            Assert.Equal(expectedBytes, result);
        }

        [Fact]
        public async Task LoadAsync_NoCoverAndNoTrack_ReturnsNull()
        {
            LocalCoverLoader loader = new();

            byte[]? result = await loader.LoadAsync(_tempRoot, firstTrackPath: null);

            Assert.Null(result);
        }

        [Fact]
        public async Task LoadAsync_TrackPathDoesNotExist_ReturnsNull()
        {
            LocalCoverLoader loader = new();
            string missingTrack = Path.Combine(_tempRoot, "nicht-vorhanden.mp3");

            byte[]? result = await loader.LoadAsync(episodeFolderPath: null, firstTrackPath: missingTrack);

            Assert.Null(result);
        }
    }
}

using EchoPlay.TagManager.Models;
using EchoPlay.TagManager.Services;
using EchoPlay.TagManager.Tests.Fakes;
using EchoPlay.TagManager.Tests.Infrastructure;

namespace EchoPlay.TagManager.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="TagService"/>.
    /// Alle Tests arbeiten mit minimalen, temporären Audiodateien, die von
    /// <see cref="AudioTestFileFactory"/> im Temp-Ordner erzeugt und nach dem Test gelöscht werden.
    /// </summary>
    public sealed class TagServiceTests
    {
        private static int _folderCounter;

        private static TagService CreateService() => new(new FakeLoggerFactory());

        [Fact]
        public async Task ReadAsync_ReturnsTitleFromMp3()
        {
            string path = AudioTestFileFactory.CreateTempMp3();
            try
            {
                TagService service = CreateService();

                AudioTag tag = await service.ReadAsync(path);

                // Die minimal-MP3-Datei enthält "TestTitle" als TIT2-Frame
                Assert.Equal("TestTitle", tag.Title);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ReadAsync_ReturnsTitleFromFlac()
        {
            string path = AudioTestFileFactory.CreateTempFlac();
            try
            {
                TagService service = CreateService();

                // FLAC-Testdatei enthält keinen vorgesetzten Titel –
                // nach dem Anlegen der Datei wird der Titel erst geschrieben
                await service.WriteAsync(path, new AudioTag { Title = "FlacTitel" });
                AudioTag tag = await service.ReadAsync(path);

                Assert.Equal("FlacTitel", tag.Title);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task WriteAsync_UpdatesTitle()
        {
            string path = AudioTestFileFactory.CreateTempMp3();
            try
            {
                TagService service = CreateService();

                await service.WriteAsync(path, new AudioTag { Title = "NeueTitel" });
                AudioTag tag = await service.ReadAsync(path);

                Assert.Equal("NeueTitel", tag.Title);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task WriteAsync_NullFieldIsIgnored()
        {
            // Ein null-Feld in AudioTag bedeutet "nicht ändern" – der bestehende Wert bleibt.
            string path = AudioTestFileFactory.CreateTempMp3();
            try
            {
                TagService service = CreateService();

                // Zuerst Album setzen
                await service.WriteAsync(path, new AudioTag { Album = "Originales Album" });

                // Nur Title ändern, Album als null lassen → Album soll erhalten bleiben
                await service.WriteAsync(path, new AudioTag { Title = "Neuer Titel", Album = null });
                AudioTag tag = await service.ReadAsync(path);

                Assert.Equal("Neuer Titel", tag.Title);
                Assert.Equal("Originales Album", tag.Album);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task WriteAsync_EmptyStringClearsField()
        {
            // Ein leerer String ist kein null – er wird geschrieben und beim Lesen als null zurückgegeben.
            // Damit lässt sich ein bestehendes Tag-Feld effektiv löschen.
            string path = AudioTestFileFactory.CreateTempMp3();
            try
            {
                TagService service = CreateService();

                await service.WriteAsync(path, new AudioTag { Title = "" });
                AudioTag tag = await service.ReadAsync(path);

                // NullIfEmpty wandelt leere Strings beim Lesen in null um
                Assert.Null(tag.Title);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task RemoveAllTagsAsync_ClearsAllFields()
        {
            string path = AudioTestFileFactory.CreateTempMp3();
            try
            {
                TagService service = CreateService();

                await service.RemoveAllTagsAsync(path);
                AudioTag tag = await service.ReadAsync(path);

                Assert.Null(tag.Title);
                Assert.Null(tag.Album);
                Assert.Null(tag.Artist);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task WriteCoverAsync_SetsCover()
        {
            string path = AudioTestFileFactory.CreateTempMp3();
            try
            {
                TagService service = CreateService();

                // Minimales 1×1 JPEG als Cover (gültiger JPEG-Header, 107 Bytes)
                byte[] cover = CreateMinimalJpeg();
                await service.WriteCoverAsync(path, cover, "image/jpeg");

                AudioTag tag = await service.ReadAsync(path);

                Assert.NotNull(tag.CoverImageData);
                Assert.Equal("image/jpeg", tag.CoverMimeType);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task WriteCoverAsync_NullRemovesCover()
        {
            string path = AudioTestFileFactory.CreateTempMp3();
            try
            {
                TagService service = CreateService();

                // Cover setzen, dann mit null entfernen
                await service.WriteCoverAsync(path, CreateMinimalJpeg(), "image/jpeg");
                await service.WriteCoverAsync(path, null);

                AudioTag tag = await service.ReadAsync(path);

                Assert.Null(tag.CoverImageData);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ReadFolderAsync_ReturnsAllFiles()
        {
            int folderId = Interlocked.Increment(ref _folderCounter);
            string folder = Path.Combine(Path.GetTempPath(), $"echoplay_test_{folderId:D6}");
            Directory.CreateDirectory(folder);
            try
            {
                TagService service = CreateService();

                // Zwei MP3-Dateien direkt in den Testordner schreiben (kein Umweg über temp)
                string tempA = AudioTestFileFactory.CreateTempMp3();
                string tempB = AudioTestFileFactory.CreateTempMp3();
                File.Move(tempA, Path.Combine(folder, "a.mp3"));
                File.Move(tempB, Path.Combine(folder, "b.mp3"));

                IReadOnlyList<(string FilePath, AudioTag Tag)> results = await service.ReadFolderAsync(folder);

                Assert.Equal(2, results.Count);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task ReadAsync_ThrowsForUnsupportedFormat()
        {
            string path = AudioTestFileFactory.CreateTempUnsupportedFile();
            try
            {
                TagService service = CreateService();

                // TagLib# kann .xyz-Dateien nicht lesen → InvalidOperationException
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadAsync(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        // --- Hilfsmethoden ---

        /// <summary>
        /// Erstellt ein minimales, gültiges JPEG (1x1 Pixel, weiß).
        /// Wird für Cover-Tests benötigt, damit TagLib# einen gültigen MIME-Typ setzen kann.
        /// </summary>
        private static byte[] CreateMinimalJpeg() =>
        [
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
            0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
            0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08, 0x07, 0x07, 0x07, 0x09,
            0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
            0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20,
            0x24, 0x2E, 0x27, 0x20, 0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29,
            0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27, 0x39, 0x3D, 0x38, 0x32,
            0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01,
            0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x1F, 0x00, 0x00,
            0x01, 0x05, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0xFB, 0xD7,
            0xFF, 0xD9
        ];
    }
}

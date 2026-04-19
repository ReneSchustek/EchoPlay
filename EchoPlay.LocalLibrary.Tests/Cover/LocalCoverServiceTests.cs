using EchoPlay.LocalLibrary.Cover;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.LocalLibrary.Tests.Cover
{
    /// <summary>
    /// Tests für <see cref="LocalCoverService"/>.
    /// Nutzt temporäre Verzeichnisse als echtes Dateisystem.
    /// Netzwerkzugriffe werden nicht durchgeführt – kein echtes <see cref="CoverService"/> nötig.
    /// </summary>
    public sealed class LocalCoverServiceTests : IDisposable
    {
        private readonly string _root;

        // Minimaler JPEG-Header – reicht für Byte-Vergleiche in Tests
        private static readonly byte[] JpegA = [0xFF, 0xD8, 0xFF, 0xE0, 0x01];
        private static readonly byte[] JpegB = [0xFF, 0xD8, 0xFF, 0x02];
        private static readonly byte[] JpegC = [0xFF, 0xD8, 0xFF, 0x03];

        /// <summary>Erstellt ein temporäres Verzeichnis für jeden Test.</summary>
        public LocalCoverServiceTests()
        {
            _root = Directory.CreateTempSubdirectory("echoplay_cover_").FullName;
        }

        /// <summary>Löscht das temporäre Verzeichnis nach jedem Test.</summary>
        public void Dispose()
        {
            Directory.Delete(_root, recursive: true);
        }

        // ── Lokale Cover-Dateien im Serienordner ─────────────────────────────────

        [Fact]
        public async Task ResolveAsync_ReturnsCoverBytes_WhenCoverJpgExists()
        {
            // cover.jpg im Serienordner ist die bevorzugte lokale Quelle
            await File.WriteAllBytesAsync(Path.Combine(_root, "cover.jpg"), JpegA);

            byte[]? result = await BuildService().ResolveAsync(_root, coverImageUrl: null);

            Assert.Equal(JpegA, result);
        }

        [Fact]
        public async Task ResolveAsync_ReturnsCoverBytes_WhenCoverJpegExists()
        {
            // cover.jpeg (mit e) wird ebenfalls erkannt
            await File.WriteAllBytesAsync(Path.Combine(_root, "cover.jpeg"), JpegA);

            byte[]? result = await BuildService().ResolveAsync(_root, coverImageUrl: null);

            Assert.Equal(JpegA, result);
        }

        [Fact]
        public async Task ResolveAsync_ReturnsCoverBytes_WhenFolderJpgExists()
        {
            // folder.jpg – üblich wenn cover.jpg fehlt (z.B. ältere Windows-Media-Player-Bibliotheken)
            await File.WriteAllBytesAsync(Path.Combine(_root, "folder.jpg"), JpegB);

            byte[]? result = await BuildService().ResolveAsync(_root, coverImageUrl: null);

            Assert.Equal(JpegB, result);
        }

        [Fact]
        public async Task ResolveAsync_ReturnsCoverBytes_WhenAlbumArtLargeExists()
        {
            // Windows Media Player erzeugt AlbumArt_*_Large.jpg automatisch – letzter Fallback
            string wmpFile = Path.Combine(_root, "AlbumArt_{9C849FDB-D1AC-4B16-9A54-3AD2F74CE9A9}_Large.jpg");
            await File.WriteAllBytesAsync(wmpFile, JpegC);

            byte[]? result = await BuildService().ResolveAsync(_root, coverImageUrl: null);

            Assert.Equal(JpegC, result);
        }

        // ── Cover-Unterordner ─────────────────────────────────────────────────────

        [Fact]
        public async Task ResolveAsync_ReturnsFrontCover_WhenCoverSubfolderContainsFrontJpg()
        {
            // Cover/front.jpg ist das Erkennungsbild – hat Vorrang vor generischen Dateinamen
            string coverDir = Directory.CreateDirectory(Path.Combine(_root, "Cover")).FullName;
            await File.WriteAllBytesAsync(Path.Combine(coverDir, "front.jpg"), JpegA);
            await File.WriteAllBytesAsync(Path.Combine(coverDir, "back.jpg"), JpegB);

            byte[]? result = await BuildService().ResolveAsync(_root, coverImageUrl: null);

            Assert.Equal(JpegA, result);
        }

        [Fact]
        public async Task ResolveAsync_IgnoresBackCover_InCoverSubfolder()
        {
            // back.jpg zeigt die Rückseite – wird übersprungen, auch wenn kein front.jpg existiert
            string coverDir = Directory.CreateDirectory(Path.Combine(_root, "Cover")).FullName;
            await File.WriteAllBytesAsync(Path.Combine(coverDir, "back.jpg"), JpegB);

            byte[]? result = await BuildService().ResolveAsync(_root, coverImageUrl: null);

            // Nur back.jpg vorhanden → kein verwendbares Cover im Unterordner
            Assert.Null(result);
        }

        [Fact]
        public async Task ResolveAsync_ReturnsCoverFromSubfolder_WhenNoFrontOrBackInName()
        {
            // cover.jpg im Cover-Unterordner wird verwendet wenn kein "back" im Namen steht
            string coverDir = Directory.CreateDirectory(Path.Combine(_root, "Cover")).FullName;
            await File.WriteAllBytesAsync(Path.Combine(coverDir, "cover.jpg"), JpegA);

            byte[]? result = await BuildService().ResolveAsync(_root, coverImageUrl: null);

            Assert.Equal(JpegA, result);
        }

        [Fact]
        public async Task ResolveAsync_CoverSubfolder_HasPriority_OverSeriesFolderFile()
        {
            // Cover-Unterordner wird zuerst geprüft – cover.jpg direkt im Serienordner kommt danach
            string coverDir = Directory.CreateDirectory(Path.Combine(_root, "Cover")).FullName;
            await File.WriteAllBytesAsync(Path.Combine(coverDir, "front.jpg"), JpegA);
            await File.WriteAllBytesAsync(Path.Combine(_root, "cover.jpg"), JpegB);

            byte[]? result = await BuildService().ResolveAsync(_root, coverImageUrl: null);

            Assert.Equal(JpegA, result);
        }

        // ── Kein Cover vorhanden ─────────────────────────────────────────────────

        [Fact]
        public async Task ResolveAsync_ReturnsNull_WhenNoSourceAvailable()
        {
            // Kein lokales Cover, keine URL → null zurückgeben
            byte[]? result = await BuildService().ResolveAsync(_root, coverImageUrl: null);

            Assert.Null(result);
        }

        [Fact]
        public async Task ResolveAsync_ReturnsNull_WhenDirectoryDoesNotExist()
        {
            // Nicht existierendes Verzeichnis darf keinen Fehler werfen
            byte[]? result = await BuildService().ResolveAsync(
                "/non/existent/path", coverImageUrl: null);

            Assert.Null(result);
        }

        /// <summary>
        /// Erstellt einen <see cref="LocalCoverService"/> ohne echten HTTP-Client.
        /// Tests ohne coverImageUrl lösen keinen Netzwerkzugriff aus.
        /// </summary>
        private static LocalCoverService BuildService()
        {
            // Test-Fixture – Ausnahme von der IHttpClientFactory-Pflicht: da kein Test eine coverImageUrl
            // übergibt, bleibt der HttpClient ungenutzt und kann kein Socket-Leak verursachen.
            return new LocalCoverService(new CoverService(new HttpClient()));
        }
    }
}

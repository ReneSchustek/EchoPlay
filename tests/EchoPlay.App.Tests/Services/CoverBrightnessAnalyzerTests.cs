using System;
using System.Threading.Tasks;
using EchoPlay.App.Services;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft die Helligkeitsmessung in der oberen linken Ecke eines Covers.
    /// Dort liegt der Rahmen des Auswahlkästchens: Auf hellem Grund braucht er einen
    /// dunklen Rand, auf dunklem einen hellen — sonst sieht der Anwender nicht, was
    /// er ausgewählt hat.
    ///
    /// Die Testbilder stehen als Bytefolge in der Datei; es wird keine Datei gelesen.
    /// </summary>
    public sealed class CoverBrightnessAnalyzerTests
    {
        [Fact]
        public async Task AnalyzeBrightnessFromBytesAsync_Null_WirftArgumentNullException()
        {
            // Abgrenzung zu den drei Tests darunter: kaputte Bilddaten sind ein Betriebsfall
            // und ergeben null. Kein Array ist ein Programmierfehler und muss auffallen,
            // statt still als "Helligkeit unbekannt" durchzugehen.
            _ = await Assert.ThrowsAsync<ArgumentNullException>(
                () => CoverBrightnessAnalyzer.AnalyzeBrightnessFromBytesAsync(null!));
        }

        [Fact]
        public async Task AnalyzeBrightnessFromBytesAsync_EmptyArray_ReturnsNull()
        {
            bool? result = await CoverBrightnessAnalyzer.AnalyzeBrightnessFromBytesAsync(Array.Empty<byte>());

            Assert.Null(result);
        }

        [Fact]
        public async Task AnalyzeBrightnessFromBytesAsync_GarbageBytes_ReturnsNull()
        {
            // Nicht-Bild-Bytes sollen den Analyzer nicht zum Crash bringen — er muss
            // sie als 'unbekannte Helligkeit' (null) abweisen.
            byte[] garbage = new byte[] { 0x00, 0xFF, 0x42, 0x13, 0x37, 0xCA, 0xFE, 0xBA, 0xBE };

            bool? result = await CoverBrightnessAnalyzer.AnalyzeBrightnessFromBytesAsync(garbage);

            Assert.Null(result);
        }

        [Fact]
        public async Task AnalyzeBrightnessFromBytesAsync_TruncatedJpegHeader_ReturnsNull()
        {
            // JPEG-Magic-Header ohne Datenrest — BitmapDecoder muss das als ungültig erkennen.
            byte[] truncated = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };

            bool? result = await CoverBrightnessAnalyzer.AnalyzeBrightnessFromBytesAsync(truncated);

            Assert.Null(result);
        }

        [Fact]
        public async Task AnalyzeBrightnessFromBytesAsync_HellesCover_MeldetHell()
        {
            // Die Ecke oben links trägt den Rahmen des Auswahlkästchens. Auf einem hellen
            // Cover braucht er einen dunklen Rand — sonst verschwindet er im Bild.
            bool? result = await CoverBrightnessAnalyzer.AnalyzeBrightnessFromBytesAsync(HellesBild);

            Assert.True(result);
        }

        [Fact]
        public async Task AnalyzeBrightnessFromBytesAsync_DunklesCover_MeldetDunkel()
        {
            bool? result = await CoverBrightnessAnalyzer.AnalyzeBrightnessFromBytesAsync(DunklesBild);

            Assert.False(result);
        }

        // ── Testbilder ───────────────────────────────────────────────

        /// <summary>Ein 4×4 großes Bild in fast weiß, als Bytefolge — keine Datei im Test.</summary>
        private static readonly byte[] HellesBild =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
            0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x04,
            0x08, 0x02, 0x00, 0x00, 0x00, 0x26, 0x93, 0x09, 0x29, 0x00, 0x00, 0x00,
            0x0F, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0xF8, 0x80, 0x04, 0x18,
            0x88, 0xE3, 0x00, 0x00, 0x92, 0x70, 0x2D, 0x01, 0x06, 0xA6, 0x33, 0x35,
            0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
        ];

        /// <summary>Dasselbe Bild in fast schwarz.</summary>
        private static readonly byte[] DunklesBild =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
            0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x04,
            0x08, 0x02, 0x00, 0x00, 0x00, 0x26, 0x93, 0x09, 0x29, 0x00, 0x00, 0x00,
            0x0E, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0xE0, 0x42, 0x02, 0x0C,
            0xC4, 0x71, 0x00, 0x30, 0xF4, 0x01, 0xE1, 0x37, 0xFA, 0xC6, 0x71, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
        ];
    }
}

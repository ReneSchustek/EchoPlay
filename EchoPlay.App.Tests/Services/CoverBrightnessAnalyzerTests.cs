using System;
using System.Threading.Tasks;
using EchoPlay.App.Services;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Smoke-Tests fuer den Cover-Helligkeits-Analyzer.
    /// Vollstaendige Pixel-Tests brauchen einen WinUI-Test-Host und ein gueltiges
    /// PNG/JPEG — hier nur die robusten Null-Pfade, die ohne BitmapDecoder-Setup laufen.
    /// </summary>
    public sealed class CoverBrightnessAnalyzerTests
    {
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
            // JPEG-Magic-Header ohne Datenrest — BitmapDecoder muss das als ungueltig erkennen.
            byte[] truncated = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };

            bool? result = await CoverBrightnessAnalyzer.AnalyzeBrightnessFromBytesAsync(truncated);

            Assert.Null(result);
        }
    }
}

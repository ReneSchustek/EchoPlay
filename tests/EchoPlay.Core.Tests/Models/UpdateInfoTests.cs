using EchoPlay.Core.Models;

namespace EchoPlay.Core.Tests.Models
{
    /// <summary>
    /// Prüft den Datensatz, mit dem eine gefundene Aktualisierung beschrieben wird.
    /// </summary>
    /// <remarks>
    /// Größe und Prüfsumme entscheiden, ob die heruntergeladene Datei überhaupt
    /// ausgeführt wird. Ein leerer Prüfwert ist dabei erlaubt und bedeutet „ohne
    /// Prüfung" — nicht „Prüfung fehlgeschlagen".
    /// </remarks>
    public sealed class UpdateInfoTests
    {
        [Fact]
        public void UpdateInfo_CarriesSizeAndChecksum()
        {
            UpdateInfo info = new(
                Version: "1.4.2",
                ReleaseNotes: "Behobene Fehler",
                DownloadUrl: "https://example.invalid/EchoPlay-Setup.exe",
                FileSizeBytes: 87_654_321,
                ExpectedSha256: "ABCDEF0123456789");

            Assert.Equal(87_654_321, info.FileSizeBytes);
            Assert.Equal("ABCDEF0123456789", info.ExpectedSha256);
        }

        [Fact]
        public void UpdateInfo_WithoutAChecksum_KeepsTheEmptyValue()
        {
            UpdateInfo info = new("1.4.2", string.Empty, "https://example.invalid/s.exe", 0, string.Empty);

            // Leer heißt: Der Release-Text nannte keinen Prüfwert. Die Integritätsprüfung
            // entfällt dann, statt fehlzuschlagen.
            Assert.Empty(info.ExpectedSha256);
            Assert.Equal(0, info.FileSizeBytes);
        }
    }
}

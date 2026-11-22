using EchoPlay.Core.Models;

namespace EchoPlay.Core.Tests.Models
{
    /// <summary>
    /// Prüft den Datensatz, mit dem eine gefundene Aktualisierung beschrieben wird.
    /// </summary>
    /// <remarks>
    /// Größe, Prüfsumme und Signatur entscheiden, ob die heruntergeladene Datei überhaupt
    /// ausgeführt wird. Der Datensatz trägt die Werte nur weiter; ob ein leerer Prüfwert
    /// genügt, entscheidet der Download-Dienst, und er lässt ihn nicht genügen.
    /// </remarks>
    public sealed class UpdateInfoTests
    {
        [Fact]
        public void UpdateInfo_CarriesSizeChecksumAndSignature()
        {
            UpdateInfo info = new(
                Version: "1.4.2",
                ReleaseNotes: "Behobene Fehler",
                DownloadUrl: new Uri("https://example.invalid/EchoPlay-Setup.exe"),
                FileSizeBytes: 87_654_321,
                ExpectedSha256: "ABCDEF0123456789",
                SignatureUrl: new Uri("https://example.invalid/EchoPlay-Setup.exe.sig"));

            Assert.Equal(87_654_321, info.FileSizeBytes);
            Assert.Equal("ABCDEF0123456789", info.ExpectedSha256);
            Assert.Equal(new Uri("https://example.invalid/EchoPlay-Setup.exe.sig"), info.SignatureUrl);
        }

        [Fact]
        public void UpdateInfo_WithoutAChecksum_KeepsTheEmptyValue()
        {
            UpdateInfo info = new("1.4.2", string.Empty, new Uri("https://example.invalid/s.exe"), 0, string.Empty, new Uri("https://example.invalid/s.exe.sig"));

            // Der Datensatz trägt den Wert nur weiter. Was ein leerer Prüfwert bedeutet, entscheidet
            // der Download-Dienst: Er installiert dann nicht.
            Assert.Empty(info.ExpectedSha256);
            Assert.Equal(0, info.FileSizeBytes);
        }
    }
}

using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Core.Models
{
    /// <summary>
    /// Beschreibt eine verfügbare App-Aktualisierung.
    /// Wird vom UpdateCheckService zurückgegeben, wenn eine neuere Version auf GitHub verfügbar ist.
    /// </summary>
    /// <param name="Version">Versionsnummer der neuen Version (z.B. "1.2.0").</param>
    /// <param name="ReleaseNotes">Beschreibungstext des GitHub-Releases (Markdown).</param>
    /// <param name="DownloadUrl">Direkte Download-URL der Setup-Datei (.exe).</param>
    /// <param name="FileSizeBytes">Dateigröße der Setup-Datei in Bytes (0 wenn unbekannt).</param>
    /// <param name="ExpectedSha256">Erwarteter SHA-256-Hash der Setup-Datei in Hex (lower-case, 64 Zeichen). Leer wenn der Release-Body keinen Hash enthält — dann wird die Integritätsprüfung übersprungen.</param>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "DTO spiegelt GitHub-Release-JSON; Uri-Typ würde Deserialisierungsaufwand erhöhen.")]
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "DTO spiegelt GitHub-Release-JSON; Uri-Typ würde Deserialisierungsaufwand erhöhen.")]
    public sealed record UpdateInfo(
        string Version,
        string ReleaseNotes,
        string DownloadUrl,
        long FileSizeBytes,
        string ExpectedSha256);
}

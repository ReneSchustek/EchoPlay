namespace EchoPlay.Core.Models
{
    /// <summary>
    /// Beschreibt eine verfügbare App-Aktualisierung.
    /// Wird vom UpdateCheckService zurückgegeben, wenn eine neuere Version auf GitHub verfügbar ist.
    /// </summary>
    /// <param name="Version">Versionsnummer der neuen Version (z.B. "1.2.0").</param>
    /// <param name="ReleaseNotes">Beschreibungstext des GitHub-Releases (Markdown).</param>
    /// <param name="DownloadUrl">Geprüfte Adresse der Setup-Datei (.exe) am Release.</param>
    /// <param name="FileSizeBytes">Dateigröße der Setup-Datei in Bytes (0 wenn unbekannt).</param>
    /// <param name="ExpectedSha256">Erwarteter SHA-256-Hash der Setup-Datei in Hex (64 Zeichen, Groß-/Kleinschreibung beliebig). Ein Release ohne gültigen Hash wird gar nicht erst als Update angeboten.</param>
    /// <param name="SignatureUrl">Adresse der losgelösten Signatur (<c>&lt;Setup&gt;.sig</c>) am Release. Ein Release ohne Signatur wird gar nicht erst als Update angeboten.</param>
    public sealed record UpdateInfo(
        string Version,
        string ReleaseNotes,
        Uri DownloadUrl,
        long FileSizeBytes,
        string ExpectedSha256,
        Uri SignatureUrl);
}

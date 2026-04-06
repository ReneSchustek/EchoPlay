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
    public sealed record UpdateInfo(
        string Version,
        string ReleaseNotes,
        string DownloadUrl,
        long FileSizeBytes);
}

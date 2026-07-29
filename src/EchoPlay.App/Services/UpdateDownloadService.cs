using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.Core.Security;
using EchoPlay.Logger.Abstractions;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Lädt die Setup-Datei einer neuen App-Version herunter und startet den Installer.
    /// Die Datei wird unter <c>%TEMP%</c> abgelegt und nach dem Start des Installers
    /// beendet sich die App, damit der Installer die Dateien aktualisieren kann.
    /// </summary>
    public sealed partial class UpdateDownloadService
    {
        // Akzeptierte Versions-Tags: 1, 1.2, 1.2.3, 1.2.3.4 — strikt numerisch.
        // Schützt den Setup-Dateinamen gegen Path-Traversal-Versuche aus
        // manipulierten GitHub-Release-Tags.
        [GeneratedRegex(@"^\d+(\.\d+){0,3}$")]
        private static partial Regex VersionPattern();

        // Hosts, von denen eine Setup-Datei bezogen werden darf. Die Download-URL stammt
        // aus dem Feld "browser_download_url" der GitHub-Release-API und ist damit ein
        // Wert aus einer fremden Antwort — sie landet aber als ausführbare Datei auf dem
        // Rechner des Nutzers. Ohne Bindung an bekannte Hosts würde eine manipulierte
        // Antwort (DNS-Hijack auf api.github.com, TLS-Interception über ein eingeschleustes
        // Root-Zertifikat, kompromittierter Release-Eintrag) genügen, um den Download auf
        // einen beliebigen Server umzulenken.
        //
        // Geprüft wird nur die Start-URL: Folge-Redirects laufen weiterhin über TLS und
        // .NET verweigert einen Redirect von HTTPS auf HTTP von sich aus.
        private static readonly string[] AllowedDownloadHosts =
        [
            "github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com"
        ];

        // SHA-256 als Hex: genau 64 Zeichen, nichts anderes.
        private const int Sha256HexLength = 64;
        private const int Sha256ByteLength = 32;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IInstallerLauncher _installerLauncher;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Download-Service. Der Installer-Download nutzt den Named-
        /// Client <c>UpdateDownload</c>, der ein längeres Timeout und den passenden
        /// User-Agent trägt.
        /// </summary>
        /// <param name="httpClientFactory">Parameter <c>httpClientFactory</c>.</param>
        /// <param name="installerLauncher">Startet die geprüfte Setup-Datei.</param>
        /// <param name="loggerFactory">Parameter <c>loggerFactory</c>.</param>
        public UpdateDownloadService(
            IHttpClientFactory httpClientFactory,
            IInstallerLauncher installerLauncher,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _httpClientFactory = httpClientFactory;
            _installerLauncher = installerLauncher;
            _logger = loggerFactory.CreateLogger(nameof(UpdateDownloadService));
        }

        /// <summary>
        /// Lädt die Setup-Datei herunter und startet den Installer.
        /// </summary>
        /// <param name="downloadUrl">Direkte Download-URL der Setup-Datei. Muss HTTPS sein und auf einen GitHub-Release-Host zeigen.</param>
        /// <param name="version">Versionsnummer für den Dateinamen (muss <c>^\d+(\.\d+){0,3}$</c> matchen).</param>
        /// <param name="expectedFileSize">Erwartete Größe der Setup-Datei in Bytes laut Release-Asset (0 = Vergleich überspringen).</param>
        /// <param name="expectedSha256">Erwarteter SHA-256-Hash der Setup-Datei in Hex (64 Zeichen, Groß-/Kleinschreibung beliebig). Pflichtangabe — fehlt sie, wird nicht installiert.</param>
        /// <param name="onProgress">Fortschritts-Callback (0.0–1.0). Null wenn kein Fortschritt gewünscht.</param>
        /// <param name="cancellationToken">Abbruch-Token für den Download.</param>
        /// <returns>True wenn der Installer gestartet wurde, false bei Fehler.</returns>
        [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "downloadUrl kommt aus externem Release-Feed (GitHub) und wird als string weitergereicht.")]
        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Setup-Download+Start: HTTP-Fehler oder IO-Fehler beim Schreiben der Temp-Datei dürfen den App-Start nicht stören – false signalisiert 'Download fehlgeschlagen, Nutzer kann später erneut versuchen'.")]
        public async Task<bool> DownloadAndInstallAsync(
            string downloadUrl,
            string version,
            long expectedFileSize,
            string expectedSha256,
            Action<double>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            // Version validieren, bevor sie in den Pfad fließt — sonst wäre Path-Traversal über
            // einen manipulierten Release-Tag möglich (z. B. "../../foo").
            if (!VersionPattern().IsMatch(version))
            {
                _logger.Warning("Ungültiges Versionsformat im Update-Tag — Download abgelehnt: \"{Version}\"", version);
                return false;
            }

            if (!TryParseDownloadUrl(downloadUrl, out Uri? downloadUri))
            {
                return false;
            }

            // Der Hash wird vor dem Download geprüft, nicht danach: Fehlt oder taugt er nicht,
            // gibt es keinen Grund, überhaupt 80 MB zu laden.
            if (!TryParseExpectedHash(expectedSha256, out byte[]? expectedHashBytes))
            {
                return false;
            }

            try
            {
                string tempDirectory = Path.GetTempPath();
                string tempPath = Path.Combine(tempDirectory, $"EchoPlay-Setup-{version}.exe");

                // Defense-in-Depth: trotz Whitelist-Regex bestätigen, dass der finale Pfad
                // wirklich im %TEMP%-Verzeichnis liegt — schützt gegen Edge-Cases (z. B. wenn
                // das Regex erweitert wird und versehentlich Trennzeichen durchlässt).
                if (!SecurePathHelper.IsPathInside(tempPath, tempDirectory))
                {
                    _logger.Warning("Setup-Pfad liegt außerhalb von TEMP — Download abgelehnt: \"{TempPath}\"", tempPath);
                    return false;
                }

                HttpClient httpClient = _httpClientFactory.CreateClient("UpdateDownload");
                using HttpResponseMessage response = await httpClient
                    .GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                _ = response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                long downloadedBytes = 0;

                await using FileStream fileStream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);

                byte[] buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                        .ConfigureAwait(false);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0 && onProgress is not null)
                    {
                        onProgress((double)downloadedBytes / totalBytes.Value);
                    }
                }

                // Stream schließen, damit die Größenprüfung gegen die fertige Datei läuft.
                await fileStream.DisposeAsync().ConfigureAwait(false);

                // ContentLength-Vergleich gegen die im Release-Asset gemeldete Größe.
                // Schützt gegen abgebrochene Downloads und gegen Manipulation, die nicht über
                // den Release-Eintrag selbst läuft (CDN-Vergiftung wäre das Hauptszenario).
                if (expectedFileSize > 0 && downloadedBytes != expectedFileSize)
                {
                    _logger.Warning("Setup-Dateigröße weicht ab — erwartet {ExpectedFileSize}, geladen {DownloadedBytes}. Datei wird gelöscht.", expectedFileSize, downloadedBytes);
                    TryDelete(tempPath);
                    return false;
                }

                // SHA-256-Hash-Pin: die eigentliche Integritätsprüfung. EchoPlay wird nicht
                // per Authenticode signiert — der Hash aus dem Release-Body ist damit die
                // einzige Kontrolle, die eine manipulierte Setup-Datei erkennt.
                if (!await VerifyFileHashAsync(tempPath, expectedHashBytes, cancellationToken).ConfigureAwait(false))
                {
                    TryDelete(tempPath);
                    return false;
                }

                // Installer starten – die App beendet sich danach
                return _installerLauncher.Start(tempPath);
            }
            catch (Exception ex)
            {
                // Download-Fehler → Nutzer kann es beim nächsten Start erneut versuchen
                _logger.Warning("Update-Download fehlgeschlagen: {Reason}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Prüft die Download-URL auf HTTPS und einen erlaubten GitHub-Release-Host.
        /// </summary>
        /// <param name="downloadUrl">Rohwert aus dem Release-Feed.</param>
        /// <param name="downloadUri">Die geprüfte URI, wenn die Prüfung besteht.</param>
        /// <returns>True, wenn von dieser URL geladen werden darf.</returns>
        private bool TryParseDownloadUrl(string downloadUrl, [NotNullWhen(true)] out Uri? downloadUri)
        {
            downloadUri = null;

            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? parsed))
            {
                _logger.Warning("Update-Download-URL ist keine absolute URL — Download abgelehnt.");
                return false;
            }

            if (parsed.Scheme != Uri.UriSchemeHttps)
            {
                _logger.Warning("Update-Download-URL nutzt nicht HTTPS (\"{Scheme}\") — Download abgelehnt.", parsed.Scheme);
                return false;
            }

            if (!Array.Exists(AllowedDownloadHosts, host => string.Equals(host, parsed.Host, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.Warning("Update-Download-URL zeigt auf einen fremden Host (\"{Host}\") — Download abgelehnt.", parsed.Host);
                return false;
            }

            downloadUri = parsed;
            return true;
        }

        /// <summary>
        /// Wandelt den Hash aus dem Release-Body in Bytes um und lehnt fehlende oder
        /// unbrauchbare Angaben ab.
        /// </summary>
        /// <param name="expectedSha256">Hex-Hash aus dem Release-Body.</param>
        /// <param name="expectedHashBytes">Die 32 Hash-Bytes, wenn die Angabe taugt.</param>
        /// <returns>True, wenn ein verwertbarer Hash vorliegt.</returns>
        /// <remarks>
        /// Früher lief die Installation weiter, wenn im Release-Body kein Hash stand
        /// ("Backwards-Compat"). Das hebelte den Schutz aber genau dort aus, wo er zählt:
        /// Wer den Body manipulieren kann, streicht einfach die SHA-Zeile und die Prüfung
        /// entfällt. Ohne Hash wird deshalb nicht mehr installiert.
        /// </remarks>
        private bool TryParseExpectedHash(string expectedSha256, [NotNullWhen(true)] out byte[]? expectedHashBytes)
        {
            expectedHashBytes = null;

            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                _logger.Warning("Kein SHA-256-Hash im Release-Body gepflegt — Update wird nicht installiert.");
                return false;
            }

            if (expectedSha256.Length != Sha256HexLength)
            {
                _logger.Warning("SHA-256 im Release-Body hat falsche Länge ({ActualLength} Zeichen statt {ExpectedLength}) — Update abgelehnt.", expectedSha256.Length, Sha256HexLength);
                return false;
            }

            try
            {
                expectedHashBytes = Convert.FromHexString(expectedSha256);
            }
            catch (FormatException ex)
            {
                _logger.Warning("SHA-256 im Release-Body ist kein gültiges Hex — Update abgelehnt: {Reason}", ex.Message);
                return false;
            }

            return expectedHashBytes.Length == Sha256ByteLength;
        }

        /// <summary>
        /// Verifiziert den SHA-256-Hash der heruntergeladenen Setup-Datei gegen den
        /// im GitHub-Release-Body gepflegten Erwartungswert. Vergleich läuft Timing-Safe
        /// über <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>.
        /// </summary>
        /// <param name="filePath">Vollständiger Pfad zur fertig heruntergeladenen Setup-Datei.</param>
        /// <param name="expectedHashBytes">Die 32 erwarteten Hash-Bytes.</param>
        /// <param name="cancellationToken">Abbruch-Token für die Hash-Berechnung.</param>
        /// <returns>True bei Match; false bei Mismatch.</returns>
        private async Task<bool> VerifyFileHashAsync(string filePath, byte[] expectedHashBytes, CancellationToken cancellationToken)
        {
            byte[] actualBytes;
            await using (FileStream verifyStream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                actualBytes = await SHA256.HashDataAsync(verifyStream, cancellationToken).ConfigureAwait(false);
            }

            if (!CryptographicOperations.FixedTimeEquals(actualBytes, expectedHashBytes))
            {
                _logger.Warning("SHA-256 der Setup-Datei stimmt nicht mit dem Release-Body überein — erwartet {ExpectedHash}, berechnet {ActualHash}. Datei wird gelöscht.", Convert.ToHexString(expectedHashBytes), Convert.ToHexString(actualBytes));
                return false;
            }

            return true;
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Best-Effort-Cleanup einer Temp-Datei darf den Caller nicht stören.")]
        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("Aufräumen der Setup-Datei fehlgeschlagen: {Reason}", ex.Message);
            }
        }
    }
}

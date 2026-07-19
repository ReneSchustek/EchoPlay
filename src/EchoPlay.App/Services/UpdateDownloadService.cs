using System;
using System.Diagnostics;
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

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Download-Service. Der Installer-Download nutzt den Named-
        /// Client <c>UpdateDownload</c>, der ein längeres Timeout und den passenden
        /// User-Agent trägt.
        /// </summary>


        /// <param name="httpClientFactory">Parameter <c>httpClientFactory</c>.</param>
        /// <param name="loggerFactory">Parameter <c>loggerFactory</c>.</param>
        public UpdateDownloadService(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _httpClientFactory = httpClientFactory;
            _logger = loggerFactory.CreateLogger(nameof(UpdateDownloadService));
        }

        /// <summary>
        /// Lädt die Setup-Datei herunter und startet den Installer.
        /// </summary>
        /// <param name="downloadUrl">Direkte Download-URL der Setup-Datei.</param>
        /// <param name="version">Versionsnummer für den Dateinamen (muss <c>^\d+(\.\d+){0,3}$</c> matchen).</param>
        /// <param name="expectedFileSize">Erwartete Größe der Setup-Datei in Bytes laut Release-Asset (0 = Vergleich überspringen).</param>
        /// <param name="expectedSha256">Erwarteter SHA-256-Hash der Setup-Datei in Hex (64 Zeichen, Groß-/Kleinschreibung beliebig). Leer = ohne Integritätsprüfung installieren (Backwards-Compat mit Releases ohne Hash im Body).</param>
        /// <param name="onProgress">Fortschritts-Callback (0.0–1.0). Null wenn kein Fortschritt gewünscht.</param>
        /// <param name="cancellationToken">Abbruch-Token für den Download.</param>
        /// <returns>True wenn der Installer gestartet wurde, false bei Fehler.</returns>
        [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "downloadUrl kommt aus externem Release-Feed (GitHub) und wird als string weitergereicht.")]
        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Setup-Download+Start: HTTP-Fehler, IO-Fehler beim Schreiben der Temp-Datei oder Win32Exception aus 'Process.Start' dürfen den App-Start nicht stören – false signalisiert 'Download fehlgeschlagen, Nutzer kann später erneut versuchen'.")]
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
                    .GetAsync(new Uri(downloadUrl, UriKind.Absolute), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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

                // SHA-256-Hash-Pin: zweite Verteidigungslinie gegen Inhalts-Manipulation
                // (kompromittierter GitHub-Account, MITM mit aufgebrochenem TLS, CDN-Vergiftung).
                // Hash und Datei kommen aus derselben Quelle — schützt nicht gegen vollen
                // Account-Compromise, aber gegen alle anderen Angriffsszenarien auf dem Transport.
                if (!await VerifyFileHashAsync(tempPath, expectedSha256, cancellationToken).ConfigureAwait(false))
                {
                    TryDelete(tempPath);
                    return false;
                }

                // Installer starten – die App beendet sich danach
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception ex)
            {
                // Download-Fehler → Nutzer kann es beim nächsten Start erneut versuchen
                _logger.Warning("Update-Download fehlgeschlagen: {Reason}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Verifiziert den SHA-256-Hash der heruntergeladenen Setup-Datei gegen den
        /// im GitHub-Release-Body gepflegten Erwartungswert. Vergleich läuft Timing-Safe
        /// über <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>.
        /// </summary>
        /// <param name="filePath">Vollständiger Pfad zur fertig heruntergeladenen Setup-Datei.</param>
        /// <param name="expectedSha256">Erwarteter Hex-Hash (64 Zeichen). Leer = Prüfung überspringen.</param>
        /// <param name="cancellationToken">Abbruch-Token für die Hash-Berechnung.</param>
        /// <returns>True bei Match oder leerem Erwartungswert; false bei Mismatch oder ungültigem Hex.</returns>
        private async Task<bool> VerifyFileHashAsync(string filePath, string expectedSha256, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                // Backwards-Compat: alte Releases ohne Hash im Body — Warning, aber Installation läuft weiter.
                _logger.Warning("Kein SHA-256-Hash im Release-Body gepflegt — Update wird ohne Integritätsprüfung installiert.");
                return true;
            }

            byte[] expectedBytes;
            try
            {
                expectedBytes = Convert.FromHexString(expectedSha256);
            }
            catch (FormatException ex)
            {
                _logger.Warning("SHA-256 im Release-Body ist kein gültiges Hex — Update abgelehnt: {Reason}", ex.Message);
                return false;
            }

            if (expectedBytes.Length != 32)
            {
                _logger.Warning("SHA-256 im Release-Body hat falsche Länge ({ActualLength} Bytes statt 32) — Update abgelehnt.", expectedBytes.Length);
                return false;
            }

            byte[] actualBytes;
            await using (FileStream verifyStream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                actualBytes = await SHA256.HashDataAsync(verifyStream, cancellationToken).ConfigureAwait(false);
            }

            if (!CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes))
            {
                _logger.Warning("SHA-256 der Setup-Datei stimmt nicht mit dem Release-Body überein — erwartet {ExpectedHash}, berechnet {ActualHash}. Datei wird gelöscht.", Convert.ToHexString(expectedBytes), Convert.ToHexString(actualBytes));
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

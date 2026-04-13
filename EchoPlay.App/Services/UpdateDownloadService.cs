using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Lädt die Setup-Datei einer neuen App-Version herunter und startet den Installer.
    /// Die Datei wird unter <c>%TEMP%</c> abgelegt und nach dem Start des Installers
    /// beendet sich die App, damit der Installer die Dateien aktualisieren kann.
    /// </summary>
    public sealed class UpdateDownloadService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        /// <summary>
        /// Initialisiert den Download-Service. Der Installer-Download nutzt den Named-
        /// Client <c>UpdateDownload</c>, der ein längeres Timeout und den passenden
        /// User-Agent trägt.
        /// </summary>
        public UpdateDownloadService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Lädt die Setup-Datei herunter und startet den Installer.
        /// </summary>
        /// <param name="downloadUrl">Direkte Download-URL der Setup-Datei.</param>
        /// <param name="version">Versionsnummer für den Dateinamen.</param>
        /// <param name="onProgress">Fortschritts-Callback (0.0–1.0). Null wenn kein Fortschritt gewünscht.</param>
        /// <param name="cancellationToken">Abbruch-Token für den Download.</param>
        /// <returns>True wenn der Installer gestartet wurde, false bei Fehler.</returns>
        [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "downloadUrl kommt aus externem Release-Feed (GitHub) und wird als string weitergereicht.")]
        public async Task<bool> DownloadAndInstallAsync(
            string downloadUrl,
            string version,
            Action<double>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                string tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"EchoPlay-Setup-{version}.exe");

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

                // Installer starten – die App beendet sich danach
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true
                });

                return true;
            }
            catch
            {
                // Download-Fehler → Nutzer kann es beim nächsten Start erneut versuchen
                return false;
            }
        }
    }
}

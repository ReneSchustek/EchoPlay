using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EchoPlay.Core.Models;
using EchoPlay.Core.Security;
using EchoPlay.Logger.Abstractions;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Lädt die Setup-Datei einer neuen App-Version herunter, prüft Hash und Signatur und startet
    /// den Installer. Die Datei wird unter <c>%TEMP%</c> abgelegt; nach dem Start des Installers
    /// beendet sich die App, damit der Installer die Dateien aktualisieren kann.
    /// </summary>
    /// <remarks>
    /// Zwei Prüfungen mit verschiedenem Zweck: Der SHA-256 aus dem Release-Text zeigt, dass die
    /// Datei so ankam, wie das Release sie beschreibt. Die Signatur zeigt, dass sie vom Herausgeber
    /// stammt; ein Angreifer, der den Release-Eintrag austauscht, kann Datei und Hash ersetzen,
    /// aber keine gültige Signatur erzeugen. Installiert wird nur, wenn beides stimmt.
    /// </remarks>
    public sealed partial class UpdateDownloadService
    {
        // Akzeptierte Versions-Tags: 1, 1.2, 1.2.3, 1.2.3.4 (strikt numerisch).
        // Schützt den Setup-Dateinamen gegen Path-Traversal-Versuche aus
        // manipulierten GitHub-Release-Tags. Der Tag kommt aus dem Netz, deshalb die Zeitgrenze.
        [GeneratedRegex(@"^\d+(\.\d+){0,3}$", RegexOptions.None, matchTimeoutMilliseconds: 500)]
        private static partial Regex VersionPattern();

        // Hosts, von denen Setup-Datei und Signatur bezogen werden dürfen. Beide Adressen stammen
        // aus dem Feld "browser_download_url" der GitHub-Release-API und sind damit Werte aus
        // einer fremden Antwort. Die Setup-Datei landet aber als ausführbare Datei auf dem
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

        // SHA-256 als Hex: genau 64 Zeichen, nichts anderes. Als Bytes sind das 32.
        private const int Sha256HexLength = 64;
        private const int Sha256ByteLength = 32;

        private const string DownloadClientName = "UpdateDownload";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IInstallerLauncher _installerLauncher;
        private readonly ReleaseSignatureVerifier _signatureVerifier;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Download-Service. Setup-Datei und Signatur laufen über den Named-
        /// Client <c>UpdateDownload</c>, der ein längeres Timeout und den passenden User-Agent trägt.
        /// </summary>
        /// <param name="httpClientFactory">Liefert den Named-Client <c>UpdateDownload</c> mit langem Timeout für den großen Setup-Download.</param>
        /// <param name="installerLauncher">Startet die geprüfte Setup-Datei.</param>
        /// <param name="signatureVerifier">Prüft die losgelöste Signatur gegen den eingebetteten öffentlichen Schlüssel.</param>
        /// <param name="loggerFactory">Erzeugt den Logger für Ablehnungsgründe und Download-Fehler.</param>
        public UpdateDownloadService(
            IHttpClientFactory httpClientFactory,
            IInstallerLauncher installerLauncher,
            ReleaseSignatureVerifier signatureVerifier,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _httpClientFactory = httpClientFactory;
            _installerLauncher = installerLauncher;
            _signatureVerifier = signatureVerifier;
            _logger = loggerFactory.CreateLogger(nameof(UpdateDownloadService));
        }

        /// <summary>
        /// Lädt Signatur und Setup-Datei, prüft beide und startet den Installer.
        /// </summary>
        /// <param name="update">Das angebotene Update mit Version, Adressen, Größe und Hash.</param>
        /// <param name="onProgress">Fortschritts-Callback (0.0–1.0) für die Setup-Datei. Null wenn kein Fortschritt gewünscht.</param>
        /// <param name="cancellationToken">Abbruch-Token für den Download.</param>
        /// <returns>True wenn der Installer gestartet wurde, false bei Fehler oder Ablehnung.</returns>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Setup-Download+Start: HTTP-Fehler oder IO-Fehler beim Schreiben der Temp-Datei dürfen den App-Start nicht stören – false signalisiert 'Download fehlgeschlagen, Nutzer kann später erneut versuchen'.")]
        public async Task<bool> DownloadAndInstallAsync(
            UpdateInfo update,
            Action<double>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(update);
            if (!TryValidateRequest(update, out byte[]? expectedHashBytes))
            {
                return false;
            }

            try
            {
                return await DownloadVerifyAndStartAsync(update, expectedHashBytes, onProgress, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ein Abbruch des Aufrufers ist kein Fehlschlag und gehört nach oben.
                throw;
            }
            catch (Exception ex)
            {
                // Download-Fehler → Nutzer kann es beim nächsten Start erneut versuchen
                _logger.Warning("Update-Download fehlgeschlagen: {Reason}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Lädt Signatur und Setup-Datei und startet den Installer nur, wenn beide passen.
        /// </summary>
        private async Task<bool> DownloadVerifyAndStartAsync(
            UpdateInfo update,
            byte[] expectedHashBytes,
            Action<double>? onProgress,
            CancellationToken cancellationToken)
        {
            string? setupPath = ResolveSetupPath(update.Version);
            if (setupPath is null)
            {
                return false;
            }

            // Die Signatur zuerst: Sie ist 64 Byte groß, und ohne sie lohnt der Download der
            // Setup-Datei nicht.
            byte[]? signature = await DownloadSignatureAsync(update.SignatureUrl, cancellationToken).ConfigureAwait(false);
            if (signature is null)
            {
                return false;
            }

            await DownloadToFileAsync(update.DownloadUrl, setupPath, onProgress, cancellationToken).ConfigureAwait(false);
            return await StartIfGenuineAsync(setupPath, update.FileSizeBytes, expectedHashBytes, signature, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Prüft Version, beide Quellen und die Prüfsumme, bevor irgendetwas geladen wird.
        /// </summary>
        private bool TryValidateRequest(UpdateInfo update, [NotNullWhen(true)] out byte[]? expectedHashBytes)
        {
            expectedHashBytes = null;

            // Version validieren, bevor sie in den Pfad fließt, sonst wäre Path-Traversal über
            // einen manipulierten Release-Tag möglich (z. B. "../../foo").
            if (!VersionPattern().IsMatch(update.Version))
            {
                _logger.Warning("Ungültiges Versionsformat im Update-Tag — Download abgelehnt: \"{Version}\"", update.Version);
                return false;
            }

            // Der Hash wird vor dem Download geprüft, nicht danach: Fehlt oder taugt er nicht,
            // gibt es keinen Grund, die ganze Setup-Datei zu laden.
            return IsAllowedDownloadSource(update.DownloadUrl, "Setup-Datei")
                && IsAllowedDownloadSource(update.SignatureUrl, "Signatur")
                && TryParseExpectedHash(update.ExpectedSha256, out expectedHashBytes);
        }

        /// <summary>Der Pfad der Setup-Datei im Temp-Ordner, oder leer, wenn er dort nicht läge.</summary>
        private string? ResolveSetupPath(string version)
        {
            string tempDirectory = Path.GetTempPath();
            string tempPath = Path.Combine(tempDirectory, $"EchoPlay-Setup-{version}.exe");

            // Defense-in-Depth: trotz Whitelist-Regex bestätigen, dass der finale Pfad
            // wirklich im %TEMP%-Verzeichnis liegt. Das schützt gegen Edge-Cases (z. B. wenn
            // das Regex erweitert wird und versehentlich Trennzeichen durchlässt).
            if (SecurePathHelper.IsPathInside(tempPath, tempDirectory))
            {
                return tempPath;
            }

            _logger.Warning("Setup-Pfad liegt außerhalb von TEMP — Download abgelehnt: \"{TempPath}\"", EchoPlay.Core.Logging.PathRedactor.Redact(tempPath));
            return null;
        }

        /// <summary>
        /// Lädt die losgelöste Signatur. Alles, was nicht genau eine P-256-Signatur lang ist, wird
        /// abgelehnt.
        /// </summary>
        /// <returns>Die Signaturbytes, oder leer.</returns>
        private async Task<byte[]?> DownloadSignatureAsync(Uri signatureUrl, CancellationToken cancellationToken)
        {
            HttpClient httpClient = _httpClientFactory.CreateClient(DownloadClientName);
            using HttpResponseMessage response = await httpClient
                .GetAsync(signatureUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            _ = response.EnsureSuccessStatusCode();

            // Ein Byte mehr als erlaubt lesen, um eine zu lange Antwort zu erkennen, ohne sie ganz
            // zu laden: Die Antwort kommt aus dem Netz und könnte beliebig groß sein.
            byte[] buffer = new byte[ReleaseSignatureVerifier.SignatureLength + 1];
            await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            int read = await content.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

            if (read != ReleaseSignatureVerifier.SignatureLength)
            {
                _logger.Warning(
                    "Signatur der Setup-Datei hat nicht die erwartete Länge von {ExpectedLength} Byte (gelesen: {ReadLength}) — Update abgelehnt.",
                    ReleaseSignatureVerifier.SignatureLength,
                    read);
                return null;
            }

            return buffer[..read];
        }

        /// <summary>Lädt die Setup-Datei herunter und meldet den Fortschritt.</summary>
        private async Task DownloadToFileAsync(Uri downloadUrl, string tempPath, Action<double>? onProgress, CancellationToken cancellationToken)
        {
            HttpClient httpClient = _httpClientFactory.CreateClient(DownloadClientName);
            using HttpResponseMessage response = await httpClient
                .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            _ = response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            long downloadedBytes = 0;

            // Der Strom schließt am Ende dieser Methode, damit die Prüfungen danach gegen die
            // fertige Datei laufen.
            await using FileStream fileStream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            // Dieselbe Größe wie bei Stream.CopyToAsync; sie bleibt knapp unter der Grenze, ab
            // der ein Array auf dem Heap für große Objekte landet.
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
        }

        /// <summary>
        /// Öffnet die Setup-Datei gesperrt, prüft sie und startet den Installer, solange die Sperre
        /// noch gilt. Eine Datei, die die Prüfung nicht besteht, wird gelöscht.
        /// </summary>
        /// <returns>True, wenn der Installer gestartet wurde.</returns>
        private async Task<bool> StartIfGenuineAsync(
            string setupPath, long expectedFileSize, byte[] expectedHashBytes, byte[] signature, CancellationToken cancellationToken)
        {
            // Lesen dürfen andere, schreiben niemand: Zwischen der Prüfung und dem Start des
            // Installers ließe sich die Datei im Temp-Ordner sonst austauschen. Der Installer
            // selbst öffnet sie nur lesend und kommt an der Sperre vorbei.
            await using (FileStream locked = new(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (await IsGenuineAsync(locked, expectedFileSize, expectedHashBytes, signature, cancellationToken).ConfigureAwait(false))
                {
                    return _installerLauncher.Start(setupPath);
                }
            }

            TryDelete(setupPath);
            return false;
        }

        /// <summary>Prüft Größe, Prüfsumme und Signatur der geöffneten Setup-Datei.</summary>
        private async Task<bool> IsGenuineAsync(
            FileStream setup, long expectedFileSize, byte[] expectedHashBytes, byte[] signature, CancellationToken cancellationToken)
        {
            // Größenvergleich gegen die im Release-Asset gemeldete Größe. Fängt abgebrochene
            // Downloads ab, bevor die ganze Datei gehasht wird.
            if (expectedFileSize > 0 && setup.Length != expectedFileSize)
            {
                _logger.Warning("Setup-Dateigröße weicht ab — erwartet {ExpectedFileSize}, geladen {DownloadedBytes}. Datei wird gelöscht.", expectedFileSize, setup.Length);
                return false;
            }

            // Ein Durchgang über die Datei liefert den Hash für beide Prüfungen.
            byte[] actualHash = await SHA256.HashDataAsync(setup, cancellationToken).ConfigureAwait(false);

            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHashBytes))
            {
                _logger.Warning("SHA-256 der Setup-Datei stimmt nicht mit dem Release-Body überein — erwartet {ExpectedHash}, berechnet {ActualHash}. Datei wird gelöscht.", Convert.ToHexString(expectedHashBytes), Convert.ToHexString(actualHash));
                return false;
            }

            if (!_signatureVerifier.VerifyDigest(actualHash, signature))
            {
                _logger.Warning("Signatur der Setup-Datei ist ungültig — Update stammt nicht vom Herausgeber und wird nicht installiert. Datei wird gelöscht.");
                return false;
            }

            _logger.Info("Setup-Datei geprüft: Prüfsumme und Signatur gültig.");
            return true;
        }

        /// <summary>
        /// Prüft eine Adresse auf HTTPS und einen erlaubten GitHub-Release-Host.
        /// </summary>
        /// <param name="parsed">Die Adresse aus dem Release-Feed.</param>
        /// <param name="kind">Was unter der Adresse liegt, für das Protokoll.</param>
        /// <returns>True, wenn von dieser Adresse geladen werden darf.</returns>
        private bool IsAllowedDownloadSource(Uri parsed, string kind)
        {
            if (!parsed.IsAbsoluteUri)
            {
                _logger.Warning("Update-Adresse ({Kind}) ist keine absolute URL — Download abgelehnt.", kind);
                return false;
            }

            if (parsed.Scheme != Uri.UriSchemeHttps)
            {
                _logger.Warning("Update-Adresse ({Kind}) nutzt nicht HTTPS (\"{Scheme}\") — Download abgelehnt.", kind, parsed.Scheme);
                return false;
            }

            if (!Array.Exists(AllowedDownloadHosts, host => string.Equals(host, parsed.Host, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.Warning("Update-Adresse ({Kind}) zeigt auf einen fremden Host (\"{Host}\") — Download abgelehnt.", kind, parsed.Host);
                return false;
            }

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
        /// Seit die Signatur die Herkunft belegt, ist der Hash die zweite Linie. Er bleibt Pflicht,
        /// weil er billig ist und ein Release mit falscher Hash-Zeile auf einen Fehler beim
        /// Veröffentlichen hinweist, den man sehen will, statt ihn zu übergehen.
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
        /// Entfernt eine heruntergeladene Setup-Datei, soweit das geht. Eine liegen gebliebene
        /// Datei im Temp-Verzeichnis ist harmlos; das Aufräumen darf den Aufrufer nicht stören.
        /// </summary>
        /// <param name="path">Pfad der Setup-Datei.</param>
        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException ex)
            {
                // Die Datei ist noch geöffnet, etwa vom Virenscanner.
                _logger.Warning("Aufräumen der Setup-Datei fehlgeschlagen: {Reason}", ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Warning("Aufräumen der Setup-Datei fehlgeschlagen: {Reason}", ex.Message);
            }
        }
    }
}

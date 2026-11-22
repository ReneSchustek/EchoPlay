using EchoPlay.Core.Http;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Prüft beim App-Start über die GitHub Releases API, ob eine neuere Version verfügbar ist.
    /// Berücksichtigt den Offline-Modus und die vom Nutzer übersprungene Version.
    /// </summary>
    public sealed partial class UpdateCheckService
    {
        /// <summary>GitHub-Repository im Format "owner/repo".</summary>
        private const string GitHubRepo = "ReneSchustek/EchoPlay";

        /// <summary>Endung der losgelösten Signatur, die neben der Setup-Datei am Release hängt.</summary>
        private const string SignatureExtension = ".sig";

        // Erkennt im Release-Body Zeilen wie „SHA256: <hex>" oder „SHA-256 = <hex>", ohne Rücksicht
        // auf Groß- und Kleinschreibung. Pflicht: 64 Hex-Zeichen, Wortgrenze danach. Mehrzeilig,
        // damit ^ jeden Zeilenanfang trifft. Der Text kommt aus dem Netz, deshalb die Zeitgrenze.
        [GeneratedRegex(@"^\s*SHA-?256\s*[:=]\s*([0-9a-fA-F]{64})\b", RegexOptions.Multiline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
        private static partial Regex Sha256Pattern();

        // Drei oder mehr Zeilenumbrüche in Folge; bleibt übrig, wo die Hash-Zeile stand.
        [GeneratedRegex(@"(\r?\n){3,}", RegexOptions.None, matchTimeoutMilliseconds: 500)]
        private static partial Regex BlankLineRunPattern();

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Service mit der ScopeFactory für DB-Zugriffe.
        /// </summary>
        /// <param name="scopeFactory">Für scoped AppSettings-Abfrage.</param>
        /// <param name="httpClientFactory">Fabrik für benannte HTTP-Clients.</param>
        /// <param name="loggerFactory">Logger-Fabrik für Job-Scopes und Diagnose.</param>
        public UpdateCheckService(IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _httpClientFactory = httpClientFactory;
            _logger = loggerFactory.CreateLogger(nameof(UpdateCheckService));
        }

        /// <summary>
        /// Prüft ob eine neuere Version auf GitHub verfügbar ist.
        /// Gibt <see langword="null"/> zurück wenn kein Update nötig ist, der Nutzer
        /// die Version übersprungen hat, oder ein Fehler auftritt.
        /// </summary>
        /// <returns>Update-Informationen oder null.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "GitHub-Release-Check: HttpRequestException, TaskCanceledException, JsonException oder unerwartete HTTP-Codes (Rate-Limit, 5xx) dürfen die App nicht stören – null signalisiert 'kein Update-Check möglich'.")]
        public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Offline-Modus: kein Update-Check
                using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.UpdateCheck);
                using IServiceScope scope = _scopeFactory.CreateScope();
                IAppSettingsDataService settingsService = scope.ServiceProvider
                    .GetRequiredService<IAppSettingsDataService>();
                AppSettings settings = await settingsService.GetAsync(cancellationToken);

                if (settings.OfflineMode)
                {
                    return null;
                }

                GitHubRelease? release = await FetchLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
                string? remoteVersion = NewerVersionOf(release, settings.SkippedUpdateVersion);
                return remoteVersion is null ? null : ToUpdateInfo(release!, remoteVersion);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ein Abbruch des Aufrufers ist kein Fehlschlag und gehört nach oben.
                throw;
            }
            catch (Exception ex)
            {
                // Netzwerkfehler, Zeitüberschreitung, unlesbare Antwort: kein Update, keine Meldung an
                // den Nutzer, aber eine Zeile im Protokoll, sonst bliebe ein dauerhaft scheiternder
                // Check unbemerkt.
                _logger.Warning("Update-Prüfung fehlgeschlagen: {Reason}", ex.Message);
                return null;
            }
        }

        /// <summary>Holt das jüngste Release von GitHub.</summary>
        private async Task<GitHubRelease?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
        {
            // Die Frist setzt allein die Wiederholungsregel des Clients "UpdateCheck"; eine
            // zweite hier schnitte deren Wiederholungen ab.
            string url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";

            HttpClient client = _httpClientFactory.CreateClient("UpdateCheck");
            return await client
                .GetFromJsonAsync(url, GitHubJsonContext.Default.GitHubRelease, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Die Version des Release, wenn sie neuer ist als die laufende und nicht übersprungen
        /// wurde; sonst leer.
        /// </summary>
        private string? NewerVersionOf(GitHubRelease? release, string? skippedVersion)
        {
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return null;
            }

            // Tag-Version parsen (z.B. "v1.2.0" → "1.2.0")
            string remoteVersion = release.TagName.TrimStart('v', 'V');

            Version? current = GetCurrentVersion();
            if (current is null || !Version.TryParse(remoteVersion, out Version? remote))
            {
                return null;
            }

            // Kein Update nötig wenn aktuelle Version gleich oder neuer; eine bewusst
            // übersprungene Version wird nicht wieder angeboten.
            bool skipped = !string.IsNullOrWhiteSpace(skippedVersion) && skippedVersion == remoteVersion;
            return remote > current && !skipped ? remoteVersion : null;
        }

        /// <summary>
        /// Baut das Angebot aus dem Release: Setup-Datei, Prüfsumme, Signatur und Hinweise. Fehlt
        /// eines davon, wird nichts angeboten.
        /// </summary>
        private UpdateInfo? ToUpdateInfo(GitHubRelease release, string remoteVersion)
        {
            // Setup-Datei im Release suchen (.exe-Asset)
            GitHubAsset[] assets = release.Assets ?? [];
            GitHubAsset? setupAsset = assets
                .FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (setupAsset is null)
            {
                return null;
            }

            string releaseBody = release.Body ?? string.Empty;
            string expectedSha256 = ExtractSha256(releaseBody);
            Uri? downloadUrl = InstallableAddress(remoteVersion, expectedSha256, setupAsset);
            Uri? signatureUrl = downloadUrl is null ? null : SignatureAddress(remoteVersion, assets, setupAsset);
            return downloadUrl is null || signatureUrl is null
                ? null
                : new UpdateInfo(
                    Version: remoteVersion,
                    ReleaseNotes: StripSha256Line(releaseBody),
                    DownloadUrl: downloadUrl,
                    FileSizeBytes: setupAsset.Size,
                    ExpectedSha256: expectedSha256,
                    SignatureUrl: signatureUrl);
        }

        /// <summary>
        /// Die Adresse der Signatur zur Setup-Datei: das Asset, das deren Namen mit der Endung
        /// <c>.sig</c> trägt. Fehlt es, ist das Release nicht installierbar; dann leer, mit einer
        /// Warnung im Protokoll.
        /// </summary>
        private Uri? SignatureAddress(string remoteVersion, GitHubAsset[] assets, GitHubAsset setupAsset)
        {
            // Zugeordnet wird über den Namen, nicht über „irgendeine .sig“: Eine Signatur gilt für
            // genau die Datei, deren Namen sie trägt. Ohne Signatur kein Angebot, denn der
            // Downloader installiert ohne gültige Signatur ohnehin nicht.
            string signatureName = setupAsset.Name + SignatureExtension;
            GitHubAsset? signatureAsset = assets
                .FirstOrDefault(a => string.Equals(a.Name, signatureName, StringComparison.OrdinalIgnoreCase));
            Uri? signatureUrl = signatureAsset is null ? null : WebAddress.TryParse(signatureAsset.BrowserDownloadUrl);

            if (signatureUrl is null)
            {
                _logger.Warning(
                    "Release {Version} hat keine lesbare Signatur {SignatureName} — wird nicht als Update angeboten.",
                    remoteVersion,
                    signatureName);
            }

            return signatureUrl;
        }

        /// <summary>
        /// Die Adresse der Setup-Datei, wenn das Release installierbar ist: mit Prüfsumme und
        /// lesbarer Adresse. Sonst leer, mit einer Warnung im Protokoll.
        /// </summary>
        private Uri? InstallableAddress(string remoteVersion, string expectedSha256, GitHubAsset setupAsset)
        {
            // Ohne brauchbaren Hash ist das Release nicht installierbar, der Downloader
            // lehnt es ohnehin ab. Es trotzdem anzubieten heißt, den Nutzer zu einem
            // Update einzuladen, das systematisch scheitert. Die Warnung bleibt, damit ein
            // Release mit vergessener Hash-Zeile diagnostizierbar ist.
            if (string.IsNullOrEmpty(expectedSha256))
            {
                _logger.Warning(
                    "Release {Version} hat keine gültige SHA-256-Zeile im Body — wird nicht als Update angeboten.",
                    remoteVersion);
                return null;
            }

            Uri? downloadUrl = WebAddress.TryParse(setupAsset.BrowserDownloadUrl);
            if (downloadUrl is null)
            {
                _logger.Warning(
                    "Release {Version} nennt keine lesbare Adresse für die Setup-Datei — wird nicht als Update angeboten.",
                    remoteVersion);
            }

            return downloadUrl;
        }

        /// <summary>
        /// Speichert eine Version als übersprungen in den Einstellungen.
        /// </summary>
        /// <param name="version">Die zu überspringende Versionsnummer.</param>
        /// <param name="cancellationToken">Abbruch-Token (z. B. App-Shutdown).</param>
        public async Task SkipVersionAsync(string version, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider
                .GetRequiredService<IAppSettingsDataService>();
            AppSettings settings = await settingsService.GetAsync(cancellationToken);
            settings.SkippedUpdateVersion = version;

            cancellationToken.ThrowIfCancellationRequested();
            await settingsService.SaveAsync(settings, cancellationToken);
        }

        /// <summary>
        /// Ermittelt die aktuelle App-Version aus den Assembly-Metadaten.
        /// </summary>
        private static Version? GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version;
        }

        /// <summary>
        /// Extrahiert den SHA-256-Hash der Setup-Datei aus dem Release-Body.
        /// Erwartete Konvention: eine Zeile <c>SHA256: &lt;64-hex&gt;</c> oder <c>SHA-256 = &lt;64-hex&gt;</c>.
        /// Liefert leeren String, wenn kein Hash gefunden wurde. Ein solches Release wird
        /// weder angeboten noch installiert.
        /// </summary>
        /// <param name="releaseBody">Markdown-Body des GitHub-Releases.</param>
        /// <returns>Hex-String (64 Zeichen, beliebige Groß-/Kleinschreibung) oder leer. Der Vergleich im Updater nutzt <c>Convert.FromHexString</c> und ist daher case-insensitive. Eine Normalisierung an dieser Stelle wäre Overhead und würde CA1308 auslösen.</returns>
        internal static string ExtractSha256(string releaseBody)
        {
            if (string.IsNullOrEmpty(releaseBody))
            {
                return string.Empty;
            }

            Match match = Sha256Pattern().Match(releaseBody);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        /// <summary>
        /// Entfernt die <c>SHA256:</c>-Zeile aus dem Release-Body, damit sie nicht im
        /// Update-Dialog landet.
        /// </summary>
        /// <remarks>
        /// Der Hash ist eine Angabe für den Updater, keine Release-Notiz: 64 Hex-Zeichen sagen
        /// dem Nutzer nichts und verdrängen im Dialog, was ihn interessiert. Er wird separat in
        /// <see cref="UpdateInfo.ExpectedSha256"/> geführt und dort geprüft. Hier fällt nur die
        /// Anzeige weg. Die Überschrift „Integritätsprüfung", unter der die Zeile im
        /// Release-Text steht, verliert damit ihren Inhalt und fliegt als leer gewordener
        /// Absatz mit heraus.
        /// </remarks>
        /// <param name="releaseBody">Markdown-Body des GitHub-Releases.</param>
        /// <returns>Der Body ohne Hash-Zeile, ohne verwaiste Leerzeilen am Rand.</returns>
        internal static string StripSha256Line(string releaseBody)
        {
            if (string.IsNullOrEmpty(releaseBody))
            {
                return string.Empty;
            }

            string withoutHash = Sha256Pattern().Replace(releaseBody, string.Empty);

            // Mehr als eine Leerzeile in Folge entsteht genau dort, wo die Zeile stand.
            return BlankLineRunPattern().Replace(withoutHash, "\n\n").Trim();
        }

        /// <summary>
        /// Die Felder der GitHub-Antwort auf releases/latest, die die Prüfung braucht:
        /// Versionsschild, Text mit der SHA256-Zeile und die angehängten Dateien.
        /// </summary>
        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("body")]
            public string? Body { get; set; }

            [JsonPropertyName("assets")]
            public GitHubAsset[]? Assets { get; set; }
        }

        /// <summary>
        /// Eine angehängte Datei des Release. Als Setup gilt die erste mit der Endung .exe, als ihre
        /// Signatur die Datei mit demselben Namen und der Endung .sig.
        /// </summary>
        private sealed class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;

            [JsonPropertyName("size")]
            public long Size { get; set; }
        }

        /// <summary>
        /// Source-Generator-Kontext für Trimming-sichere JSON-Deserialisierung der GitHub-API-DTOs.
        /// </summary>
        [JsonSerializable(typeof(GitHubRelease))]
        private sealed partial class GitHubJsonContext : JsonSerializerContext;
    }
}

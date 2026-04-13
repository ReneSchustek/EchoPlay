using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        /// <summary>Maximale Wartezeit für die GitHub-API-Abfrage.</summary>
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// Initialisiert den Service mit der ScopeFactory für DB-Zugriffe.
        /// </summary>
        /// <param name="scopeFactory">Für scoped AppSettings-Abfrage.</param>
        /// <param name="httpClientFactory">Fabrik für benannte HTTP-Clients.</param>
        public UpdateCheckService(IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory)
        {
            _scopeFactory = scopeFactory;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Prüft ob eine neuere Version auf GitHub verfügbar ist.
        /// Gibt <see langword="null"/> zurück wenn kein Update nötig ist, der Nutzer
        /// die Version übersprungen hat, oder ein Fehler auftritt.
        /// </summary>
        /// <returns>Update-Informationen oder null.</returns>
        public async Task<UpdateInfo?> CheckForUpdateAsync()
        {
            try
            {
                // Offline-Modus: kein Update-Check
                using IServiceScope scope = _scopeFactory.CreateScope();
                IAppSettingsDataService settingsService = scope.ServiceProvider
                    .GetRequiredService<IAppSettingsDataService>();
                AppSettings settings = await settingsService.GetAsync();

                if (settings.OfflineMode)
                {
                    return null;
                }

                using CancellationTokenSource cts = new(RequestTimeout);
                string url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";

                HttpClient client = _httpClientFactory.CreateClient("UpdateCheck");
                GitHubRelease? release = await client
                    .GetFromJsonAsync(url, GitHubJsonContext.Default.GitHubRelease, cts.Token)
                    .ConfigureAwait(false);

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

                // Kein Update nötig wenn aktuelle Version gleich oder neuer
                if (remote <= current)
                {
                    return null;
                }

                // Nutzer hat diese Version bewusst übersprungen
                if (!string.IsNullOrWhiteSpace(settings.SkippedUpdateVersion)
                    && settings.SkippedUpdateVersion == remoteVersion)
                {
                    return null;
                }

                // Setup-Datei im Release suchen (.exe-Asset)
                GitHubAsset? setupAsset = release.Assets?
                    .FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                if (setupAsset is null)
                {
                    return null;
                }

                return new UpdateInfo(
                    Version: remoteVersion,
                    ReleaseNotes: release.Body ?? string.Empty,
                    DownloadUrl: setupAsset.BrowserDownloadUrl,
                    FileSizeBytes: setupAsset.Size);
            }
            catch
            {
                // Netzwerkfehler, Timeout, JSON-Fehler → kein Update-Check, kein Fehler für den Nutzer
                return null;
            }
        }

        /// <summary>
        /// Speichert eine Version als übersprungen in den Einstellungen.
        /// </summary>
        /// <param name="version">Die zu überspringende Versionsnummer.</param>
        public async Task SkipVersionAsync(string version)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider
                .GetRequiredService<IAppSettingsDataService>();
            AppSettings settings = await settingsService.GetAsync();
            settings.SkippedUpdateVersion = version;
            await settingsService.SaveAsync(settings);
        }

        /// <summary>
        /// Ermittelt die aktuelle App-Version aus den Assembly-Metadaten.
        /// </summary>
        private static Version? GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version;
        }

        // ── GitHub-API-DTOs ─────────────────────────────────────────────────────

        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("body")]
            public string? Body { get; set; }

            [JsonPropertyName("assets")]
            public GitHubAsset[]? Assets { get; set; }
        }

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

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Sucht Cover über die kostenlose Discogs-Datenbank-API.
    /// Discogs hat eine große Sammlung physischer Releases (Vinyl, CD, Kassette) mit Scans –
    /// besonders wertvoll für ältere Hörspielserien, die auf den Streaming-Plattformen fehlen.
    /// </summary>
    /// <remarks>
    /// Endpunkt: <c>https://api.discogs.com/database/search?q={query}&amp;type=release</c>
    /// Kein API-Key nötig für Basis-Zugriff (60 Requests/Minute ohne Token).
    /// Ein <c>User-Agent</c>-Header ist Pflicht, sonst antwortet Discogs mit HTTP 403.
    /// Die Thumbnails sind klein (~150 px), das Vollbild wird über die Release-Detail-URL geladen.
    /// </remarks>
    public sealed class DiscogsCoverSearchService : ICoverSearchService
    {
        private readonly HttpClient _httpClient;

        /// <summary>Maximal 9 Treffer pro Suche.</summary>
        private const int MaxResults = 9;

        /// <summary>
        /// Initialisiert den Service mit einem vorkonfigurierten <see cref="HttpClient"/>.
        /// Der Client muss einen gültigen <c>User-Agent</c>-Header tragen.
        /// </summary>
        /// <param name="httpClient">HTTP-Client, bereitgestellt durch IHttpClientFactory.</param>
        public DiscogsCoverSearchService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return [];
            }

            DiscogsSearchResponse? response;

            try
            {
                string encodedTitle = Uri.EscapeDataString(title);
                string url = $"https://api.discogs.com/database/search?q={encodedTitle}&type=release&per_page={MaxResults}";

                response = await _httpClient.GetFromJsonAsync<DiscogsSearchResponse>(url, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return [];
            }

            if (response?.Results is not { Count: > 0 })
            {
                return [];
            }

            List<CoverSearchResult> results = [];

            foreach (DiscogsRelease release in response.Results)
            {
                // Discogs liefert ein Thumbnail und eine Cover-URL.
                // Manche Releases haben kein Bild – die filtern wir raus.
                if (string.IsNullOrWhiteSpace(release.CoverImage)
                    || string.IsNullOrWhiteSpace(release.Thumb))
                {
                    continue;
                }

                string releaseTitle = release.Title ?? title;

                results.Add(new CoverSearchResult(
                    ThumbnailUrl: release.Thumb,
                    FullUrl:      release.CoverImage,
                    ReleaseTitle: releaseTitle,
                    Source:       "Discogs"));
            }

            return results;
        }

        // ── Interne DTO-Klassen für die JSON-Deserialisierung ─────────────────────

        private sealed class DiscogsSearchResponse
        {
            [JsonPropertyName("results")]
            public List<DiscogsRelease>? Results { get; set; }
        }

        private sealed class DiscogsRelease
        {
            [JsonPropertyName("title")]
            public string? Title { get; set; }

            /// <summary>Kleines Vorschaubild (~150 px).</summary>
            [JsonPropertyName("thumb")]
            public string? Thumb { get; set; }

            /// <summary>Vollbild des Release-Covers.</summary>
            [JsonPropertyName("cover_image")]
            public string? CoverImage { get; set; }
        }
    }
}

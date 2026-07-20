using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
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
    public sealed class DiscogsCoverSearchService : JsonCoverSearchServiceBase
    {
        /// <summary>
        /// Initialisiert den Service mit einem vorkonfigurierten <see cref="HttpClient"/>.
        /// Der Client muss einen gültigen <c>User-Agent</c>-Header tragen.
        /// </summary>
        /// <param name="httpClient">HTTP-Client, bereitgestellt durch IHttpClientFactory.</param>
        public DiscogsCoverSearchService(HttpClient httpClient) : base(httpClient)
        {
        }

        /// <inheritdoc/>
        public override Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CancellationToken ct = default) =>
            SearchJsonAsync<DiscogsSearchResponse, DiscogsRelease>(
                title,
                static (encodedTitle, maxResults) => $"https://api.discogs.com/database/search?q={encodedTitle}&type=release&per_page={maxResults}",
                static response => response.Results,
                MapRelease,
                ct);

        private static CoverSearchResult? MapRelease(DiscogsRelease release, string fallbackTitle)
        {
            // Discogs liefert ein Thumbnail und eine Cover-URL.
            // Manche Releases haben kein Bild – die filtern wir raus.
            if (string.IsNullOrWhiteSpace(release.CoverImage)
                || string.IsNullOrWhiteSpace(release.Thumb))
            {
                return null;
            }

            string releaseTitle = release.Title ?? fallbackTitle;

            return new CoverSearchResult(
                ThumbnailUrl: release.Thumb,
                FullUrl: release.CoverImage,
                ReleaseTitle: releaseTitle,
                Source: "Discogs");
        }

        // ── Interne DTO-Klassen für die JSON-Deserialisierung ─────────────────────

        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "DTO wird von System.Text.Json via Deserialize<T> per Reflection instanziiert.")]
        private sealed class DiscogsSearchResponse
        {
            [JsonPropertyName("results")]
            public List<DiscogsRelease>? Results { get; set; }
        }

        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "DTO wird von System.Text.Json via Deserialize<T> per Reflection instanziiert.")]
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

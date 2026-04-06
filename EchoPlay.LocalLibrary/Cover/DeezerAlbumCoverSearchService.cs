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
    /// Sucht Album-Cover über die kostenlose Deezer-API.
    /// Dritte Quelle für Folgen-Cover neben iTunes und Cover Art Archive –
    /// nützlich wenn eine Hörspielfolge nur bei Deezer gelistet ist.
    /// </summary>
    /// <remarks>
    /// Endpunkt: <c>https://api.deezer.com/search/album?q={query}</c>
    /// Kein API-Key nötig. Rate-Limit: 50 Requests pro 5 Sekunden.
    /// Die Cover kommen in festen Größen: <c>cover_medium</c> (250×250),
    /// <c>cover_big</c> (500×500), <c>cover_xl</c> (1000×1000).
    /// </remarks>
    public sealed class DeezerAlbumCoverSearchService : ICoverSearchService
    {
        private readonly HttpClient _httpClient;

        /// <summary>Maximal 9 Treffer – passt in ein 3×3-Grid im Auswahldialog.</summary>
        private const int MaxResults = 9;

        /// <summary>
        /// Initialisiert den Service mit einem vorkonfigurierten <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="httpClient">HTTP-Client, bereitgestellt durch IHttpClientFactory.</param>
        public DeezerAlbumCoverSearchService(HttpClient httpClient)
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

            DeezerSearchResponse? response;

            try
            {
                string encodedTitle = Uri.EscapeDataString(title);
                string url = $"https://api.deezer.com/search/album?q={encodedTitle}&limit={MaxResults}";

                response = await _httpClient.GetFromJsonAsync<DeezerSearchResponse>(url, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return [];
            }

            if (response?.Data is not { Count: > 0 })
            {
                return [];
            }

            List<CoverSearchResult> results = [];

            foreach (DeezerAlbum album in response.Data)
            {
                if (string.IsNullOrWhiteSpace(album.CoverMedium)
                    || string.IsNullOrWhiteSpace(album.CoverXl))
                {
                    continue;
                }

                string albumTitle = album.Title ?? title;

                results.Add(new CoverSearchResult(
                    ThumbnailUrl: album.CoverMedium,
                    FullUrl:      album.CoverXl,
                    ReleaseTitle: albumTitle,
                    Source:       "Deezer (Album)"));
            }

            return results;
        }

        // ── Interne DTO-Klassen für die JSON-Deserialisierung ─────────────────────

        private sealed class DeezerSearchResponse
        {
            [JsonPropertyName("data")]
            public List<DeezerAlbum>? Data { get; set; }
        }

        private sealed class DeezerAlbum
        {
            [JsonPropertyName("title")]
            public string? Title { get; set; }

            /// <summary>250×250 Pixel – Vorschau im Auswahldialog.</summary>
            [JsonPropertyName("cover_medium")]
            public string? CoverMedium { get; set; }

            /// <summary>1000×1000 Pixel – wird als Cover gespeichert.</summary>
            [JsonPropertyName("cover_xl")]
            public string? CoverXl { get; set; }
        }
    }
}

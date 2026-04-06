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
    /// Sucht Künstler-/Serienbilder über die kostenlose Deezer-API.
    /// Ideal für Serien-Cover, da Deezer Künstler-Profilbilder in mehreren Größen liefert –
    /// im Gegensatz zu iTunes und Cover Art Archive, die nur Album-Cover zurückgeben.
    /// </summary>
    /// <remarks>
    /// Endpunkt: <c>https://api.deezer.com/search/artist?q={query}</c>
    /// Kein API-Key nötig. Rate-Limit: 50 Requests pro 5 Sekunden.
    /// Die Bilder kommen in festen Größen: <c>picture_medium</c> (250×250),
    /// <c>picture_big</c> (500×500), <c>picture_xl</c> (1000×1000).
    /// </remarks>
    public sealed class DeezerArtistCoverSearchService : ICoverSearchService
    {
        private readonly HttpClient _httpClient;

        /// <summary>Maximal 6 Künstlertreffer – Serienbilder sind meist im ersten Ergebnis.</summary>
        private const int MaxResults = 6;

        /// <summary>
        /// Initialisiert den Service mit einem vorkonfigurierten <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="httpClient">HTTP-Client, bereitgestellt durch IHttpClientFactory.</param>
        public DeezerArtistCoverSearchService(HttpClient httpClient)
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
                string url = $"https://api.deezer.com/search/artist?q={encodedTitle}&limit={MaxResults}";

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

            foreach (DeezerArtist artist in response.Data)
            {
                // Deezer liefert einen Platzhalter-URL wenn kein Bild vorhanden ist –
                // diese enthalten "/artist//", ohne ID dazwischen
                if (string.IsNullOrWhiteSpace(artist.PictureMedium)
                    || string.IsNullOrWhiteSpace(artist.PictureXl))
                {
                    continue;
                }

                string artistName = artist.Name ?? title;

                results.Add(new CoverSearchResult(
                    ThumbnailUrl: artist.PictureMedium,
                    FullUrl:      artist.PictureXl,
                    ReleaseTitle: artistName,
                    Source:       "Deezer (Künstler)"));
            }

            return results;
        }

        // ── Interne DTO-Klassen für die JSON-Deserialisierung ─────────────────────

        private sealed class DeezerSearchResponse
        {
            [JsonPropertyName("data")]
            public List<DeezerArtist>? Data { get; set; }
        }

        private sealed class DeezerArtist
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            /// <summary>250×250 Pixel – Vorschau im Auswahldialog.</summary>
            [JsonPropertyName("picture_medium")]
            public string? PictureMedium { get; set; }

            /// <summary>1000×1000 Pixel – wird als Serien-Cover gespeichert.</summary>
            [JsonPropertyName("picture_xl")]
            public string? PictureXl { get; set; }
        }
    }
}

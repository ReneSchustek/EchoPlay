using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Sucht Cover-Kandidaten über die kostenlose iTunes Search API.
    /// Kein API-Key nötig – die API ist öffentlich und rate-limited auf ca. 20 Requests/Minute.
    /// </summary>
    /// <remarks>
    /// Die iTunes Search API liefert Album-Artwork in festen Größen (100×100 als Standard).
    /// Für höhere Auflösungen wird der URL-Platzhalter manuell ersetzt:
    /// <c>100x100bb</c> → <c>600x600bb</c> für das Vollbild,
    /// <c>100x100bb</c> → <c>250x250bb</c> für das Thumbnail.
    /// </remarks>
    public sealed class ITunesCoverSearchService : ICoverSearchService
    {
        private readonly HttpClient _httpClient;

        /// <summary>Maximal 9 Treffer – passt in ein 3×3-Grid im Auswahldialog.</summary>
        private const int MaxResults = 9;

        /// <summary>
        /// Initialisiert den Service mit einem vorkonfigurierten <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="httpClient">HTTP-Client, bereitgestellt durch IHttpClientFactory.</param>
        public ITunesCoverSearchService(HttpClient httpClient)
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

            ITunesSearchResponse? response;

            try
            {
                string encodedTitle = Uri.EscapeDataString(title);
                string url = $"https://itunes.apple.com/search?term={encodedTitle}&media=music&entity=album&limit={MaxResults}";

                response = await _httpClient.GetFromJsonAsync<ITunesSearchResponse>(url, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException
                                       or TaskCanceledException
                                       or JsonException
                                       or NotSupportedException
                                       or UriFormatException
                                       or InvalidOperationException)
            {
                // Netzwerkfehler oder JSON-Deserialisierung → leere Liste, kein Absturz
                return [];
            }

            if (response?.Results is not { Count: > 0 })
            {
                return [];
            }

            List<CoverSearchResult> results = [];

            foreach (ITunesAlbumResult album in response.Results)
            {
                if (string.IsNullOrWhiteSpace(album.ArtworkUrl100))
                {
                    continue;
                }

                // iTunes liefert 100×100 als Standard – Größe im URL-String austauschen
                string thumbnailUrl = album.ArtworkUrl100.Replace("100x100bb", "250x250bb", StringComparison.Ordinal);
                string fullUrl = album.ArtworkUrl100.Replace("100x100bb", "600x600bb", StringComparison.Ordinal);

                string albumTitle = album.CollectionName ?? title;

                results.Add(new CoverSearchResult(
                    ThumbnailUrl: thumbnailUrl,
                    FullUrl: fullUrl,
                    ReleaseTitle: albumTitle,
                    Source: "iTunes"));
            }

            return results;
        }

        // ── Interne DTO-Klassen für die JSON-Deserialisierung ─────────────────────

        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "DTO wird von System.Text.Json via Deserialize<T> per Reflection instanziiert.")]
        private sealed class ITunesSearchResponse
        {
            [JsonPropertyName("results")]
            public List<ITunesAlbumResult>? Results { get; set; }
        }

        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "DTO wird von System.Text.Json via Deserialize<T> per Reflection instanziiert.")]
        private sealed class ITunesAlbumResult
        {
            [JsonPropertyName("artworkUrl100")]
            public string? ArtworkUrl100 { get; set; }

            [JsonPropertyName("collectionName")]
            public string? CollectionName { get; set; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
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
    public sealed class ITunesCoverSearchService : JsonCoverSearchServiceBase
    {
        /// <summary>
        /// Initialisiert den Service mit einem vorkonfigurierten <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="httpClient">HTTP-Client, bereitgestellt durch IHttpClientFactory.</param>
        public ITunesCoverSearchService(HttpClient httpClient) : base(httpClient)
        {
        }

        /// <inheritdoc/>
        public override Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CancellationToken ct = default) =>
            SearchJsonAsync<ITunesSearchResponse, ITunesAlbumResult>(
                title,
                static (encodedTitle, maxResults) => $"https://itunes.apple.com/search?term={encodedTitle}&media=music&entity=album&limit={maxResults}",
                static response => response.Results,
                MapAlbum,
                ct);

        private static CoverSearchResult? MapAlbum(ITunesAlbumResult album, string fallbackTitle)
        {
            if (string.IsNullOrWhiteSpace(album.ArtworkUrl100))
            {
                return null;
            }

            // iTunes liefert 100×100 als Standard – Größe im URL-String austauschen
            string thumbnailUrl = album.ArtworkUrl100.Replace("100x100bb", "250x250bb", StringComparison.Ordinal);
            string fullUrl = album.ArtworkUrl100.Replace("100x100bb", "600x600bb", StringComparison.Ordinal);

            string albumTitle = album.CollectionName ?? fallbackTitle;

            return new CoverSearchResult(
                ThumbnailUrl: thumbnailUrl,
                FullUrl: fullUrl,
                ReleaseTitle: albumTitle,
                Source: "iTunes");
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

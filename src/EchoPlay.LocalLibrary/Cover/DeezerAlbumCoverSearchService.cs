using EchoPlay.Logger.Abstractions;
using EchoPlay.Core.Http;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Sucht Album-Cover über die kostenlose Deezer-API.
    /// Dritte Quelle für Folgen-Cover neben iTunes und Cover Art Archive,
    /// nützlich, wenn eine Hörspielfolge nur bei Deezer gelistet ist.
    /// </summary>
    /// <remarks>
    /// Endpunkt: <c>https://api.deezer.com/search/album?q={query}</c>
    /// Kein API-Key nötig. Rate-Limit: 50 Requests pro 5 Sekunden.
    /// Die Cover kommen in festen Größen: <c>cover_medium</c> (250×250),
    /// <c>cover_big</c> (500×500), <c>cover_xl</c> (1000×1000).
    /// </remarks>
    public sealed class DeezerAlbumCoverSearchService : JsonCoverSearchServiceBase
    {
        /// <summary>
        /// Initialisiert den Service mit einem vorkonfigurierten <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="httpClient">HTTP-Client, bereitgestellt durch IHttpClientFactory.</param>
        /// <param name="loggerFactory">Für das Protokoll übersprungener Anfragen.</param>
        public DeezerAlbumCoverSearchService(HttpClient httpClient, ILoggerFactory loggerFactory) : base(httpClient, loggerFactory)
        {
        }

        /// <inheritdoc/>
        public override Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CoverSearchPage page,
            CancellationToken ct = default) =>
            SearchJsonAsync<DeezerSearchResponse, DeezerAlbum>(
                title,
                page,
                static (encodedTitle, window) => $"https://api.deezer.com/search/album?q={encodedTitle}&limit={window.Limit}&index={window.Offset}",
                static response => response.Data,
                MapAlbum,
                ct);

        private static CoverSearchResult? MapAlbum(DeezerAlbum album, string fallbackTitle)
        {
            Uri? thumbnail = WebAddress.TryParse(album.CoverMedium);
            Uri? full = WebAddress.TryParse(album.CoverXl);
            if (thumbnail is null || full is null)
            {
                return null;
            }

            string albumTitle = album.Title ?? fallbackTitle;

            return new CoverSearchResult(
                ThumbnailUrl: thumbnail,
                FullUrl: full,
                ReleaseTitle: albumTitle,
                Source: "Deezer (Album)");
        }

        /// <summary>
        /// Antwort der Deezer-Albensuche; gelesen wird nur die Trefferliste.
        /// </summary>
        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "DTO wird von System.Text.Json via Deserialize<T> per Reflection instanziiert.")]
        private sealed class DeezerSearchResponse
        {
            [JsonPropertyName("data")]
            public List<DeezerAlbum>? Data { get; set; }
        }

        /// <summary>
        /// Ein Albumtreffer mit Titel und dem Cover in zwei Größen.
        /// </summary>
        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "DTO wird von System.Text.Json via Deserialize<T> per Reflection instanziiert.")]
        private sealed class DeezerAlbum
        {
            [JsonPropertyName("title")]
            public string? Title { get; set; }

            /// <summary>250×250 Pixel für die Vorschau im Auswahldialog.</summary>
            [JsonPropertyName("cover_medium")]
            public string? CoverMedium { get; set; }

            /// <summary>1000×1000 Pixel; wird als Cover gespeichert.</summary>
            [JsonPropertyName("cover_xl")]
            public string? CoverXl { get; set; }
        }
    }
}

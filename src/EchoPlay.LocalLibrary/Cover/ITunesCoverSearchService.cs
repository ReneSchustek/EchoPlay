using EchoPlay.Logger.Abstractions;
using EchoPlay.Core.Http;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Sucht Cover-Kandidaten über die kostenlose iTunes Search API.
    /// Kein API-Key nötig, die API ist öffentlich und rate-limited auf ca. 20 Requests/Minute.
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
        /// <param name="loggerFactory">Für das Protokoll übersprungener Anfragen.</param>
        public ITunesCoverSearchService(HttpClient httpClient, ILoggerFactory loggerFactory) : base(httpClient, loggerFactory)
        {
        }

        /// <summary>
        /// Die iTunes Search API kennt keinen Versatz, nur <c>limit</c>. Nachladen holt deshalb
        /// mehr und verwirft den bereits gezeigten Anfang.
        /// </summary>
        protected override bool SupportsOffset => false;

        /// <inheritdoc/>
        public override Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CoverSearchPage page,
            CancellationToken ct = default) =>
            SearchJsonAsync<ITunesSearchResponse, ITunesAlbumResult>(
                title,
                page,
                static (encodedTitle, window) => $"https://itunes.apple.com/search?term={encodedTitle}&media=music&entity=album&limit={window.Limit}",
                static response => response.Results,
                MapAlbum,
                ct);

        private static CoverSearchResult? MapAlbum(ITunesAlbumResult album, string fallbackTitle)
        {
            // iTunes liefert 100×100 als Standard; die Größe steht im Pfad und wird dort getauscht.
            Uri? thumbnail = WebAddress.TryParse(album.ArtworkUrl100?.Replace("100x100bb", "250x250bb", StringComparison.Ordinal));
            Uri? full = WebAddress.TryParse(album.ArtworkUrl100?.Replace("100x100bb", "600x600bb", StringComparison.Ordinal));
            if (thumbnail is null || full is null)
            {
                return null;
            }

            string albumTitle = album.CollectionName ?? fallbackTitle;

            return new CoverSearchResult(
                ThumbnailUrl: thumbnail,
                FullUrl: full,
                ReleaseTitle: albumTitle,
                Source: "iTunes");
        }

        /// <summary>
        /// Antwort der iTunes-Suche; gelesen wird nur die Trefferliste.
        /// </summary>
        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "DTO wird von System.Text.Json via Deserialize<T> per Reflection instanziiert.")]
        private sealed class ITunesSearchResponse
        {
            [JsonPropertyName("results")]
            public List<ITunesAlbumResult>? Results { get; set; }
        }

        /// <summary>
        /// Ein Albumtreffer. iTunes nennt nur die Adresse in 100 × 100 Pixeln; Vorschau und
        /// Vollbild entstehen durch Umschreiben der Größe in dieser Adresse.
        /// </summary>
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

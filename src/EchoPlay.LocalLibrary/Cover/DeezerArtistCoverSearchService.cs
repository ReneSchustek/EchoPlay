using EchoPlay.Logger.Abstractions;
using EchoPlay.Core.Http;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Sucht Künstler-/Serienbilder über die kostenlose Deezer-API.
    /// Ideal für Serien-Cover, da Deezer Künstler-Profilbilder in mehreren Größen liefert,
    /// im Gegensatz zu iTunes und Cover Art Archive, die nur Album-Cover zurückgeben.
    /// </summary>
    /// <remarks>
    /// Endpunkt: <c>https://api.deezer.com/search/artist?q={query}</c>
    /// Kein API-Key nötig. Rate-Limit: 50 Requests pro 5 Sekunden.
    /// Die Bilder kommen in festen Größen: <c>picture_medium</c> (250×250),
    /// <c>picture_big</c> (500×500), <c>picture_xl</c> (1000×1000).
    /// </remarks>
    public sealed class DeezerArtistCoverSearchService : JsonCoverSearchServiceBase
    {
        /// <summary>
        /// Initialisiert den Service mit einem vorkonfigurierten <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="httpClient">HTTP-Client, bereitgestellt durch IHttpClientFactory.</param>
        /// <param name="loggerFactory">Für das Protokoll übersprungener Anfragen.</param>
        public DeezerArtistCoverSearchService(HttpClient httpClient, ILoggerFactory loggerFactory) : base(httpClient, loggerFactory)
        {
        }

        /// <summary>Maximal 6 Künstlertreffer, denn Serienbilder sind meist im ersten Ergebnis.</summary>
        protected override int MaxResults => 6;

        /// <inheritdoc/>
        public override Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CoverSearchPage page,
            CancellationToken ct = default) =>
            SearchJsonAsync<DeezerSearchResponse, DeezerArtist>(
                title,
                page,
                static (encodedTitle, window) => $"https://api.deezer.com/search/artist?q={encodedTitle}&limit={window.Limit}&index={window.Offset}",
                static response => response.Data,
                MapArtist,
                ct);

        private static bool IsPlaceholder(Uri picture)
            => picture.OriginalString.Contains("/artist//", StringComparison.Ordinal);

        private static CoverSearchResult? MapArtist(DeezerArtist artist, string fallbackTitle)
        {
            Uri? thumbnail = WebAddress.TryParse(artist.PictureMedium);
            Uri? full = WebAddress.TryParse(artist.PictureXl);

            // Ohne eigenes Bild liefert Deezer die Adresse eines grauen Platzhalters; ihr fehlt
            // die Bildkennung, erkennbar am leeren Abschnitt „/artist//".
            if (thumbnail is null || full is null || IsPlaceholder(full))
            {
                return null;
            }

            string artistName = artist.Name ?? fallbackTitle;

            return new CoverSearchResult(
                ThumbnailUrl: thumbnail,
                FullUrl: full,
                ReleaseTitle: artistName,
                Source: "Deezer (Künstler)");
        }

        /// <summary>
        /// Antwort der Deezer-Künstlersuche; gelesen wird nur die Trefferliste.
        /// </summary>
        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "DTO wird von System.Text.Json via Deserialize<T> per Reflection instanziiert.")]
        private sealed class DeezerSearchResponse
        {
            [JsonPropertyName("data")]
            public List<DeezerArtist>? Data { get; set; }
        }

        /// <summary>
        /// Ein Künstlertreffer mit Name und Bild in zwei Größen.
        /// </summary>
        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "DTO wird von System.Text.Json via Deserialize<T> per Reflection instanziiert.")]
        private sealed class DeezerArtist
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            /// <summary>250×250 Pixel für die Vorschau im Auswahldialog.</summary>
            [JsonPropertyName("picture_medium")]
            public string? PictureMedium { get; set; }

            /// <summary>1000×1000 Pixel; wird als Serien-Cover gespeichert.</summary>
            [JsonPropertyName("picture_xl")]
            public string? PictureXl { get; set; }
        }
    }
}

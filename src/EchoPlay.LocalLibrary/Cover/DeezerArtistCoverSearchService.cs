using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
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
    public sealed class DeezerArtistCoverSearchService : JsonCoverSearchServiceBase
    {
        /// <summary>
        /// Initialisiert den Service mit einem vorkonfigurierten <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="httpClient">HTTP-Client, bereitgestellt durch IHttpClientFactory.</param>
        public DeezerArtistCoverSearchService(HttpClient httpClient) : base(httpClient)
        {
        }

        /// <summary>Maximal 6 Künstlertreffer – Serienbilder sind meist im ersten Ergebnis.</summary>
        protected override int MaxResults => 6;

        /// <inheritdoc/>
        public override Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CancellationToken ct = default) =>
            SearchJsonAsync<DeezerSearchResponse, DeezerArtist>(
                title,
                static (encodedTitle, maxResults) => $"https://api.deezer.com/search/artist?q={encodedTitle}&limit={maxResults}",
                static response => response.Data,
                MapArtist,
                ct);

        private static CoverSearchResult? MapArtist(DeezerArtist artist, string fallbackTitle)
        {
            // Deezer liefert einen Platzhalter-URL wenn kein Bild vorhanden ist –
            // diese enthalten "/artist//", ohne ID dazwischen
            if (string.IsNullOrWhiteSpace(artist.PictureMedium)
                || string.IsNullOrWhiteSpace(artist.PictureXl))
            {
                return null;
            }

            string artistName = artist.Name ?? fallbackTitle;

            return new CoverSearchResult(
                ThumbnailUrl: artist.PictureMedium,
                FullUrl: artist.PictureXl,
                ReleaseTitle: artistName,
                Source: "Deezer (Künstler)");
        }

        // ── Interne DTO-Klassen für die JSON-Deserialisierung ─────────────────────

        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "DTO wird von System.Text.Json via Deserialize<T> per Reflection instanziiert.")]
        private sealed class DeezerSearchResponse
        {
            [JsonPropertyName("data")]
            public List<DeezerArtist>? Data { get; set; }
        }

        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "DTO wird von System.Text.Json via Deserialize<T> per Reflection instanziiert.")]
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

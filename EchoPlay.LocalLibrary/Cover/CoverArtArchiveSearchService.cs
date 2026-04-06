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
    /// Implementierung von <see cref="ICoverSearchService"/> über die MusicBrainz-API
    /// und das Cover Art Archive. Beide Dienste sind kostenlos und benötigen keinen API-Key.
    /// </summary>
    /// <remarks>
    /// Ablauf:
    /// 1. MusicBrainz-Release-Suche mit dem Titel liefert MBIDs (MusicBrainz-IDs).
    /// 2. Für jede MBID wird ein Cover Art Archive-Eintrag geprüft.
    /// 3. Gefundene Cover werden als <see cref="CoverSearchResult"/> zurückgegeben.
    ///
    /// MusicBrainz schreibt einen aussagekräftigen User-Agent-Header vor –
    /// Anfragen ohne diesen Header werden gedrosselt oder abgelehnt.
    /// </remarks>
    public sealed class CoverArtArchiveSearchService : ICoverSearchService
    {
        private readonly HttpClient _httpClient;

        // Maximale Anzahl Ergebnisse – 9 Kacheln passen in 3×3-Grid des Auswahldialogs
        private const int MaxResults = 9;

        /// <summary>
        /// Initialisiert den Service mit einem konfigurierten <see cref="HttpClient"/>.
        /// Der Client muss einen gültigen <c>User-Agent</c>-Header tragen.
        /// </summary>
        /// <param name="httpClient">Vorkonfigurierter HTTP-Client mit User-Agent.</param>
        public CoverArtArchiveSearchService(HttpClient httpClient)
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

            MusicBrainzReleaseSearchResponse? searchResponse;

            try
            {
                string encodedTitle = Uri.EscapeDataString(title);
                // MaxResults ist ein int – bei URI-Werten ist Kulturunabhängigkeit garantiert
                string url = $"https://musicbrainz.org/ws/2/release?query={encodedTitle}&limit={MaxResults}&fmt=json";

                searchResponse = await _httpClient.GetFromJsonAsync<MusicBrainzReleaseSearchResponse>(
                    url, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Netzwerkfehler → leere Liste, kein Absturz
                return [];
            }

            if (searchResponse?.Releases is not { Count: > 0 })
            {
                return [];
            }

            List<CoverSearchResult> results = [];

            foreach (MusicBrainzRelease release in searchResponse.Releases)
            {
                if (string.IsNullOrWhiteSpace(release.Id))
                {
                    continue;
                }

                // Cover Art Archive gibt HTTP 404 zurück wenn kein Cover vorhanden –
                // HEAD-Request vermeidet unnötigen Download zum Prüfen der Verfügbarkeit
                bool hasCover = await HasCoverAsync(release.Id, ct).ConfigureAwait(false);

                if (!hasCover)
                {
                    continue;
                }

                string mbid = release.Id;
                string releaseTitle = release.Title ?? title;

                results.Add(new CoverSearchResult(
                    ThumbnailUrl: $"https://coverartarchive.org/release/{mbid}/front-250",
                    FullUrl:      $"https://coverartarchive.org/release/{mbid}/front",
                    ReleaseTitle: releaseTitle,
                    Source:       "Cover Art Archive"));

                if (results.Count >= MaxResults)
                {
                    break;
                }
            }

            return results;
        }

        /// <summary>
        /// Prüft per HEAD-Request, ob das Cover Art Archive für die MBID ein Cover bereithält.
        /// HTTP 200 → vorhanden, alles andere (404, Timeout, …) → nicht vorhanden.
        /// </summary>
        private async Task<bool> HasCoverAsync(string mbid, CancellationToken ct)
        {
            try
            {
                string url = $"https://coverartarchive.org/release/{mbid}/front";

                using HttpRequestMessage request = new(HttpMethod.Head, url);
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ── Interne DTO-Klassen für die JSON-Deserialisierung ─────────────────────

        private sealed class MusicBrainzReleaseSearchResponse
        {
            [JsonPropertyName("releases")]
            public List<MusicBrainzRelease>? Releases { get; set; }
        }

        private sealed class MusicBrainzRelease
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("title")]
            public string? Title { get; set; }
        }
    }
}

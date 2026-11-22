using EchoPlay.Core.Http;
using EchoPlay.Logger.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Sucht Cover über die MusicBrainz-API und das Cover Art Archive. Beide Dienste sind
    /// kostenlos und brauchen keinen API-Schlüssel.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>Die Release-Suche bei MusicBrainz liefert zum Titel die Release-Kennungen (MBID).</item>
    /// <item>Je Kennung fragt eine HEAD-Anfrage das Cover Art Archive, ob ein Front-Cover da ist.</item>
    /// <item>Nur Releases mit Cover werden zum Treffer.</item>
    /// </list>
    /// MusicBrainz schreibt einen aussagekräftigen User-Agent-Header vor;
    /// Anfragen ohne diesen Header werden gedrosselt oder abgelehnt.
    /// </remarks>
    public sealed class CoverArtArchiveSearchService : ICoverSearchService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        // Neun Treffer füllen das 3×3-Raster des Auswahldialogs.
        private const int MaxResults = 9;

        /// <summary>
        /// Initialisiert den Service mit einem konfigurierten <see cref="HttpClient"/>.
        /// Der Client muss einen gültigen <c>User-Agent</c>-Header tragen.
        /// </summary>
        /// <param name="httpClient">Vorkonfigurierter HTTP-Client mit User-Agent.</param>
        /// <param name="loggerFactory">Für das Protokoll übersprungener Anfragen.</param>
        public CoverArtArchiveSearchService(HttpClient httpClient, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _httpClient = httpClient;
            _logger = loggerFactory.CreateLogger(nameof(CoverArtArchiveSearchService));
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CancellationToken ct = default) =>
            SearchAsync(title, CoverSearchPage.First, ct);

        /// <inheritdoc/>
        public async Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CoverSearchPage page,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return [];
            }

            // MusicBrainz kennt offset, Nachladen ist hier eine echte Folgeabfrage.
            CoverSearchWindow window = page.ToWindow(MaxResults, supportsOffset: true);

            MusicBrainzReleaseSearchResponse? searchResponse = await SearchReleasesAsync(title, window, ct).ConfigureAwait(false);

            if (searchResponse?.Releases is not { Count: > 0 })
            {
                return [];
            }

            List<CoverSearchResult> results = [];

            foreach (MusicBrainzRelease release in searchResponse.Releases)
            {
                if (await TryCoverResultAsync(release, title, ct).ConfigureAwait(false) is { } result)
                {
                    results.Add(result);
                    if (results.Count >= MaxResults)
                    {
                        break;
                    }
                }
            }

            return results;
        }

        /// <summary>Fragt MusicBrainz nach Veröffentlichungen mit diesem Titel.</summary>
        /// <returns>Die Antwort, oder leer, wenn der Anbieter nicht oder unlesbar geantwortet hat.</returns>
        private async Task<MusicBrainzReleaseSearchResponse?> SearchReleasesAsync(string title, CoverSearchWindow window, CancellationToken ct)
        {
            try
            {
                string encodedTitle = Uri.EscapeDataString(title);
                string url = $"https://musicbrainz.org/ws/2/release?query={encodedTitle}&limit={window.Limit}&offset={window.Offset}&fmt=json";

                return await _httpClient.GetFromJsonAsync<MusicBrainzReleaseSearchResponse>(
                    url, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (TransientRequestError.IsTransient(ex, ct))
            {
                // Ein Anbieter, der nicht antwortet oder Unlesbares schickt, fällt für diese Suche
                // aus; ein Abbruch des Aufrufers geht dagegen durch.
                _logger.Warning("Cover-Suche bei MusicBrainz übersprungen: {Reason}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Das Ergebnis für eine Veröffentlichung, wenn das Archiv ein Cover dazu hat.
        /// </summary>
        /// <param name="release">Die Veröffentlichung aus der MusicBrainz-Antwort.</param>
        /// <param name="title">Der gesuchte Titel; er gilt, wenn die Veröffentlichung keinen trägt.</param>
        /// <param name="ct">Abbruch der Suche.</param>
        private async Task<CoverSearchResult?> TryCoverResultAsync(MusicBrainzRelease release, string title, CancellationToken ct)
        {
            // Ohne Cover antwortet das Archiv mit 404. HEAD statt GET, damit die Prüfung
            // nicht das ganze Bild lädt.
            if (string.IsNullOrWhiteSpace(release.Id) || !await HasCoverAsync(release.Id, ct).ConfigureAwait(false))
            {
                return null;
            }

            // Die Kennung stammt aus der MusicBrainz-Antwort; erst die geprüfte Adresse zählt.
            string mbid = Uri.EscapeDataString(release.Id);
            Uri? thumbnail = WebAddress.TryParse($"https://coverartarchive.org/release/{mbid}/front-250");
            Uri? full = WebAddress.TryParse($"https://coverartarchive.org/release/{mbid}/front");
            if (thumbnail is null || full is null)
            {
                return null;
            }

            return new CoverSearchResult(
                ThumbnailUrl: thumbnail,
                FullUrl: full,
                ReleaseTitle: release.Title ?? title,
                Source: "Cover Art Archive");
        }

        /// <summary>
        /// Prüft per HEAD-Request, ob das Cover Art Archive für die MBID ein Cover bereithält.
        /// Eine erfolgreiche Antwort heißt vorhanden; 404, Zeitüberschreitung und jeder andere
        /// Fehler heißen nicht vorhanden.
        /// </summary>
        private async Task<bool> HasCoverAsync(string mbid, CancellationToken ct)
        {
            try
            {
                string url = $"https://coverartarchive.org/release/{Uri.EscapeDataString(mbid)}/front";

                using HttpRequestMessage request = new(HttpMethod.Head, url);
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (TransientRequestError.IsTransient(ex, ct))
            {
                // Nicht erreichbar gilt als „kein Cover"; der Treffer fällt weg, die Suche läuft weiter.
                _logger.Debug(() => $"Cover-Prüfung für {mbid} übersprungen: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Antwort der MusicBrainz-Release-Suche; gelesen wird nur die Trefferliste.
        /// </summary>
        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "DTO wird von System.Text.Json via Deserialize<T> per Reflection instanziiert.")]
        private sealed class MusicBrainzReleaseSearchResponse
        {
            [JsonPropertyName("releases")]
            public List<MusicBrainzRelease>? Releases { get; set; }
        }

        /// <summary>
        /// Ein Release-Treffer. Aus seiner Id entsteht die Adresse des Covers im Cover Art Archive.
        /// </summary>
        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "DTO wird von System.Text.Json via Deserialize<T> per Reflection instanziiert.")]
        private sealed class MusicBrainzRelease
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("title")]
            public string? Title { get; set; }
        }
    }
}

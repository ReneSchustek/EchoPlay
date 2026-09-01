using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;
using System.Text.Json;

namespace EchoPlay.AppleMusic.Clients
{
    /// <summary>
    /// Implementiert den Zugriff auf die iTunes Search API.
    /// Die API ist kostenfrei, öffentlich zugänglich und benötigt keine Authentifizierung.
    /// Alle Suchanfragen verwenden den deutschen Storefront (country=de).
    /// </summary>
    public sealed class AppleMusicSearchClient : IAppleMusicSearchClient
    {
        private const string Country = "de";

        /// <summary>
        /// Obergrenze einer Lookup-Antwort. iTunes liefert höchstens so viele Alben je Anfrage,
        /// unabhängig davon, welches <c>limit</c> in der Adresse steht – ein höherer Wert
        /// täuscht nur Vollständigkeit vor. Gemessen an „Die drei ???": 200 Alben, und die
        /// jüngsten Folgen fehlten.
        /// </summary>
        private const int LookupAlbumCap = 200;

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly HttpClient _httpClient;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger;

        /// <summary>
        /// Initialisiert den Client mit einem vorkonfigurierten HttpClient.
        /// Die BaseAddress muss auf https://itunes.apple.com/ gesetzt sein.
        /// </summary>
        /// <param name="httpClient">Der HttpClient mit konfigurierter BaseAddress.</param>
        /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
        public AppleMusicSearchClient(
            HttpClient httpClient,
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _httpClient = httpClient;
            _logger = loggerFactory.CreateLogger("AppleMusicSearchClient");
        }

        /// <summary>
        /// Sucht nach Künstlern anhand eines freien Suchbegriffs.
        /// </summary>
        /// <param name="query">Der Suchbegriff.</param>
        /// <param name="limit">Maximale Anzahl der Ergebnisse.</param>
        /// <param name="ct">Abbruchtoken für den HTTP-Aufruf.</param>
        /// <returns>Die Suchantwort mit Künstler-Ergebnissen.</returns>
        public async Task<ITunesResponseDto<ITunesArtistDto>> SearchArtistsAsync(string query, int limit = 25, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Suchbegriff darf nicht leer sein.", nameof(query));
            }

            string url = $"search?term={Uri.EscapeDataString(query)}&entity=musicArtist&country={Country}&limit={limit}";

            return await GetAsync<ITunesResponseDto<ITunesArtistDto>>(url, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<ITunesResponseDto<ITunesCollectionDto>> SearchAlbumsAsync(string query, int limit = 25, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Suchbegriff darf nicht leer sein.", nameof(query));
            }

            string url = $"search?term={Uri.EscapeDataString(query)}&entity=album&country={Country}&limit={limit}";

            return await GetAsync<ITunesResponseDto<ITunesCollectionDto>>(url, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Lädt alle Alben eines Künstlers über die Lookup-API.
        /// Bei langen Serien werden zwei Reihenfolgen abgefragt und zusammengelegt,
        /// weil eine einzelne Antwort den Bestand nicht fasst.
        /// </summary>
        /// <param name="artistId">Die iTunes-Artist-ID.</param>
        /// <param name="ct">Abbruchtoken für den HTTP-Aufruf.</param>
        /// <returns>Die Lookup-Antwort mit Künstler- und Album-Einträgen.</returns>
        public async Task<ITunesResponseDto<ITunesCollectionDto>> LookupAlbumsAsync(long artistId, CancellationToken ct = default)
        {
            string basisUrl = $"lookup?id={artistId}&entity=album&country={Country}&limit={LookupAlbumCap}";

            ITunesResponseDto<ITunesCollectionDto> standard =
                await GetAsync<ITunesResponseDto<ITunesCollectionDto>>(basisUrl, ct).ConfigureAwait(false);

            int albenImStandard = CountAlbums(standard);

            // Unter dem Deckel ist der Bestand vollständig – eine zweite Anfrage brächte nichts.
            if (albenImStandard < LookupAlbumCap)
            {
                return standard;
            }

            // Ausgeschöpfter Deckel heißt: Es gibt mehr, als eine Antwort trägt. Die
            // Standardreihenfolge beginnt bei den ältesten Alben, deshalb fehlen ausgerechnet
            // die neuen Folgen. `sort=recent` dreht die Reihenfolge um; beide Hälften
            // zusammengelegt decken den Bestand ab.
            ITunesResponseDto<ITunesCollectionDto> neueste =
                await GetAsync<ITunesResponseDto<ITunesCollectionDto>>($"{basisUrl}&sort=recent", ct).ConfigureAwait(false);

            ITunesResponseDto<ITunesCollectionDto> zusammengelegt = MergeAlbums(standard, neueste);

            _logger.Debug(() =>
                $"iTunes-Lookup für Künstler '{artistId}': Deckel von {LookupAlbumCap} ausgeschöpft, " +
                $"mit sort=recent auf {CountAlbums(zusammengelegt)} Alben ergänzt.");

            return zusammengelegt;
        }

        /// <summary>
        /// Zählt die Album-Einträge einer Lookup-Antwort. Der Künstler-Eintrag steht
        /// als erstes Element in derselben Liste und zählt nicht mit.
        /// </summary>
        /// <param name="response">Die Lookup-Antwort.</param>
        private static int CountAlbums(ITunesResponseDto<ITunesCollectionDto> response)
        {
            int anzahl = 0;
            foreach (ITunesCollectionDto eintrag in response.Results)
            {
                if (IsAlbum(eintrag))
                {
                    anzahl++;
                }
            }

            return anzahl;
        }

        /// <summary>Ob ein Lookup-Eintrag ein Album ist und kein Künstler.</summary>
        /// <param name="entry">Der Eintrag aus der Lookup-Antwort.</param>
        private static bool IsAlbum(ITunesCollectionDto entry) =>
            string.Equals(entry.WrapperType, "collection", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Legt zwei Lookup-Antworten desselben Künstlers zusammen. Die erste bleibt
        /// vollständig erhalten (samt Künstler-Eintrag), aus der zweiten kommen nur die
        /// Alben hinzu, deren Collection-ID noch fehlt.
        /// </summary>
        /// <param name="first">Die Antwort in Standardreihenfolge.</param>
        /// <param name="second">Die Antwort mit den neuesten Alben.</param>
        private static ITunesResponseDto<ITunesCollectionDto> MergeAlbums(
            ITunesResponseDto<ITunesCollectionDto> first,
            ITunesResponseDto<ITunesCollectionDto> second)
        {
            HashSet<long> bekannteAlben = [];
            foreach (ITunesCollectionDto eintrag in first.Results)
            {
                if (IsAlbum(eintrag))
                {
                    _ = bekannteAlben.Add(eintrag.CollectionId);
                }
            }

            List<ITunesCollectionDto> zusammen = [.. first.Results];
            foreach (ITunesCollectionDto eintrag in second.Results)
            {
                if (IsAlbum(eintrag) && bekannteAlben.Add(eintrag.CollectionId))
                {
                    zusammen.Add(eintrag);
                }
            }

            return new ITunesResponseDto<ITunesCollectionDto>
            {
                ResultCount = zusammen.Count,
                Results = zusammen
            };
        }

        /// <summary>
        /// Lädt alle Tracks eines Albums über die Lookup-API.
        /// </summary>
        /// <param name="collectionId">Die iTunes-Collection-ID des Albums.</param>
        /// <param name="ct">Abbruchtoken für den HTTP-Aufruf.</param>
        /// <returns>Die Lookup-Antwort mit Album- und Track-Einträgen.</returns>
        public async Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksAsync(long collectionId, CancellationToken ct = default)
        {
            string url = $"lookup?id={collectionId}&entity=song&country={Country}";

            return await GetAsync<ITunesResponseDto<ITunesTrackDto>>(url, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Lädt die Tracks mehrerer Alben in einem Lookup-Request (kommaseparierte IDs).
        /// </summary>
        /// <param name="collectionIds">Die iTunes-Collection-IDs der Alben (ein Batch).</param>
        /// <param name="ct">Abbruchtoken für den HTTP-Aufruf.</param>
        /// <returns>Die Lookup-Antwort mit Alben und deren Tracks.</returns>
        public async Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksBatchAsync(IReadOnlyList<long> collectionIds, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(collectionIds);

            if (collectionIds.Count == 0)
            {
                return new ITunesResponseDto<ITunesTrackDto>();
            }

            string ids = string.Join(',', collectionIds);
            string url = $"lookup?id={ids}&entity=song&country={Country}";

            return await GetAsync<ITunesResponseDto<ITunesTrackDto>>(url, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Führt einen GET-Request aus und deserialisiert die JSON-Antwort.
        /// </summary>
        /// <typeparam name="T">Ziel-Typ der Deserialisierung.</typeparam>
        /// <param name="relativeUrl">Relativer API-Pfad.</param>
        /// <param name="ct">Abbruchtoken für den HTTP-Aufruf.</param>
        /// <returns>Das deserialisierte Antwortobjekt.</returns>
        private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken ct = default) where T : new()
        {
            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope($"API:iTunes:GET");

            _logger.Debug(() => $"iTunes-API-Anfrage: GET {relativeUrl}");

            try
            {
                using HttpResponseMessage response = await _httpClient
                    .GetAsync(new Uri(relativeUrl, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);

                _ = response.EnsureSuccessStatusCode();

                using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

                T? result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct).ConfigureAwait(false);

                // Eine null-Deserialisierung deutet auf ein strukturelles API-Problem hin,
                // nicht auf ein leeres Ergebnis – der Aufrufer muss das unterscheiden können.
                if (result is null)
                {
                    _logger.Warning("iTunes-API lieferte null-Response für: {RelativeUrl}", relativeUrl);
                    throw new InvalidOperationException($"iTunes-API-Response konnte nicht deserialisiert werden: {relativeUrl}");
                }

                _logger.Debug(() => $"iTunes-API-Antwort erhalten: {relativeUrl}");

                return result;
            }
            catch (HttpRequestException ex)
            {
                _logger.Error("iTunes-API-Anfrage fehlgeschlagen: {RelativeUrl}", ex, relativeUrl);
                throw;
            }
            catch (JsonException ex)
            {
                _logger.Error("iTunes-API-Antwort konnte nicht geparst werden: {RelativeUrl}", ex, relativeUrl);
                throw;
            }
        }
    }
}

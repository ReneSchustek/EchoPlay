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
        /// </summary>
        /// <param name="artistId">Die iTunes-Artist-ID.</param>
        /// <param name="ct">Abbruchtoken für den HTTP-Aufruf.</param>
        /// <returns>Die Lookup-Antwort mit Künstler- und Album-Einträgen.</returns>
        public async Task<ITunesResponseDto<ITunesCollectionDto>> LookupAlbumsAsync(long artistId, CancellationToken ct = default)
        {
            // Limit bewusst hoch gewählt – Serien wie Die drei ??? haben 230+ Folgen.
            // Die iTunes Lookup-API akzeptiert Werte über 200 problemlos.
            string url = $"lookup?id={artistId}&entity=album&country={Country}&limit=500";

            return await GetAsync<ITunesResponseDto<ITunesCollectionDto>>(url, ct).ConfigureAwait(false);
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
        /// Führt einen GET-Request aus und deserialisiert die JSON-Antwort.
        /// </summary>
        /// <typeparam name="T">Ziel-Typ der Deserialisierung.</typeparam>
        /// <param name="relativeUrl">Relativer API-Pfad.</param>
        /// <param name="ct">Abbruchtoken für den HTTP-Aufruf.</param>
        /// <returns>Das deserialisierte Antwortobjekt.</returns>
        private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken ct = default) where T : new()
        {
            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope($"API:iTunes:GET");

            _logger.Debug($"iTunes-API-Anfrage: GET {relativeUrl}");

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
                    _logger.Warning($"iTunes-API lieferte null-Response für: {relativeUrl}");
                    throw new InvalidOperationException($"iTunes-API-Response konnte nicht deserialisiert werden: {relativeUrl}");
                }

                _logger.Debug($"iTunes-API-Antwort erhalten: {relativeUrl}");

                return result;
            }
            catch (HttpRequestException ex)
            {
                _logger.Error($"iTunes-API-Anfrage fehlgeschlagen: {relativeUrl}", ex);
                throw;
            }
            catch (JsonException ex)
            {
                _logger.Error($"iTunes-API-Antwort konnte nicht geparst werden: {relativeUrl}", ex);
                throw;
            }
        }
    }
}

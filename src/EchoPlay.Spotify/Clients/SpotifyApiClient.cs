using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Http;
using System.Text.Json;

namespace EchoPlay.Spotify.Clients
{
    /// <summary>
    /// Technische Implementierung des Spotify-Web-API-Clients.
    ///
    /// Diese Klasse bildet ausschließlich die HTTP-Endpunkte von Spotify ab
    /// und übersetzt die JSON-Antworten in interne DTOs.
    ///
    /// Sie enthält bewusst keinerlei fachliche Logik, Bewertung oder Filterung.
    /// Dadurch bleibt die Trennung zwischen Technik (API-Zugriff)
    /// und Fachlichkeit (Import, Scoring, Mapping) klar gewahrt.
    /// </summary>
    /// <remarks>
    /// Erstellt eine neue Instanz des SpotifyApiClients.
    ///
    /// Der HttpClient wird von außen injiziert, um Konfiguration,
    /// Authentifizierung und Lebenszyklus zentral steuern zu können.
    /// </remarks>
    /// <param name="httpClient">
    /// Vorkonfigurierter HttpClient mit BaseAddress und Authorization-Header.
    /// </param>
    /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
    public sealed class SpotifyApiClient(
        HttpClient httpClient,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : ISpotifyApiClient
    {
        private readonly HttpClient _httpClient = httpClient;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger = loggerFactory.CreateLogger("SpotifyApiClient");

        /// <summary>
        /// Sucht Künstler über die Spotify-Such-API.
        ///
        /// Die Methode bildet den technischen Endpunkt direkt ab.
        /// Eine fachliche Bewertung oder Filterung der Ergebnisse
        /// erfolgt bewusst nicht an dieser Stelle.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="limit">Maximale Anzahl der Ergebnisse.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Eine Liste roher Spotify-Künstlerdaten.</returns>
        public async Task<IReadOnlyList<SpotifyArtistDto>> SearchArtistsAsync(string query, int limit, CancellationToken cancellationToken = default)
        {
            // Spotify stellt eine rein query-basierte Such-API bereit.
            // Die Verantwortung für Relevanz oder Scoring liegt nicht hier.
            string requestUri =
                $"search?q={Uri.EscapeDataString(query)}&type=artist&limit={limit}";

            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("API:Spotify:SearchArtists");

            _logger.Debug(() => $"Spotify-Künstlersuche gestartet: {requestUri}");

            try
            {
                using JsonDocument document = await SendAndParseAsync(requestUri, UriKind.Relative, cancellationToken).ConfigureAwait(false);

                JsonElement items =
                    document.RootElement.GetProperty("artists").GetProperty("items");

                List<SpotifyArtistDto> artists = [];

                foreach (JsonElement item in items.EnumerateArray())
                {
                    // Künstler ohne ID oder Name sind für den Import nicht verwertbar
                    // und werden übersprungen statt eine Exception zu werfen.
                    string? artistId = item.TryGetProperty("id", out JsonElement idEl)
                        ? idEl.GetString()
                        : null;

                    string? name = item.TryGetProperty("name", out JsonElement nameEl)
                        ? nameEl.GetString()
                        : null;

                    if (artistId is null || name is null)
                    {
                        _logger.Warning("Spotify-Künstler ohne ID oder Name übersprungen.");
                        continue;
                    }

                    // Es werden nur die Felder gelesen, die für die spätere
                    // Hörspiel-Erkennung relevant sind.
                    artists.Add(new SpotifyArtistDto
                    {
                        SpotifyArtistId = artistId,
                        Name = name,
                        Genres = [.. item.GetProperty("genres").EnumerateArray()
                            .Select(g => g.GetString())
                            .Where(g => g is not null)
                            .Select(g => g!)],

                        // Für EchoPlay genügt ein einzelnes Bild.
                        ImageUrl = TryGetFirstImageUrl(item)
                    });
                }

                _logger.Info("Spotify-Künstlersuche erfolgreich: {ArtistCount} Ergebnisse.", artists.Count);

                return artists;
            }
            catch (HttpRequestException ex)
            {
                _logger.Error("Spotify-Künstlersuche fehlgeschlagen: {RequestUri}", ex, requestUri);
                throw;
            }
            catch (JsonException ex)
            {
                _logger.Error("Spotify-Antwort der Künstlersuche konnte nicht geparst werden: {RequestUri}", ex, requestUri);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<SpotifyAlbumDto>> SearchAlbumsAsync(string query, int limit, CancellationToken cancellationToken = default)
        {
            string requestUri =
                $"search?q={Uri.EscapeDataString(query)}&type=album&limit={limit}";

            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("API:Spotify:SearchAlbums");
            _logger.Debug(() => $"Spotify-Albumsuche gestartet: {requestUri}");

            try
            {
                using JsonDocument document = await SendAndParseAsync(requestUri, UriKind.Relative, cancellationToken).ConfigureAwait(false);

                JsonElement items =
                    document.RootElement.GetProperty("albums").GetProperty("items");

                List<SpotifyAlbumDto> albums = [];

                foreach (JsonElement item in items.EnumerateArray())
                {
                    string? albumId = item.TryGetProperty("id", out JsonElement idEl)
                        ? idEl.GetString()
                        : null;

                    string? name = item.TryGetProperty("name", out JsonElement nameEl)
                        ? nameEl.GetString()
                        : null;

                    if (albumId is null || name is null) continue;

                    // Erster Künstler als Serienname
                    string? artistName = null;
                    if (item.TryGetProperty("artists", out JsonElement artists) && artists.GetArrayLength() > 0)
                    {
                        artistName = artists[0].TryGetProperty("name", out JsonElement an) ? an.GetString() : null;
                    }

                    string? imageUrl = TryGetFirstImageUrl(item);

                    int totalTracks = item.TryGetProperty("total_tracks", out JsonElement tt) ? tt.GetInt32() : 0;

                    albums.Add(new SpotifyAlbumDto
                    {
                        SpotifyAlbumId = albumId,
                        Title = name,
                        TotalTracks = totalTracks,
                        ImageUrl = imageUrl,
                        ArtistName = artistName
                    });
                }

                _logger.Info("Spotify-Albumsuche erfolgreich: {AlbumCount} Ergebnisse.", albums.Count);
                return albums;
            }
            catch (HttpRequestException ex)
            {
                _logger.Error("Spotify-Albumsuche fehlgeschlagen: {RequestUri}", ex, requestUri);
                throw;
            }
            catch (JsonException ex)
            {
                _logger.Error("Spotify-Antwort der Albumsuche konnte nicht geparst werden: {RequestUri}", ex, requestUri);
                throw;
            }
        }

        /// <summary>
        /// Lädt die Alben eines Künstlers.
        ///
        /// Singles und Compilations werden bewusst ausgeschlossen,
        /// da sie für Hörspiel-Serien in der Regel keine Rolle spielen.
        /// </summary>
        /// <param name="artistId">Die Spotify-ID des Künstlers.</param>
        /// <param name="limit">Maximale Anzahl der Alben.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Eine Liste roher Spotify-Albumdaten.</returns>
        public async Task<IReadOnlyList<SpotifyAlbumDto>> GetArtistAlbumsAsync(string artistId, int limit, CancellationToken cancellationToken = default)
        {
            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope($"API:Spotify:GetArtistAlbums");

            _logger.Debug(() => $"Spotify-Alben laden für Künstler '{artistId}' (max. {limit}).");

            // Spotify liefert maximal 50 Alben pro Seite – bei Serien mit >50 Folgen
            // (z.B. Die drei ??? mit 230+) muss über mehrere Seiten iteriert werden.
            int pageSize = Math.Min(limit, 50);
            string? requestUri = $"artists/{artistId}/albums?limit={pageSize}&include_groups=album";
            List<SpotifyAlbumDto> albums = [];

            try
            {
                while (requestUri is not null && albums.Count < limit)
                {
                    // Die Pagination-URL aus "next" ist absolut, der Startwert ist relativ –
                    // UriKind.RelativeOrAbsolute deckt beide Fälle im Loop ab.
                    using JsonDocument document = await SendAndParseAsync(requestUri, UriKind.RelativeOrAbsolute, cancellationToken).ConfigureAwait(false);

                    JsonElement items = document.RootElement.GetProperty("items");

                    foreach (JsonElement item in items.EnumerateArray())
                    {
                        if (albums.Count >= limit) break;

                        string? albumId = item.TryGetProperty("id", out JsonElement idEl)
                            ? idEl.GetString()
                            : null;

                        string? title = item.TryGetProperty("name", out JsonElement nameEl)
                            ? nameEl.GetString()
                            : null;

                        if (albumId is null || title is null)
                        {
                            _logger.Warning("Spotify-Album ohne ID oder Name übersprungen.");
                            continue;
                        }

                        // Das Veröffentlichungsdatum kann unterschiedlich genau sein
                        // (Jahr, Monat oder Tag) und wird daher defensiv geparst.
                        // Cover-URL: erstes Bild im "images"-Array (höchste Auflösung)
                        string? imageUrl = TryGetFirstImageUrl(item);

                        albums.Add(new SpotifyAlbumDto
                        {
                            SpotifyAlbumId = albumId,
                            Title = title,
                            ReleaseDate =
                                DateTime.TryParse(item.GetProperty("release_date").GetString(), out DateTime date)
                                    ? date
                                    : null,
                            TotalTracks = item.GetProperty("total_tracks").GetInt32(),
                            ImageUrl = imageUrl
                        });
                    }

                    // "next" enthält die vollständige URL zur nächsten Seite oder null bei der letzten
                    requestUri = document.RootElement.TryGetProperty("next", out JsonElement nextEl)
                                 && nextEl.ValueKind == JsonValueKind.String
                        ? nextEl.GetString()
                        : null;
                }

                _logger.Info("Spotify-Alben für Künstler '{ArtistId}' geladen: {AlbumCount} Alben.", artistId, albums.Count);

                return albums;
            }
            catch (HttpRequestException ex)
            {
                _logger.Error("Spotify-Alben für Künstler '{ArtistId}' konnten nicht geladen werden.", ex, artistId);
                throw;
            }
            catch (JsonException ex)
            {
                _logger.Error("Spotify-Albumantwort für Künstler '{ArtistId}' konnte nicht geparst werden.", ex, artistId);
                throw;
            }
        }

        /// <summary>
        /// Lädt alle Tracks eines Albums.
        ///
        /// Die Reihenfolge entspricht der von Spotify gelieferten Track-Reihenfolge.
        /// </summary>
        /// <param name="albumId">Die Spotify-ID des Albums.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Eine Liste roher Spotify-Trackdaten.</returns>
        public async Task<IReadOnlyList<SpotifyTrackDto>> GetAlbumTracksAsync(string albumId, CancellationToken cancellationToken = default)
        {
            string requestUri = $"albums/{albumId}/tracks";

            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope($"API:Spotify:GetAlbumTracks");

            _logger.Debug(() => $"Spotify-Tracks laden für Album '{albumId}'.");

            try
            {
                using JsonDocument document = await SendAndParseAsync(requestUri, UriKind.Relative, cancellationToken).ConfigureAwait(false);

                JsonElement items = document.RootElement.GetProperty("items");

                List<SpotifyTrackDto> tracks = [];

                foreach (JsonElement item in items.EnumerateArray())
                {
                    string? trackId = item.TryGetProperty("id", out JsonElement idEl)
                        ? idEl.GetString()
                        : null;

                    string? title = item.TryGetProperty("name", out JsonElement nameEl)
                        ? nameEl.GetString()
                        : null;

                    if (trackId is null || title is null)
                    {
                        _logger.Warning("Spotify-Track ohne ID oder Name übersprungen.");
                        continue;
                    }

                    // Die Umrechnung in TimeSpan erfolgt hier,
                    // damit nachfolgende Schichten Spotify-unabhängig arbeiten können.
                    tracks.Add(new SpotifyTrackDto
                    {
                        SpotifyTrackId = trackId,
                        Title = title,
                        Duration = TimeSpan.FromMilliseconds(item.GetProperty("duration_ms").GetInt32()),
                        TrackNumber = item.GetProperty("track_number").GetInt32()
                    });
                }

                _logger.Info("Spotify-Tracks für Album '{AlbumId}' geladen: {TrackCount} Tracks.", albumId, tracks.Count);

                return tracks;
            }
            catch (HttpRequestException ex)
            {
                _logger.Error("Spotify-Tracks für Album '{AlbumId}' konnten nicht geladen werden.", ex, albumId);
                throw;
            }
            catch (JsonException ex)
            {
                _logger.Error("Spotify-Trackantwort für Album '{AlbumId}' konnte nicht geparst werden.", ex, albumId);
                throw;
            }
        }

        /// <summary>
        /// Führt einen GET-Request mit Retry aus, stellt den Erfolg sicher und parst die
        /// Antwort in ein <see cref="JsonDocument"/>. Der Aufrufer ist für die Entsorgung
        /// des zurückgegebenen Dokuments verantwortlich (<c>using</c>).
        /// </summary>
        /// <param name="requestUri">Die relative oder absolute Anfrage-URL.</param>
        /// <param name="uriKind">Die Art der URL (relativ, absolut oder beides).</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Das geparste JSON-Dokument der Antwort.</returns>
        private async Task<JsonDocument> SendAndParseAsync(string requestUri, UriKind uriKind, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await SpotifyHttpRetry.SendWithRetryAsync(
                () => _httpClient.GetAsync(new Uri(requestUri, uriKind), cancellationToken), cancellationToken).ConfigureAwait(false);

            // Transport- und Authentifizierungsfehler können hier nicht fachlich sinnvoll
            // behandelt werden und werden daher direkt weitergegeben.
            _ = response.EnsureSuccessStatusCode();

            // JsonDocument.ParseAsync puffert den gesamten Stream, daher darf der Stream
            // nach dem Parsen entsorgt werden – das Dokument bleibt gültig.
            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Liefert die URL des ersten Bildes aus dem <c>images</c>-Array eines Spotify-Objekts.
        /// Für EchoPlay genügt ein einzelnes Cover (höchste Auflösung zuerst).
        /// </summary>
        /// <param name="item">Das Spotify-JSON-Objekt (Künstler oder Album).</param>
        /// <returns>Die Bild-URL oder <c>null</c>, wenn kein Bild vorhanden ist.</returns>
        private static string? TryGetFirstImageUrl(JsonElement item) =>
            item.TryGetProperty("images", out JsonElement images) && images.GetArrayLength() > 0
                ? images[0].GetProperty("url").GetString()
                : null;
    }
}

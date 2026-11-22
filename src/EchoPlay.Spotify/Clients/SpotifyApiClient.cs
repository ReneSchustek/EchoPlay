using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;
using System.Text.Json;

namespace EchoPlay.Spotify.Clients
{
    /// <summary>
    /// Technische Implementierung des Spotify-Web-API-Clients.
    /// <para>
    /// Bildet ausschließlich die HTTP-Endpunkte von Spotify ab
    /// und übersetzt die JSON-Antworten in interne DTOs.
    /// Er enthält keinerlei fachliche Logik, Bewertung oder Filterung.
    /// Dadurch bleibt die Trennung zwischen Technik (API-Zugriff)
    /// und Fachlichkeit (Import, Scoring, Mapping) klar gewahrt.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Der HttpClient wird von außen injiziert, um Konfiguration, Authentifizierung,
    /// Wiederholungsregel und Lebenszyklus zentral steuern zu können.
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
        /// Die Methode bildet den technischen Endpunkt direkt ab.
        /// Eine fachliche Bewertung oder Filterung der Ergebnisse
        /// erfolgt nicht an dieser Stelle.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="limit">Maximale Anzahl der Ergebnisse.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Eine Liste roher Spotify-Künstlerdaten.</returns>
        public async Task<IReadOnlyList<SpotifyArtistDto>> SearchArtistsAsync(string query, int limit, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(query);

            // Spotify stellt eine rein query-basierte Such-API bereit.
            // Die Verantwortung für Relevanz oder Scoring liegt nicht hier.
            string requestUri =
                $"search?q={Uri.EscapeDataString(query)}&type=artist&limit={limit}";

            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("API:Spotify:SearchArtists");

            _logger.Debug(() => $"Spotify-Künstlersuche gestartet: {requestUri}");

            try
            {
                using JsonDocument document = await SendAndParseAsync(requestUri, UriKind.Relative, cancellationToken).ConfigureAwait(false);
                List<SpotifyArtistDto> artists = ReadArtists(document);

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

        /// <summary>Liest die verwertbaren Künstler aus der Suchantwort.</summary>
        private List<SpotifyArtistDto> ReadArtists(JsonDocument document)
        {
            JsonElement items =
                document.RootElement.GetProperty("artists").GetProperty("items");

            List<SpotifyArtistDto> artists = [];
            foreach (JsonElement item in items.EnumerateArray())
            {
                // Künstler ohne ID oder Name sind für den Import nicht verwertbar
                // und werden übersprungen statt eine Exception zu werfen.
                if (ReadArtist(item) is { } artist)
                {
                    artists.Add(artist);
                }
                else
                {
                    _logger.Warning("Spotify-Künstler ohne ID oder Name übersprungen.");
                }
            }

            return artists;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<SpotifyAlbumDto>> SearchAlbumsAsync(string query, int limit, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(query);

            string requestUri =
                $"search?q={Uri.EscapeDataString(query)}&type=album&limit={limit}";

            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("API:Spotify:SearchAlbums");
            _logger.Debug(() => $"Spotify-Albumsuche gestartet: {requestUri}");

            try
            {
                using JsonDocument document = await SendAndParseAsync(requestUri, UriKind.Relative, cancellationToken).ConfigureAwait(false);

                JsonElement items =
                    document.RootElement.GetProperty("albums").GetProperty("items");

                List<SpotifyAlbumDto> albums = [.. items.EnumerateArray()
                    .Select(ReadSearchAlbum)
                    .Where(album => album is not null)
                    .Select(album => album!)];

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
        /// Singles und Compilations werden ausgeschlossen,
        /// da sie für Hörspiel-Serien in der Regel keine Rolle spielen.
        /// </summary>
        /// <param name="artistId">Die Spotify-ID des Künstlers.</param>
        /// <param name="limit">Maximale Anzahl der Alben.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Eine Liste roher Spotify-Albumdaten.</returns>
        public async Task<IReadOnlyList<SpotifyAlbumDto>> GetArtistAlbumsAsync(string artistId, int limit, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(artistId);

            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("API:Spotify:GetArtistAlbums");

            _logger.Debug(() => $"Spotify-Alben laden für Künstler '{artistId}' (max. {limit}).");

            // Spotify liefert maximal 50 Alben pro Seite; bei Serien mit >50 Folgen
            // (z.B. Die drei ??? mit 230+) muss über mehrere Seiten iteriert werden.
            int pageSize = Math.Min(limit, 50);
            string? requestUri = $"artists/{artistId}/albums?limit={pageSize}&include_groups=album";
            List<SpotifyAlbumDto> albums = [];

            try
            {
                while (requestUri is not null && albums.Count < limit)
                {
                    // Die Pagination-URL aus "next" ist absolut, der Startwert ist relativ;
                    // UriKind.RelativeOrAbsolute deckt beide Fälle im Loop ab.
                    using JsonDocument document = await SendAndParseAsync(requestUri, UriKind.RelativeOrAbsolute, cancellationToken).ConfigureAwait(false);
                    AddAlbumsFromPage(document, albums, limit);
                    requestUri = NextPage(document);
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

        /// <summary>Übernimmt die verwertbaren Alben einer Seite, bis die Obergrenze erreicht ist.</summary>
        private void AddAlbumsFromPage(JsonDocument document, List<SpotifyAlbumDto> albums, int limit)
        {
            JsonElement items = document.RootElement.GetProperty("items");

            foreach (JsonElement item in items.EnumerateArray().TakeWhile(_ => albums.Count < limit))
            {
                if (ReadArtistAlbum(item) is { } album)
                {
                    albums.Add(album);
                }
                else
                {
                    _logger.Warning("Spotify-Album ohne ID oder Name übersprungen.");
                }
            }
        }

        /// <summary>Liest Kennung und Namen eines Eintrags; fehlt eines, ist der Eintrag nicht verwertbar.</summary>
        private static bool TryReadIdAndName(
            JsonElement item,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? id,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? name)
        {
            id = item.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;
            name = item.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() : null;
            return id is not null && name is not null;
        }

        /// <summary>
        /// Ein Künstler aus der Suche. Es werden nur die Felder gelesen, die für die spätere
        /// Hörspiel-Erkennung relevant sind.
        /// </summary>
        private static SpotifyArtistDto? ReadArtist(JsonElement item)
        {
            if (!TryReadIdAndName(item, out string? artistId, out string? name))
            {
                return null;
            }

            return new SpotifyArtistDto
            {
                SpotifyArtistId = artistId,
                Name = name,
                // Neuere Spotify-Antworten lassen „genres" teils weg; ohne Feld gilt die
                // Liste als leer, statt die ganze Suche scheitern zu lassen.
                Genres = item.TryGetProperty("genres", out JsonElement genres) && genres.ValueKind == JsonValueKind.Array
                    ? [.. genres.EnumerateArray()
                        .Select(g => g.GetString())
                        .Where(g => g is not null)
                        .Select(g => g!)]
                    : [],

                // Für EchoPlay genügt ein einzelnes Bild.
                ImageUrl = TryGetFirstImageUrl(item)
            };
        }

        /// <summary>Ein Album aus der Albumsuche; der erste Künstler gilt als Serienname.</summary>
        private static SpotifyAlbumDto? ReadSearchAlbum(JsonElement item)
        {
            if (!TryReadIdAndName(item, out string? albumId, out string? name))
            {
                return null;
            }

            string? artistName = item.TryGetProperty("artists", out JsonElement artists) && artists.GetArrayLength() > 0
                && artists[0].TryGetProperty("name", out JsonElement an)
                ? an.GetString()
                : null;

            return new SpotifyAlbumDto
            {
                SpotifyAlbumId = albumId,
                Title = name,
                TotalTracks = item.TryGetProperty("total_tracks", out JsonElement tt) ? tt.GetInt32() : 0,
                ImageUrl = TryGetFirstImageUrl(item),
                ArtistName = artistName
            };
        }

        /// <summary>Ein Album eines Künstlers; das Cover ist das erste Bild im "images"-Array (höchste Auflösung).</summary>
        private static SpotifyAlbumDto? ReadArtistAlbum(JsonElement item)
        {
            if (!TryReadIdAndName(item, out string? albumId, out string? title))
            {
                return null;
            }

            return new SpotifyAlbumDto
            {
                SpotifyAlbumId = albumId,
                Title = title,
                ReleaseDate = ParseReleaseDate(
                    item.TryGetProperty("release_date", out JsonElement dateEl) ? dateEl.GetString() : null),
                TotalTracks = item.GetProperty("total_tracks").GetInt32(),
                ImageUrl = TryGetFirstImageUrl(item)
            };
        }

        /// <summary>
        /// Lädt alle Tracks eines Albums, auch über mehrere Seiten.
        /// Die Reihenfolge entspricht der von Spotify gelieferten Track-Reihenfolge.
        /// </summary>
        /// <param name="albumId">Die Spotify-ID des Albums.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Eine Liste roher Spotify-Trackdaten.</returns>
        public async Task<IReadOnlyList<SpotifyTrackDto>> GetAlbumTracksAsync(string albumId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(albumId);

            // Ohne Angabe liefert Spotify zwanzig Spuren je Seite; Folgen mit vielen kurzen
            // Spuren hätten sonst eine viel zu kurze Dauer. Fünfzig ist die Obergrenze je Seite.
            string? requestUri = $"albums/{albumId}/tracks?limit=50";

            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("API:Spotify:GetAlbumTracks");

            _logger.Debug(() => $"Spotify-Tracks laden für Album '{albumId}'.");

            try
            {
                List<SpotifyTrackDto> tracks = [];
                while (requestUri is not null)
                {
                    using JsonDocument document = await SendAndParseAsync(requestUri, UriKind.RelativeOrAbsolute, cancellationToken).ConfigureAwait(false);
                    ReadTracks(document.RootElement.GetProperty("items"), tracks);
                    requestUri = NextPage(document);
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

        private void ReadTracks(JsonElement items, List<SpotifyTrackDto> tracks)
        {
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
        }

        /// <summary>
        /// Liest das Erscheinungsdatum. Spotify gibt es je nach Album als Jahr („1981"), als
        /// Monat („1981-05") oder als Tag an; ein bloßes Jahr steht für den 1. Januar.
        /// </summary>
        /// <param name="value">Der Wert aus <c>release_date</c>.</param>
        /// <returns>Das Datum, oder <see langword="null"/>, wenn der Wert fehlt oder unlesbar ist.</returns>
        internal static DateTime? ParseReleaseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (value.Length == 4 && int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int year) && year >= 1)
            {
                return new DateTime(year, 1, 1);
            }

            return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out DateTime date)
                ? date
                : null;
        }

        // "next" enthält die vollständige URL zur nächsten Seite oder null bei der letzten.
        private static string? NextPage(JsonDocument document)
            => document.RootElement.TryGetProperty("next", out JsonElement nextEl) && nextEl.ValueKind == JsonValueKind.String
                ? nextEl.GetString()
                : null;

        /// <summary>
        /// Führt einen GET-Request aus, stellt den Erfolg sicher und parst die Antwort in ein
        /// <see cref="JsonDocument"/>. Wiederholungen bei 429, 5xx und Verbindungsfehlern
        /// übernimmt die Regel, die die Anwendung an den Client hängt. Der Aufrufer ist für die Entsorgung
        /// des zurückgegebenen Dokuments verantwortlich (<c>using</c>).
        /// </summary>
        /// <param name="requestUri">Die relative oder absolute Anfrage-URL.</param>
        /// <param name="uriKind">Die Art der URL (relativ, absolut oder beides).</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Das geparste JSON-Dokument der Antwort.</returns>
        private async Task<JsonDocument> SendAndParseAsync(string requestUri, UriKind uriKind, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(new Uri(requestUri, uriKind), cancellationToken).ConfigureAwait(false);

            // Transport- und Authentifizierungsfehler können hier nicht fachlich sinnvoll
            // behandelt werden und werden daher direkt weitergegeben.
            _ = response.EnsureSuccessStatusCode();

            // JsonDocument.ParseAsync puffert den gesamten Stream, daher darf der Stream
            // nach dem Parsen entsorgt werden, das Dokument bleibt gültig.
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

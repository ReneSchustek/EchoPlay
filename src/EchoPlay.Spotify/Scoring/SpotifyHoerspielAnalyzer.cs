using EchoPlay.Core.Scoring;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text.Json;

namespace EchoPlay.Spotify.Scoring
{
    /// <summary>
    /// Führt die Spotify-spezifische Hörspiel-Analyse durch.
    /// Kombiniert Genre-Prüfung, Name-Matching und Album-Struktur-Analyse
    /// zu einem reinen Analyse-Ergebnis ohne Score-Berechnung.
    /// </summary>
    internal sealed class SpotifyHoerspielAnalyzer
    {
        private readonly ISpotifyApiClient _apiClient;
        private readonly SpotifyHoerspielSettings _settings;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger;

        /// <summary>
        /// Initialisiert den Analyzer mit allen benötigten Abhängigkeiten.
        /// </summary>
        /// <param name="apiClient">Der Spotify-API-Client für Album- und Track-Abfragen.</param>
        /// <param name="options">Die konfigurierbaren Bewertungsregeln.</param>
        /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
        public SpotifyHoerspielAnalyzer(
            ISpotifyApiClient apiClient,
            IOptions<SpotifyHoerspielSettings> options,
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory)
        {
            _apiClient = apiClient;
            _settings = options.Value;
            _logger = loggerFactory.CreateLogger("SpotifyHoerspielAnalyzer");
        }

        /// <summary>
        /// Analysiert einen Spotify-Künstler hinsichtlich Hörspiel-Merkmalen.
        /// </summary>
        /// <param name="source">Der Spotify-Künstler.</param>
        /// <param name="searchQuery">Ursprünglicher Suchbegriff.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Das Analyse-Ergebnis mit Boolean-Flags.</returns>
        public async Task<SpotifyHoerspielAnalysis> AnalyzeAsync(
            SpotifyArtistDto source,
            string searchQuery,
            CancellationToken cancellationToken = default)
        {
            _logger.Debug(() => $"Hörspiel-Analyse für Künstler '{source.Name}' (ID: {source.SpotifyArtistId}) gestartet.");

            bool hasNegativeMusicGenre = HasNegativeMusicGenre(source.Genres);
            bool isKnownSeries = HoerspielNameMatcher.IsKnownSeries(source.Name, _settings.DefaultKnownSeries);

            string normalizedName = HoerspielTextNormalizer.Normalize(source.Name);
            string normalizedQuery = HoerspielTextNormalizer.Normalize(searchQuery);

            bool nameContainsQuery = normalizedName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
            bool hasNumberVariantMatch = HoerspielNameMatcher.HasNumberVariantMatch(normalizedName, normalizedQuery, _settings.NumberWordMapping);
            bool hasExactWordMatch = HoerspielNameMatcher.IsExactWordMatch(normalizedName, normalizedQuery);

            (bool hasAlbums, bool hasHoerspielAlbumStructure) = await AnalyzeAlbumsAsync(source.SpotifyArtistId, cancellationToken).ConfigureAwait(false);

            List<string> debugParts = [];

            if (hasNegativeMusicGenre)
            {
                debugParts.Add($"Negatives Musikgenre: [{string.Join(", ", source.Genres)}]");
            }

            if (isKnownSeries)
            {
                debugParts.Add($"Bekannte Serie: '{source.Name}'");
            }

            if (nameContainsQuery)
            {
                debugParts.Add("Name-Contains-Match");
            }

            if (hasNumberVariantMatch)
            {
                debugParts.Add("Zahlwort-Variante");
            }

            if (hasExactWordMatch)
            {
                debugParts.Add("Exaktes Wort-Match");
            }

            if (hasHoerspielAlbumStructure)
            {
                debugParts.Add("Hörspiel-Albumstruktur");
            }
            else if (!hasAlbums)
            {
                debugParts.Add("Keine Alben");
            }

            string debugInfo = debugParts.Count > 0
                ? string.Join("; ", debugParts)
                : "Keine Indikatoren gefunden";

            _logger.Debug(() => $"Hörspiel-Analyse für '{source.Name}' abgeschlossen: {debugInfo}");

            return new SpotifyHoerspielAnalysis
            {
                HasNegativeMusicGenre = hasNegativeMusicGenre,
                IsKnownSeries = isKnownSeries,
                NameContainsQuery = nameContainsQuery,
                HasNumberVariantMatch = hasNumberVariantMatch,
                HasExactWordMatch = hasExactWordMatch,
                HasHoerspielAlbumStructure = hasHoerspielAlbumStructure,
                HasAlbums = hasAlbums,
                DebugInfo = debugInfo
            };
        }

        /// <summary>
        /// Prüft, ob mindestens ein Genre des Künstlers ein negatives Musik-Genre ist.
        /// </summary>
        /// <param name="genres">Die Genres des Künstlers.</param>
        /// <returns><c>true</c>, wenn ein negatives Genre gefunden wurde.</returns>
        private bool HasNegativeMusicGenre(IReadOnlyList<string> genres)
        {
            foreach (string genre in genres)
            {
                foreach (string negative in _settings.NegativeMusicGenres)
                {
                    if (genre.Contains(negative, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Analysiert die Album-Struktur eines Künstlers auf Hörspiel-Merkmale.
        /// </summary>
        /// <param name="artistId">Die Spotify-Artist-ID.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Ein Tupel mit Flags: ob Alben vorhanden sind und ob Hörspiel-Struktur erkannt wurde.</returns>
        private async Task<(bool HasAlbums, bool HasHoerspielStructure)> AnalyzeAlbumsAsync(string artistId, CancellationToken cancellationToken)
        {
            IReadOnlyList<SpotifyAlbumDto> albums = await _apiClient.GetArtistAlbumsAsync(
                artistId,
                _settings.AlbumsToCheck,
                cancellationToken).ConfigureAwait(false);

            if (albums.Count == 0)
            {
                return (false, false);
            }

            foreach (SpotifyAlbumDto album in albums)
            {
                IReadOnlyList<SpotifyTrackDto> tracks;

                try
                {
                    tracks = await _apiClient.GetAlbumTracksAsync(album.SpotifyAlbumId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException
                                           or TaskCanceledException
                                           or JsonException
                                           or InvalidOperationException
                                           or UriFormatException)
                {
                    // Schlägt das Laden der Tracks für ein Album fehl, wird es bei der Strukturanalyse
                    // übersprungen, um die Gesamtbewertung nicht zu blockieren.
                    _logger.Warning(
                        $"Tracks für Spotify-Album '{album.SpotifyAlbumId}' ('{album.Title}') konnten nicht geladen werden. Album wird bei der Hörspielanalyse übersprungen.");
                    _logger.Error("Fehlerdetails:", ex);
                    continue;
                }

                if (HoerspielAlbumAnalyzer.LooksLikeHoerspiel(tracks))
                {
                    return (true, true);
                }
            }

            return (true, false);
        }
    }
}

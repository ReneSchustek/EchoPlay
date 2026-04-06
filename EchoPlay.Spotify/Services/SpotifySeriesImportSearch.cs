using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Mapping;

namespace EchoPlay.Spotify.Services
{
    /// <summary>
    /// Spotify-spezifische Implementierung der Serien-Importsuche.
    /// Die Klasse kapselt den vollständigen Ablauf von der Spotify-Suche bis zur fachlich bewerteten Import-Serie.
    /// </summary>
    /// <remarks>
    /// Initialisiert den Import-Suchservice mit allen erforderlichen Spotify-Abhängigkeiten.
    /// </remarks>
    /// <param name="apiClient">Der Spotify-API-Client.</param>
    /// <param name="seriesMapper">Der Mapper für Import-Serien.</param>
    /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
    internal sealed class SpotifySeriesImportSearch(
        ISpotifyApiClient apiClient,
        SpotifySeriesMapper seriesMapper,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : ISeriesImportSearch
    {
        private readonly ISpotifyApiClient _apiClient = apiClient;
        private readonly SpotifySeriesMapper _seriesMapper = seriesMapper;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger = loggerFactory.CreateLogger("SpotifySeriesImportSearch");

        /// <summary>
        /// Sucht nach potenziellen Hörspielserien bei Spotify anhand eines Suchbegriffs.
        /// Schlägt die Bewertung eines einzelnen Künstlers fehl, wird dieser übersprungen –
        /// die restlichen Ergebnisse bleiben unberührt.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <returns>Eine Liste fachlich bewerteter Import-Serien.</returns>
        public async Task<IReadOnlyList<ImportSeries>> SearchAsync(string query)
        {
            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope($"Import:Spotify:Search");

            _logger.Debug($"Spotify-Seriensuche gestartet: '{query}'.");

            IReadOnlyList<SpotifyArtistDto> artists = await _apiClient.SearchArtistsAsync(query, limit: 10).ConfigureAwait(false);

            List<ImportSeries> results = [];

            foreach (SpotifyArtistDto artist in artists)
            {
                ImportSeries series;

                try
                {
                    series = await _seriesMapper.MapToImportSeriesAsync(artist, query).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Einzelne Bewertungsfehler dürfen die Gesamtsuche nicht unterbrechen.
                    _logger.Warning(
                        $"Bewertung für Spotify-Künstler '{artist.SpotifyArtistId}' ({artist.Name}) fehlgeschlagen. Künstler wird übersprungen.");
                    _logger.Error("Fehlerdetails:", ex);
                    continue;
                }

                // Nur Kandidaten aufnehmen, die vom Scorer als Hörspiel erkannt wurden.
                if (series.IsHoerspiel)
                {
                    results.Add(series);
                }
            }

            _logger.Info($"Spotify-Seriensuche abgeschlossen: {results.Count} Hörspielserien gefunden.");

            return results;
        }
    }
}

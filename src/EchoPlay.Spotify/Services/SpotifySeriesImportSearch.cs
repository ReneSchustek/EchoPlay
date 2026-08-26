using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Mapping;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

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
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Eine Liste fachlich bewerteter Import-Serien.</returns>
        public async Task<IReadOnlyList<ImportSeries>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            List<ImportSeries> results = [];

            await foreach (ImportSeries series in SearchStreamAsync(query, cancellationToken).ConfigureAwait(false))
            {
                results.Add(series);
            }

            return results;
        }

        /// <summary>
        /// Sucht nach Hörspielserien und meldet jeden Treffer, sobald er feststeht.
        /// Schlägt die Bewertung eines einzelnen Künstlers fehl, wird dieser übersprungen –
        /// die restlichen Ergebnisse bleiben unberührt. Ein Abbruch der umgebenden Operation
        /// beendet die Suche dagegen sofort und gilt nicht als Fehlschlag.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Die Hörspielserien in der Reihenfolge ihrer Bewertung.</returns>
        public async IAsyncEnumerable<ImportSeries> SearchStreamAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(query);

            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope($"Import:Spotify:Search");

            _logger.Debug(() => $"Spotify-Seriensuche gestartet: '{query}'.");

            IReadOnlyList<SpotifyArtistDto> artists = await _apiClient.SearchArtistsAsync(query, limit: 10, cancellationToken).ConfigureAwait(false);

            int found = 0;

            foreach (SpotifyArtistDto artist in artists)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ImportSeries? series = await MapCandidateAsync(artist, query, cancellationToken).ConfigureAwait(false);

                // Nur Kandidaten aufnehmen, die vom Scorer als Hörspiel erkannt wurden.
                if (series is null || !series.IsHoerspiel)
                {
                    continue;
                }

                found++;
                yield return series;
            }

            _logger.Info("Spotify-Seriensuche abgeschlossen: {ResultCount} Hörspielserien gefunden.", found);
        }

        /// <summary>
        /// Bewertet einen Künstler und baut daraus das Serienmodell.
        /// </summary>
        /// <param name="artist">Der zu bewertende Künstler.</param>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>
        /// Das Serienmodell, oder <see langword="null"/>, wenn die Bewertung fehlschlug.
        /// Ein Abbruch wird durchgereicht und beendet die Suche.
        /// </returns>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Einzelne Bewertungsfehler aus der Scoring-/Mapper-Pipeline dürfen die Gesamtsuche nicht abbrechen; der Mapper kombiniert mehrere Heuristiken und die konkreten Fehlertypen sind nicht vollständig vorhersehbar.")]
        private async Task<ImportSeries?> MapCandidateAsync(
            SpotifyArtistDto artist,
            string query,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _seriesMapper.MapToImportSeriesAsync(artist, query, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ein Abbruch ist kein Fehlschlag: Der Nutzer tippt weiter, die Suche ist überholt.
                throw;
            }
            catch (Exception ex)
            {
                // Einzelne Bewertungsfehler dürfen die Gesamtsuche nicht unterbrechen.
                _logger.Warning(
                    $"Bewertung für Spotify-Künstler '{artist.SpotifyArtistId}' ({artist.Name}) fehlgeschlagen. Künstler wird übersprungen.");
                _logger.Error("Fehlerdetails:", ex);
                return null;
            }
        }
    }
}

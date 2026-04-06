using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Mapping;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Scoring;

namespace EchoPlay.AppleMusic.Clients
{
    /// <summary>
    /// Implementiert die fachliche Suche nach Hörspielserien über die iTunes Search API.
    /// Sucht nach Künstlern und bewertet diese mittels Scoring-Pipeline.
    /// </summary>
    public sealed class AppleMusicSeriesSearch : ISeriesImportSearch
    {
        private readonly IAppleMusicSearchClient _searchClient;
        private readonly IHoerspielScorer<ITunesArtistDto> _scorer;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger;

        /// <summary>
        /// Initialisiert die Seriensuche mit Search-Client und Scorer.
        /// </summary>
        /// <param name="searchClient">Der iTunes-Search-Client für Künstler-Suche.</param>
        /// <param name="scorer">Der Hörspiel-Scorer für die Bewertung der Suchergebnisse.</param>
        /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
        public AppleMusicSeriesSearch(
            IAppleMusicSearchClient searchClient,
            IHoerspielScorer<ITunesArtistDto> scorer,
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(searchClient);
            ArgumentNullException.ThrowIfNull(scorer);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _searchClient = searchClient;
            _scorer = scorer;
            _logger = loggerFactory.CreateLogger("AppleMusicSeriesSearch");
        }

        /// <summary>
        /// Sucht nach importierbaren Hörspielserien anhand eines freien Suchbegriffs.
        /// Schlägt die Bewertung eines einzelnen Künstlers fehl, wird dieser übersprungen –
        /// die restlichen Ergebnisse bleiben unberührt.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <returns>Eine fachlich bewertete Liste importierbarer Serien.</returns>
        public async Task<IReadOnlyList<ImportSeries>> SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Suchbegriff darf nicht leer sein.", nameof(query));
            }

            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("Import:AppleMusic:Search");

            _logger.Debug($"Apple-Music-Seriensuche gestartet: '{query}'.");

            ITunesResponseDto<ITunesArtistDto> response =
                await _searchClient.SearchArtistsAsync(query).ConfigureAwait(false);

            if (response.Results.Count == 0)
            {
                _logger.Debug("Keine Künstler gefunden.");
                return [];
            }

            List<ImportSeries> results = new();

            foreach (ITunesArtistDto artist in response.Results)
            {
                HoerspielScoreResult score;

                try
                {
                    score = await _scorer.ScoreAsync(artist, query).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Einzelne Bewertungsfehler dürfen die Gesamtsuche nicht unterbrechen.
                    _logger.Warning(
                        $"Bewertung für iTunes-Künstler '{artist.ArtistId}' ({artist.ArtistName}) fehlgeschlagen. Künstler wird übersprungen.");
                    _logger.Error("Fehlerdetails:", ex);
                    continue;
                }

                ImportSeries series = AppleMusicSeriesMapper.Map(artist, score);

                // Nur Kandidaten aufnehmen, die vom Scorer als Hörspiel erkannt wurden
                if (series.IsHoerspiel)
                {
                    results.Add(series);
                }
            }

            _logger.Info($"Apple-Music-Seriensuche abgeschlossen: {results.Count} Hörspielserien gefunden.");

            return results;
        }
    }
}

using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Scoring;
using EchoPlay.Spotify.Dtos;

namespace EchoPlay.Spotify.Mapping
{
    /// <summary>
    /// Übersetzt Spotify-Künstlerdaten in fachliche Import-Serienmodelle.
    /// Die Klasse kapselt den bewussten Übergang von anbietergebundenen Daten in Core-Modelle.
    /// </summary>
    /// <remarks>
    /// Initialisiert den Mapper mit dem Spotify-spezifischen Hörspiel-Scorer.
    /// </remarks>
    /// <param name="scorer">Der zu verwendende Scorer.</param>
    internal sealed class SpotifySeriesMapper(IHoerspielScorer<SpotifyArtistDto> scorer)
    {
        private readonly IHoerspielScorer<SpotifyArtistDto> _scorer = scorer;

        /// <summary>
        /// Erstellt ein Import-Serienmodell aus einem Spotify-Künstler.
        /// </summary>
        /// <param name="artist">Der Spotify-Künstler.</param>
        /// <param name="searchQuery">Der ursprüngliche Suchbegriff des Benutzers.</param>
        /// <returns>Das fachlich bewertete Import-Serienmodell.</returns>
        public async Task<ImportSeries> MapToImportSeriesAsync(SpotifyArtistDto artist, string searchQuery)
        {
            HoerspielScoreResult scoreResult = await _scorer.ScoreAsync(artist, searchQuery).ConfigureAwait(false);

            return new ImportSeries
            {
                SourceSeriesId = artist.SpotifyArtistId,
                Source = "Spotify",
                Title = artist.Name,
                Description = null,
                CoverImageUrl = artist.ImageUrl,
                IsHoerspiel = scoreResult.IsHoerspiel,
                Score = scoreResult.Score
            };
        }
    }
}

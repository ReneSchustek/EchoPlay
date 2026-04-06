using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Scoring;
using System.Globalization;

namespace EchoPlay.AppleMusic.Mapping
{
    /// <summary>
    /// Wandelt iTunes-Künstler-Daten in importierbare Serienmodelle um.
    /// Die iTunes Search API liefert keine Editorial Notes oder Artwork auf Künstler-Ebene,
    /// daher bleiben Description und CoverImageUrl leer.
    /// </summary>
    public static class AppleMusicSeriesMapper
    {
        /// <summary>
        /// Erstellt aus einem iTunes-Künstler und dessen Bewertung eine ImportSeries.
        /// </summary>
        /// <param name="artist">Der iTunes-Künstler.</param>
        /// <param name="scoreResult">Das Ergebnis der Hörspiel-Bewertung.</param>
        /// <returns>Das importierbare Serienmodell.</returns>
        public static ImportSeries Map(ITunesArtistDto artist, HoerspielScoreResult scoreResult)
        {
            return new ImportSeries
            {
                SourceSeriesId = artist.ArtistId.ToString(CultureInfo.InvariantCulture),
                Source = "AppleMusic",
                Title = artist.ArtistName,
                Description = null,
                CoverImageUrl = null,
                IsHoerspiel = scoreResult.IsHoerspiel,
                Score = scoreResult.Score
            };
        }
    }
}

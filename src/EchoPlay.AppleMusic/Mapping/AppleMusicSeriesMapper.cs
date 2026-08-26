using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Scoring;
using System.Globalization;

namespace EchoPlay.AppleMusic.Mapping
{
    /// <summary>
    /// Wandelt iTunes-Künstler-Daten in importierbare Serienmodelle um.
    /// Die iTunes Search API liefert auf Künstler-Ebene weder Editorial Notes noch Artwork —
    /// die Beschreibung bleibt deshalb leer, das Cover stammt aus dem ersten Album der Serie.
    /// </summary>
    public static class AppleMusicSeriesMapper
    {
        /// <summary>
        /// Erstellt aus einem iTunes-Künstler und dessen Bewertung eine ImportSeries.
        /// </summary>
        /// <param name="artist">Der iTunes-Künstler.</param>
        /// <param name="scoreResult">Das Ergebnis der Hörspiel-Bewertung.</param>
        /// <param name="coverImageUrl">
        /// Cover-Adresse aus der Albenprüfung. Bleibt sie leer, sucht die Trefferkarte das
        /// Cover in der eigenen Datenbank — für bereits importierte Serien reicht das.
        /// </param>
        /// <returns>Das importierbare Serienmodell.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "Die Adresse stammt als Zeichenkette aus der Anbieter-Antwort und wird in ImportSeries ebenfalls als Zeichenkette geführt; ein Uri-Umweg brächte nur Umwandlungen.")]
        public static ImportSeries Map(ITunesArtistDto artist, HoerspielScoreResult scoreResult, string? coverImageUrl = null)
        {
            ArgumentNullException.ThrowIfNull(artist);
            ArgumentNullException.ThrowIfNull(scoreResult);

            return new ImportSeries
            {
                SourceSeriesId = artist.ArtistId.ToString(CultureInfo.InvariantCulture),
                Source = "AppleMusic",
                Title = artist.ArtistName,
                Description = null,
                CoverImageUrl = coverImageUrl,
                IsHoerspiel = scoreResult.IsHoerspiel,
                Score = scoreResult.Score
            };
        }
    }
}

using EchoPlay.Core.Scoring;

namespace EchoPlay.Spotify.Scoring
{
    /// <summary>
    /// Enthält alle konfigurierbaren Werte für die Spotify-spezifische Hörspiel-Bewertung.
    /// Erbt gemeinsame Scoring-Parameter von <see cref="HoerspielScorerSettings"/>
    /// und ergänzt Spotify-eigene Regeln (z.B. Musik-Genre-Erkennung).
    /// </summary>
    internal sealed class SpotifyHoerspielSettings : HoerspielScorerSettings
    {
        /// <summary>
        /// Genres, die eindeutig auf Musik hindeuten und zur sofortigen Ablehnung führen.
        /// </summary>
        public List<string> NegativeMusicGenres { get; init; } =
        [
            "pop", "rock", "hip hop", "rap", "metal", "electronic",
            "dance", "r&b", "country", "punk", "jazz", "blues",
            "classical", "reggae", "soul", "funk", "indie",
            "alternative", "techno", "house", "latin"
        ];
    }
}

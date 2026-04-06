using EchoPlay.Core.Scoring;

namespace EchoPlay.AppleMusic.Scoring
{
    /// <summary>
    /// Enthält alle konfigurierbaren Werte für die Apple-Music-spezifische Hörspiel-Bewertung.
    /// Erbt gemeinsame Scoring-Parameter von <see cref="HoerspielScorerSettings"/>
    /// und ergänzt Apple-Music-eigene Regeln (Genre-Erkennung über iTunes Search API).
    /// </summary>
    internal sealed class AppleMusicHoerspielSettings : HoerspielScorerSettings
    {
        /// <summary>
        /// Genres, die auf Hörspiel-Inhalte hindeuten.
        /// Die iTunes Search API liefert primaryGenreName auf Künstler-Ebene.
        /// </summary>
        public List<string> HoerspielGenres { get; init; } =
        [
            "Hörspiele",
            "Hörbücher",
            "Spoken Word",
            "Kinder und Jugend"
        ];

        /// <summary>
        /// Bonus, wenn das primäre Genre des Künstlers auf Hörspiel hinweist.
        /// </summary>
        public int GenreBonus { get; init; } = 30;
    }
}

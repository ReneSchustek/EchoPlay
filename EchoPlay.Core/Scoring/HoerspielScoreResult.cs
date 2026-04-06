namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Ergebnis einer fachlichen Hörspiel-Bewertung.
    /// </summary>
    public sealed class HoerspielScoreResult
    {
        /// <summary>
        /// Gibt an, ob der Kandidat als Hörspiel akzeptiert werden kann.
        /// </summary>
        public bool IsHoerspiel { get; init; }

        /// <summary>
        /// Erreichter Bewertungs-Score.
        /// </summary>
        public int Score { get; init; }

        /// <summary>
        /// Fachlicher Hauptgrund für die Entscheidung.
        /// </summary>
        public HoerspielDecisionReason Reason { get; init; }

        /// <summary>
        /// Spotify-Artist-ID des bewerteten Künstlers.
        /// </summary>
        public string ArtistId { get; init; } = string.Empty;

        /// <summary>
        /// Menschenlesbare Debug-Information zur Entscheidung.
        /// </summary>
        public string DebugInfo { get; init; } = string.Empty;

        /// <summary>
        /// Erzeugt ein positives Bewertungsergebnis.
        /// </summary>
        /// <param name="artistId">Die Artist-ID.</param>
        /// <param name="reason">Der Hauptgrund der Akzeptanz.</param>
        /// <param name="score">Der erreichte Score.</param>
        /// <param name="debugInfo">Menschenlesbare Zusatzinfo.</param>
        /// <returns>Ein akzeptierendes Bewertungsergebnis.</returns>
        public static HoerspielScoreResult Yes(string artistId, HoerspielDecisionReason reason, int score, string debugInfo)
        {
            return new HoerspielScoreResult
            {
                IsHoerspiel = true,
                ArtistId = artistId,
                Reason = reason,
                Score = score,
                DebugInfo = debugInfo
            };
        }

        /// <summary>
        /// Erzeugt ein negatives Bewertungsergebnis.
        /// </summary>
        /// <param name="artistId">Die Artist-ID.</param>
        /// <param name="reason">Der Hauptgrund der Ablehnung.</param>
        /// <param name="score">Der erreichte Score.</param>
        /// <param name="debugInfo">Menschenlesbare Zusatzinfo.</param>
        /// <returns>Ein ablehnendes Bewertungsergebnis.</returns>
        public static HoerspielScoreResult No(string artistId, HoerspielDecisionReason reason, int score, string debugInfo)
        {
            return new HoerspielScoreResult
            {
                IsHoerspiel = false,
                ArtistId = artistId,
                Reason = reason,
                Score = score,
                DebugInfo = debugInfo
            };
        }
    }
}

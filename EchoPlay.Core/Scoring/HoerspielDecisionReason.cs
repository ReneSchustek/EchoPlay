namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Benennt den fachlichen Hauptgrund für die Hörspiel-Entscheidung.
    /// </summary>
    public enum HoerspielDecisionReason
    {
        /// <summary>
        /// Score-basierte Entscheidung, kein einzelner harter Grund.
        /// </summary>
        None,

        /// <summary>
        /// Name gehört zu einer bekannten Hörspielserie.
        /// </summary>
        KnownSeriesName,

        /// <summary>
        /// Genre deutet eindeutig auf Musik hin (Negativfilter).
        /// </summary>
        NegativeMusicGenre
    }
}

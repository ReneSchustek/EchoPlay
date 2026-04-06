namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Definiert den fachlichen Vertrag zur Bewertung, ob ein Import-Kandidat als Hörspiel geeignet ist.
    /// </summary>
    /// <typeparam name="TSource">Anbieter- oder quellenspezifischer Datentyp.</typeparam>
    public interface IHoerspielScorer<TSource>
    {
        /// <summary>
        /// Führt eine asynchrone fachliche Hörspiel-Bewertung durch.
        /// </summary>
        /// <param name="source">Die zu bewertenden Quelldaten.</param>
        /// <param name="searchQuery">Ursprünglicher Suchbegriff.</param>
        /// <returns>Das Bewertungsergebnis.</returns>
        Task<HoerspielScoreResult> ScoreAsync(
            TSource source,
            string searchQuery);
    }
}

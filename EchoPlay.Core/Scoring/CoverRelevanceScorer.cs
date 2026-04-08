namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Bewertet die Relevanz eines Cover-Suchergebnisses anhand des Release-Titels.
    /// Verhindert, dass irrelevante Treffer (anderer Künstler, andere Serie) als
    /// Cover übernommen werden. Die Bewertung basiert auf Textübereinstimmung
    /// zwischen dem Release-Titel und den Suchkriterien (Serienname, Episodennummer, Folgentitel).
    /// </summary>
    public static class CoverRelevanceScorer
    {
        /// <summary>
        /// Mindest-Score, ab dem ein Treffer als relevant gilt.
        /// Ein Treffer muss mindestens den Seriennamen enthalten (50 Punkte).
        /// </summary>
        public const int MinimumThreshold = 50;

        /// <summary>
        /// Berechnet einen Relevanz-Score für einen Release-Titel.
        /// </summary>
        /// <param name="releaseTitle">Der Titel des Suchergebnisses (z.B. Albumname).</param>
        /// <param name="seriesName">Der Name der Serie (z.B. „Die drei ??? Kids").</param>
        /// <param name="episodeNumber">Die Folgennummer oder <see langword="null"/>.</param>
        /// <param name="episodeTitle">Der kurze Folgentitel oder <see langword="null"/>.</param>
        /// <returns>Score zwischen 0 und 100. Höher = relevanter.</returns>
        public static int CalculateScore(
            string? releaseTitle,
            string seriesName,
            int? episodeNumber,
            string? episodeTitle)
        {
            if (string.IsNullOrWhiteSpace(releaseTitle) || string.IsNullOrWhiteSpace(seriesName))
            {
                return 0;
            }

            string normalizedRelease = HoerspielTextNormalizer.Normalize(releaseTitle);
            string normalizedSeries = HoerspielTextNormalizer.Normalize(seriesName);

            int score = 0;

            // Serienname im Release-Titel: Grundvoraussetzung für Relevanz
            if (normalizedRelease.Contains(normalizedSeries))
            {
                score += 50;
            }

            // Episodennummer im Release-Titel: starker Indikator für korrekte Folge
            if (episodeNumber.HasValue)
            {
                string numberText = episodeNumber.Value.ToString();

                if (normalizedRelease.Contains(numberText))
                {
                    score += 30;
                }
            }

            // Folgentitel-Schlagwort: zusätzliche Bestätigung
            if (!string.IsNullOrWhiteSpace(episodeTitle))
            {
                string normalizedEpisode = HoerspielTextNormalizer.Normalize(episodeTitle);

                // Kurze Titel (≤3 Zeichen nach Normalisierung) sind zu generisch
                if (normalizedEpisode.Length > 3 && normalizedRelease.Contains(normalizedEpisode))
                {
                    score += 20;
                }
            }

            return Math.Min(score, 100);
        }
    }
}

namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Stellt provider-neutrale Name-Matching-Heuristiken für die Hörspiel-Bewertung bereit.
    /// Kapselt den Abgleich gegen bekannte Serien, Zahlwort-Varianten und exaktes Wort-Matching,
    /// damit Spotify- und Apple-Music-Analyzer dieselbe Logik teilen.
    /// </summary>
    public static class HoerspielNameMatcher
    {
        /// <summary>
        /// Prüft, ob der Künstlername einer der bekannten Hörspielserien entspricht.
        /// Name und Serien werden vor dem Vergleich normalisiert.
        /// </summary>
        /// <param name="artistName">Der zu prüfende Künstlername.</param>
        /// <param name="knownSeries">Die Namen bekannter Hörspielserien.</param>
        /// <returns><c>true</c>, wenn eine bekannte Serie erkannt wurde.</returns>
        public static bool IsKnownSeries(string artistName, IEnumerable<string> knownSeries)
        {
            ArgumentNullException.ThrowIfNull(artistName);
            ArgumentNullException.ThrowIfNull(knownSeries);

            string normalizedName = HoerspielTextNormalizer.Normalize(artistName);

            foreach (string series in knownSeries)
            {
                string normalizedSeries = HoerspielTextNormalizer.Normalize(series);

                if (normalizedName.Contains(normalizedSeries, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Prüft, ob eine Zahlwort-Variante des Suchbegriffs im normalisierten Künstlernamen enthalten ist.
        /// </summary>
        /// <param name="normalizedName">Der normalisierte Künstlername.</param>
        /// <param name="normalizedQuery">Der normalisierte Suchbegriff.</param>
        /// <param name="numberWordMapping">Zuordnung von Ziffern zu Zahlwörtern.</param>
        /// <returns><c>true</c>, wenn eine Zahlwort-Variante gefunden wurde.</returns>
        public static bool HasNumberVariantMatch(
            string normalizedName,
            string normalizedQuery,
            IReadOnlyDictionary<string, string> numberWordMapping)
        {
            ArgumentNullException.ThrowIfNull(normalizedName);
            ArgumentNullException.ThrowIfNull(normalizedQuery);
            ArgumentNullException.ThrowIfNull(numberWordMapping);

            foreach (string variant in GenerateNumberVariants(normalizedQuery, numberWordMapping))
            {
                if (normalizedName.Contains(variant, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Prüft, ob alle Wörter des Suchbegriffs als eigenständige Wörter im Künstlernamen vorkommen.
        /// </summary>
        /// <param name="normalizedName">Der normalisierte Künstlername.</param>
        /// <param name="normalizedQuery">Der normalisierte Suchbegriff.</param>
        /// <returns><c>true</c>, wenn ein exaktes Wort-Match vorliegt.</returns>
        public static bool IsExactWordMatch(string normalizedName, string normalizedQuery)
        {
            ArgumentNullException.ThrowIfNull(normalizedName);
            ArgumentNullException.ThrowIfNull(normalizedQuery);

            string[] nameWords = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] queryWords = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Alle Wörter des Suchbegriffs müssen als eigenständige Wörter im Namen vorkommen
            foreach (string queryWord in queryWords)
            {
                bool found = false;

                foreach (string nameWord in nameWords)
                {
                    if (string.Equals(nameWord, queryWord, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return queryWords.Length > 0;
        }

        /// <summary>
        /// Erzeugt Namensvarianten durch Ersetzung von Ziffern durch Zahlwörter und umgekehrt.
        /// </summary>
        /// <param name="normalizedQuery">Der normalisierte Suchbegriff.</param>
        /// <param name="numberWordMapping">Zuordnung von Ziffern zu Zahlwörtern.</param>
        /// <returns>Liste der erzeugten Varianten (ohne das Original).</returns>
        private static List<string> GenerateNumberVariants(
            string normalizedQuery,
            IReadOnlyDictionary<string, string> numberWordMapping)
        {
            List<string> variants = [];

            foreach (KeyValuePair<string, string> mapping in numberWordMapping)
            {
                // Ziffer → Zahlwort
                if (normalizedQuery.Contains(mapping.Key, StringComparison.Ordinal))
                {
                    variants.Add(normalizedQuery.Replace(mapping.Key, mapping.Value, StringComparison.Ordinal));
                }

                // Zahlwort → Ziffer
                if (normalizedQuery.Contains(mapping.Value, StringComparison.Ordinal))
                {
                    variants.Add(normalizedQuery.Replace(mapping.Value, mapping.Key, StringComparison.Ordinal));
                }
            }

            return variants;
        }
    }
}

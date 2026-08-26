using EchoPlay.Core.Models.Import;
using System;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Der Rang eines Treffers in der Ergebnisliste.
    /// </summary>
    /// <param name="MatchesQuery">Ob Titel oder Künstler den Suchbegriff enthalten.</param>
    /// <param name="Score">Die Bewertung des Anbieters.</param>
    /// <remarks>
    /// Die Treffer erscheinen einzeln, sobald sie feststehen — eine Sortierung am Ende gäbe
    /// es dafür nicht mehr, sie käme erst, wenn der letzte Anbieter geantwortet hat. Der Rang
    /// wird deshalb je Treffer gebildet, und die Karte wandert beim Einfügen an ihre Stelle.
    /// </remarks>
    internal readonly record struct SearchResultRank(bool MatchesQuery, int Score)
    {
        /// <summary>
        /// Bildet den Rang eines Treffers zum Suchbegriff.
        /// </summary>
        /// <param name="series">Der Treffer.</param>
        /// <param name="searchNeedle">
        /// Der Suchbegriff, unverändert. Der Vergleich ignoriert Groß- und Kleinschreibung,
        /// ein vorheriges Umwandeln in Großbuchstaben war irreführend.
        /// </param>
        /// <returns>Der Rang.</returns>
        public static SearchResultRank Create(ImportSeries series, string searchNeedle)
        {
            ArgumentNullException.ThrowIfNull(series);

            bool matches = series.Title.Contains(searchNeedle, StringComparison.OrdinalIgnoreCase)
                        || (series.ArtistName?.Contains(searchNeedle, StringComparison.OrdinalIgnoreCase) ?? false);

            return new SearchResultRank(matches, series.Score);
        }

        /// <summary>
        /// Gibt an, ob dieser Treffer vor <paramref name="other"/> stehen soll.
        /// Namenstreffer zuerst, danach die höhere Bewertung.
        /// </summary>
        /// <param name="other">Der Rang des bereits eingereihten Treffers.</param>
        /// <returns><c>true</c>, wenn dieser Treffer weiter oben gehört.</returns>
        public bool RanksBefore(SearchResultRank other)
        {
            if (MatchesQuery != other.MatchesQuery)
            {
                return MatchesQuery;
            }

            return Score > other.Score;
        }
    }
}

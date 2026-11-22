namespace EchoPlay.Benchmarks.Cases.Infrastructure
{
    /// <summary>
    /// Größe und Verteilung der Produktivbibliothek, aus einem lesenden Abzug vom 20.11.2026. Der
    /// Messbestand (<c>LibrarySeed</c>), der Ordnerbaum und die Titel richten sich danach.
    /// </summary>
    /// <remarks>
    /// Eigene Klasse ohne Abhängigkeiten, damit auch Testprojekte ohne Datenschicht sie nutzen.
    /// </remarks>
    internal static class LibraryShape
    {
        /// <summary>Serien im Bestand.</summary>
        public const int SeriesCount = 105;

        /// <summary>Serien mit lokalem Ordner.</summary>
        public const int LocalSeriesCount = 94;

        /// <summary>Abonnierte Serien.</summary>
        public const int SubscribedSeriesCount = 103;

        /// <summary>Überwachte Serien.</summary>
        public const int WatchedSeriesCount = 4;

        /// <summary>Folgen im Bestand.</summary>
        public const int EpisodeCount = 5118;

        /// <summary>Folgen der größten Serie.</summary>
        public const int LargestSeriesEpisodes = 340;

        /// <summary>Abgeschlossene Hörstände.</summary>
        public const int CompletedStates = 3298;

        /// <summary>
        /// Fortsetzbare Hörstände. Der Produktivbestand führt kaum welche; einige braucht
        /// „Weiterhören“, sonst misst die Abfrage über eine leere Menge.
        /// </summary>
        public const int InProgressStates = 40;
    }
}

namespace EchoPlay.LocalLibrary.Analysis
{
    /// <summary>
    /// Analysiert einen Bibliotheks-Wurzelordner und schlägt passende Episodenmuster vor,
    /// die im <c>EpisodeFolderPattern</c> der Einstellungen verwendet werden können.
    /// </summary>
    public interface IEpisodePatternAnalyzer
    {
        /// <summary>
        /// Analysiert die Episodenordner-Namen aller Serien-Unterordner innerhalb von
        /// <paramref name="libraryRootPath"/> und bewertet, welche der vordefinierten Muster
        /// am besten passen. Unterstützt sowohl die klassische Drei-Ebenen-Struktur
        /// (Root → Serie → Episodenordner) als auch flache Serien (MP3s direkt im Serienordner).
        /// </summary>
        /// <param name="libraryRootPath">
        /// Vollständiger Pfad zum Bibliotheks-Wurzelordner, etwa <c>D:\Mp3\Hörspiele</c>.
        /// </param>
        /// <returns>
        /// Sortierte Liste von Vorschlägen, bester Treffer zuerst.
        /// Leer, wenn der Ordner nicht existiert oder kein Muster mindestens die Hälfte der
        /// Namen trifft. Geprüft werden die ersten zwanzig Serien.
        /// </returns>
        Task<IReadOnlyList<PatternSuggestion>> AnalyzeAsync(string libraryRootPath);
    }
}

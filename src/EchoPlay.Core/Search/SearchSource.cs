namespace EchoPlay.Core.Search
{
    /// <summary>
    /// Gibt an, aus welcher Quelle Suchergebnisse stammen sollen.
    /// Wird in der Suche-Ansicht genutzt, um den Suchbereich einzuschränken
    /// oder alle verfügbaren Quellen gleichzeitig zu durchsuchen.
    /// </summary>
    public enum SearchSource
    {
        /// <summary>
        /// Nur Online-Quellen (Spotify oder Apple Music, je nach aktivem Anbieter).
        /// </summary>
        Online,

        /// <summary>
        /// Nur die lokale Bibliothek (bereits importierte Serien in der Datenbank).
        /// </summary>
        Local,

        /// <summary>
        /// Sowohl Online-Quellen als auch die lokale Bibliothek.
        /// </summary>
        Both
    }
}

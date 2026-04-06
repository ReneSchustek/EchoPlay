namespace EchoPlay.LocalLibrary.Models
{
    /// <summary>
    /// Enthält das Scan-Ergebnis für einen einzelnen Episodenordner.
    /// </summary>
    public sealed class LocalEpisodeScan
    {
        /// <summary>
        /// Absoluter Pfad zum Episodenordner.
        /// </summary>
        public string FolderPath { get; init; } = string.Empty;

        /// <summary>
        /// Aus dem Ordnernamen geparste Episodennummer.
        /// Null, wenn das Muster keine Nummer enthält oder die Erkennung fehlschlug.
        /// </summary>
        public int? ParsedNumber { get; init; }

        /// <summary>
        /// Aus dem Ordnernamen geparster Episodentitel.
        /// Null, wenn das Muster keinen Titel enthält oder die Erkennung fehlschlug.
        /// </summary>
        public string? ParsedTitle { get; init; }

        /// <summary>
        /// Absoluter Pfade aller gefundenen Audiodateien, sortiert nach Dateiname.
        /// </summary>
        public IReadOnlyList<string> TrackPaths { get; init; } = [];

        /// <summary>
        /// Anzahl der gefundenen Audiodateien.
        /// </summary>
        public int TrackCount => TrackPaths.Count;
    }
}

namespace EchoPlay.LocalLibrary.Models
{
    /// <summary>
    /// Enthält das vollständige Scan-Ergebnis für eine Hörspielserie,
    /// bestehend aus dem Serienordner und allen gefundenen Episoden.
    /// </summary>
    public sealed class LocalScanResult
    {
        /// <summary>
        /// Name des Serienordners (nicht der vollständige Pfad).
        /// </summary>
        public string SeriesName { get; init; } = string.Empty;

        /// <summary>
        /// Absoluter Pfad zum Serienordner.
        /// </summary>
        public string SeriesFolderPath { get; init; } = string.Empty;

        /// <summary>
        /// Alle gefundenen Episoden innerhalb des Serienordners.
        /// </summary>
        public IReadOnlyList<LocalEpisodeScan> Episodes { get; init; } = [];
    }
}

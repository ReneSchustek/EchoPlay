namespace EchoPlay.LocalLibrary.Models
{
    /// <summary>
    /// Ergebnis der Analyse für den Ordnerstruktur-Assistenten.
    /// Enthält alle geplanten Verschiebe-Aktionen als Vorschau – noch wurde nichts verschoben.
    /// </summary>
    public sealed class RestructurePreview
    {
        /// <summary>Absoluter Pfad des Serienordners.</summary>
        public required string SeriesFolderPath { get; init; }

        /// <summary>
        /// Geplante Verschiebe-Aktionen, sortiert nach Episodennummer.
        /// Jede Aktion beschreibt eine Datei und ihren Zielordner.
        /// </summary>
        public required IReadOnlyList<RestructureAction> Actions { get; init; }

        /// <summary>
        /// Anzahl der Audiodateien, die verschoben werden sollen.
        /// Entspricht <c>Actions.Count</c> – als Kurzform für die Anzeige.
        /// </summary>
        public int FileCount => Actions.Count;

        /// <summary>
        /// Anzahl der neuen Ordner, die angelegt werden.
        /// Kann kleiner als <see cref="FileCount"/> sein, wenn mehrere Dateien
        /// in denselben Ordner verschoben werden (z.B. Kassetten a+b zusammen).
        /// </summary>
        public int FolderCount { get; init; }

        /// <summary>
        /// True wenn keine verschiebbaren Dateien gefunden wurden.
        /// Kann passieren wenn der Serienordner bereits eine Ordnerstruktur hat.
        /// </summary>
        public bool IsEmpty => Actions.Count == 0;
    }
}

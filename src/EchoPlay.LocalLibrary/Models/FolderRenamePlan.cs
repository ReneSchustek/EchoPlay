namespace EchoPlay.LocalLibrary.Models
{
    /// <summary>
    /// Vorschau für das Umbenennen der Folgenordner einer Serie. Noch ist nichts umbenannt.
    /// </summary>
    public sealed class FolderRenamePlan
    {
        /// <summary>Absoluter Pfad des Serienordners.</summary>
        public required string SeriesFolderPath { get; init; }

        /// <summary>Alle Folgenordner mit Audiodateien, in der Reihenfolge des Serienordners.</summary>
        public required IReadOnlyList<FolderRenameItem> Items { get; init; }

        /// <summary>
        /// Ob das Muster sich füllen lässt. Ein Muster mit anderen Platzhaltern als
        /// <c>{number}</c> und <c>{title}</c> ergibt keinen Namen.
        /// </summary>
        public bool PatternSupported { get; init; } = true;

        /// <summary>
        /// Ob die Serie überhaupt nummeriert ist. Ohne Nummern im Namen wäre jede Nummer nur
        /// die Reihenfolge der Ordner; sie in den Namen zu schreiben, machte eine Zufälligkeit
        /// dauerhaft.
        /// </summary>
        public bool SeriesIsNumbered { get; init; } = true;

        /// <summary>
        /// Zahl der Ordner, die ein liegengebliebenes Journal eines abgebrochenen Laufs vor
        /// dieser Vorschau zurückbenannt hat.
        /// </summary>
        public int RecoveredCount { get; init; }

        /// <summary>Die Ordner, die tatsächlich umbenannt würden.</summary>
        public IReadOnlyList<FolderRenameItem> Renames => [.. Items.Where(item => item.Status == FolderRenameStatus.Rename)];
    }
}

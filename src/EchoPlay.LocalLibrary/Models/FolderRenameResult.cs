namespace EchoPlay.LocalLibrary.Models
{
    /// <summary>
    /// Ergebnis eines Umbenennungslaufs.
    /// </summary>
    /// <param name="Moves">Je umbenanntem Ordner der alte und der neue Pfad.</param>
    /// <param name="SkippedCount">
    /// Ordner, deren Ziel zwischen Vorschau und Ausführung entstanden ist oder deren Quelle
    /// verschwunden ist. Sie bleiben unangetastet.
    /// </param>
    public sealed record FolderRenameResult(
        IReadOnlyList<(string OldPath, string NewPath)> Moves,
        int SkippedCount);
}

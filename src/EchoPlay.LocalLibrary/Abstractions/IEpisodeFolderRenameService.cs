using EchoPlay.LocalLibrary.Models;

namespace EchoPlay.LocalLibrary.Abstractions
{
    /// <summary>
    /// Benennt die Folgenordner einer Serie nach dem eingestellten Muster um. Nummer aus dem
    /// Ordnernamen, Titel aus dem Album-Tag der Audiodateien.
    /// </summary>
    public interface IEpisodeFolderRenameService
    {
        /// <summary>
        /// Erstellt die Vorschau. Liegt ein Journal eines abgebrochenen Laufs im Serienordner,
        /// wird es vorher zurückgespielt; sonst verändert die Vorschau nichts.
        /// </summary>
        /// <param name="seriesFolderPath">Absoluter Pfad des Serienordners.</param>
        /// <param name="folderPattern">Das Ordnermuster, z. B. <c>{number:000} - {title}</c>.</param>
        /// <returns>Die Vorschau mit allen Folgenordnern.</returns>
        FolderRenamePlan Plan(string seriesFolderPath, string folderPattern);

        /// <summary>
        /// Benennt die Ordner mit <see cref="FolderRenameStatus.Rename"/> um. Scheitert ein
        /// Schritt, werden alle bisherigen zurückbenannt und der Fehler weitergegeben.
        /// </summary>
        /// <param name="plan">Die Vorschau aus <see cref="Plan"/>.</param>
        /// <returns>Die ausgeführten Umbenennungen.</returns>
        FolderRenameResult Execute(FolderRenamePlan plan);
    }
}

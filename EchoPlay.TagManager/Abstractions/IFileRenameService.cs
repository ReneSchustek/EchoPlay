using EchoPlay.TagManager.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.TagManager.Abstractions
{
    /// <summary>
    /// Benennt Audiodateien nach einem konfigurierbaren Muster um – ähnlich wie MP3Tag.
    /// Unterstützte Platzhalter: <c>{title}</c>, <c>{album}</c>, <c>{artist}</c>,
    /// <c>{year}</c>, <c>{track}</c>, <c>{track:00}</c>, <c>{track:000}</c>, <c>{filename}</c>.
    /// </summary>
    public interface IFileRenameService
    {
        /// <summary>
        /// Erstellt eine Vorschau der Umbenennung, ohne Dateien zu verändern.
        /// Für jede Eingabedatei wird ein <see cref="RenamePreviewItem"/> erzeugt,
        /// das den alten und den neuen Namen enthält.
        /// </summary>
        /// <param name="files">
        /// Liste von Dateipfad-Tag-Paaren – typischerweise aus <see cref="ITagService.ReadFolderAsync"/>.
        /// </param>
        /// <param name="pattern">
        /// Muster mit Platzhaltern, z.B. <c>"{track:00} - {title}"</c>.
        /// </param>
        /// <returns>
        /// Vorschau-Liste in derselben Reihenfolge wie <paramref name="files"/>.
        /// Gibt eine leere Liste zurück, wenn <paramref name="files"/> leer ist.
        /// </returns>
        IReadOnlyList<RenamePreviewItem> BuildPreview(
            IReadOnlyList<(string FilePath, AudioTag Tag)> files,
            string pattern);

        /// <summary>
        /// Benennt alle Dateien gemäß dem Muster um.
        /// Dateien, bei denen alter und neuer Name übereinstimmen, werden übersprungen.
        /// Dateien, die nicht umbenannt werden können (z.B. gesperrt), werden geloggt und übersprungen.
        /// </summary>
        /// <param name="files">
        /// Liste von Dateipfad-Tag-Paaren – muss aktuell sein (nicht gecacht vom Laden).
        /// </param>
        /// <param name="pattern">
        /// Muster mit Platzhaltern, z.B. <c>"{track:00} - {title}"</c>.
        /// </param>
        /// <returns>
        /// Anzahl der erfolgreich umbenannten Dateien.
        /// Gibt 0 zurück, wenn keine Datei umbenannt werden musste oder konnte.
        /// </returns>
        Task<int> RenameAsync(
            IReadOnlyList<(string FilePath, AudioTag Tag)> files,
            string pattern);
    }
}

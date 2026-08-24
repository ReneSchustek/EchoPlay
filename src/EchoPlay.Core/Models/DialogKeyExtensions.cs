using System.Collections.Generic;

namespace EchoPlay.Core.Models
{
    /// <summary>
    /// Eigenschaften einzelner Dialoge, die über den Namen hinausgehen.
    /// </summary>
    public static class DialogKeyExtensions
    {
        /// <summary>
        /// Rückfragen, deren Aktion Dateien auf der Platte verändert.
        /// </summary>
        /// <remarks>
        /// Alles Übrige in EchoPlay ist ein Soft-Delete und lässt sich bis zum Ablauf von
        /// <c>DbPurgeDays</c> zurückholen. Der Tag-Manager schreibt dagegen in die Dateien
        /// selbst — wer diese Rückfragen abschaltet, verliert die letzte Bremse davor.
        /// </remarks>
        private static readonly HashSet<DialogKey> WithoutUndo =
        [
            DialogKey.TagManagerRename,
            DialogKey.TagManagerSaveAll,
            DialogKey.TagManagerRemoveAll,
            DialogKey.TagManagerCoverApplyAll,
            DialogKey.TagManagerApplyToAll
        ];

        /// <summary>
        /// Gibt an, ob die Aktion hinter der Rückfrage Dateien ohne Rückweg schreibt.
        /// </summary>
        /// <param name="key">Der zu prüfende Dialog.</param>
        /// <returns><see langword="true"/>, wenn es keinen Rückweg gibt.</returns>
        public static bool WritesFilesWithoutUndo(this DialogKey key) => WithoutUndo.Contains(key);
    }
}

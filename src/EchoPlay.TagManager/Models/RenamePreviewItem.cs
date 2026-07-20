namespace EchoPlay.TagManager.Models
{
    /// <summary>
    /// Repräsentiert einen einzelnen Eintrag in der Umbennungs-Vorschau.
    /// Enthält den alten und neuen Dateinamen sowie die für das Umbenennen
    /// benötigten vollständigen Pfade.
    /// </summary>
    public sealed class RenamePreviewItem
    {
        /// <summary>
        /// Aktueller Dateiname inklusive Extension, ohne Verzeichnispfad.
        /// Beispiel: <c>"001 - Titel.mp3"</c>.
        /// </summary>
        public string OldName { get; init; } = string.Empty;

        /// <summary>
        /// Neuer Dateiname nach Anwendung des Musters, inklusive Extension.
        /// Beispiel: <c>"01 - Klassenfahrt zur Hexenburg.mp3"</c>.
        /// </summary>
        public string NewName { get; init; } = string.Empty;

        /// <summary>
        /// Vollständiger Pfad der Quelldatei (wird beim Umbenennen als Quelle übergeben).
        /// </summary>
        public string FilePath { get; init; } = string.Empty;

        /// <summary>
        /// Vollständiger Zielpfad nach der Umbenennung.
        /// Verzeichnis bleibt gleich – nur der Dateiname ändert sich.
        /// </summary>
        public string NewFilePath { get; init; } = string.Empty;

        /// <summary>
        /// Gibt an, ob alte und neue Dateinamen identisch sind.
        /// Einträge, bei denen <see cref="IsUnchanged"/> <see langword="true"/> ist,
        /// werden beim Umbenennen übersprungen.
        /// </summary>
        public bool IsUnchanged => OldName == NewName;
    }
}

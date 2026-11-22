namespace EchoPlay.LocalLibrary.Models
{
    /// <summary>
    /// Was beim Umbenennen eines Folgenordners geschehen soll, und warum gegebenenfalls nicht.
    /// </summary>
    public enum FolderRenameStatus
    {
        /// <summary>Der Ordner bekommt den neuen Namen.</summary>
        Rename,

        /// <summary>Der Ordner heißt schon so, wie das Muster es verlangt.</summary>
        Unchanged,

        /// <summary>
        /// Der Ordner trägt keine Folgennummer, etwa ein Adventskalender. Ohne Nummer lässt
        /// sich das Muster nicht füllen; der Name bleibt.
        /// </summary>
        NoNumber,

        /// <summary>Weder Album-Tag noch Ordnername liefern einen Titel.</summary>
        NoTitle,

        /// <summary>
        /// Der Zielname ist schon vergeben, durch einen anderen Ordner oder durch einen
        /// zweiten Ordner derselben Vorschau mit gleichem Ziel.
        /// </summary>
        Conflict,

        /// <summary>Der neue Pfad wäre länger, als Windows ohne Sonderschalter zulässt.</summary>
        TooLong
    }
}

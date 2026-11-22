namespace EchoPlay.LocalLibrary.Models
{
    /// <summary>
    /// Ein Folgenordner in der Vorschau: wie er heißt, wie er heißen soll und ob das geht.
    /// </summary>
    /// <param name="CurrentPath">Absoluter Pfad des Ordners.</param>
    /// <param name="TargetName">Der neue Ordnername; bei <see cref="FolderRenameStatus.NoNumber"/> und <see cref="FolderRenameStatus.NoTitle"/> der bisherige.</param>
    /// <param name="Status">Ob umbenannt wird oder warum nicht.</param>
    /// <param name="TitleFromTag">Ob der Titel aus dem Album-Tag stammt; sonst aus dem Ordnernamen.</param>
    public sealed record FolderRenameItem(
        string CurrentPath,
        string TargetName,
        FolderRenameStatus Status,
        bool TitleFromTag)
    {
        /// <summary>Der bisherige Ordnername.</summary>
        public string CurrentName => Path.GetFileName(CurrentPath);

        /// <summary>Absoluter Zielpfad im selben Serienordner.</summary>
        public string TargetPath => Path.Combine(Path.GetDirectoryName(CurrentPath) ?? string.Empty, TargetName);
    }
}

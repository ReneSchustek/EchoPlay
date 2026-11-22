namespace EchoPlay.LocalLibrary.Metadata
{
    /// <summary>
    /// Liest Titel- und Album-Tag aus Audiodateien via TagLibSharp.
    /// Unterstützt alle Formate, die TagLibSharp erkennt, darunter .mp3, .m4a, .flac und .ogg.
    /// </summary>
    public sealed class TagTitleReader : ITagTitleReader
    {
        /// <inheritdoc/>
        public (string Title, string Album) Read(string filePath)
        {
            using TagLib.File file = TagLib.File.Create(filePath);

            return (file.Tag.Title ?? string.Empty, file.Tag.Album ?? string.Empty);
        }
    }
}

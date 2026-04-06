namespace EchoPlay.LocalLibrary.Metadata
{
    /// <summary>
    /// Liest Titel- und Album-Tag aus Audiodateien via TagLibSharp.
    /// Unterstützt alle Formate, die TagLibSharp erkennt (.mp3, .m4a, .flac, .ogg u.a.).
    /// </summary>
    public sealed class TagTitleReader : ITagTitleReader
    {
        /// <summary>
        /// Liest <c>Tag.Title</c> und <c>Tag.Album</c> aus der angegebenen Audiodatei.
        /// </summary>
        /// <param name="filePath">Absoluter Pfad zur Audiodatei.</param>
        /// <returns>
        /// Ein Tupel aus <c>Title</c> und <c>Album</c>.
        /// Leere Strings, wenn die Tags nicht gesetzt sind.
        /// </returns>
        /// <exception cref="TagLib.UnsupportedFormatException">
        /// Wird geworfen, wenn das Dateiformat nicht unterstützt wird.
        /// </exception>
        /// <exception cref="System.IO.IOException">
        /// Wird geworfen, wenn die Datei nicht gelesen werden kann.
        /// </exception>
        public (string Title, string Album) Read(string filePath)
        {
            using TagLib.File file = TagLib.File.Create(filePath);

            return (file.Tag.Title ?? string.Empty, file.Tag.Album ?? string.Empty);
        }
    }
}

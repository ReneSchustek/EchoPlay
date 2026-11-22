namespace EchoPlay.LocalLibrary.Metadata
{
    /// <summary>
    /// Liest Titel- und Album-Metadaten aus einer Audiodatei.
    /// Kapselt den TagLib#-Zugriff, damit der Scanner ohne Dateisystemzugriff testbar bleibt.
    /// </summary>
    public interface ITagTitleReader
    {
        /// <summary>
        /// Liest <c>Tag.Title</c> und <c>Tag.Album</c> aus der angegebenen Audiodatei.
        /// </summary>
        /// <param name="filePath">Absoluter Pfad zur Audiodatei.</param>
        /// <returns>
        /// Ein Tupel aus <c>Title</c> und <c>Album</c>; leere Zeichenketten, wenn die Tags nicht
        /// gesetzt sind.
        /// </returns>
        /// <exception cref="TagLib.UnsupportedFormatException">Wenn TagLib# das Format nicht kennt.</exception>
        /// <exception cref="TagLib.CorruptFileException">Wenn die Datei beschädigt ist.</exception>
        /// <exception cref="System.IO.IOException">Wenn die Datei nicht lesbar ist.</exception>
        (string Title, string Album) Read(string filePath);
    }
}

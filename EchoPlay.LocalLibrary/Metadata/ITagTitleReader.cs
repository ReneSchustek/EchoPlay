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
        /// Ein Tupel aus <c>Title</c> und <c>Album</c>.
        /// Leere Strings, wenn die Tags nicht gesetzt sind.
        /// Wirft Ausnahmen bei unlesbar oder nicht unterstützten Formaten –
        /// der Aufrufer ist verantwortlich für eine angemessene Fehlerbehandlung.
        /// </returns>
        (string Title, string Album) Read(string filePath);
    }
}

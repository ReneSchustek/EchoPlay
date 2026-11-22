namespace EchoPlay.LocalLibrary.Metadata
{
    /// <summary>
    /// Liest Metadaten aus Audiodateien. Die Übernahme der Folgen hängt nur an dieser
    /// Schnittstelle und lässt sich so ohne echte Dateien prüfen.
    /// </summary>
    public interface IAudioMetadataReader
    {
        /// <summary>
        /// Liest Abspieldauer und Tracknummer aus der angegebenen Audiodatei.
        /// </summary>
        /// <param name="filePath">Absoluter Pfad zur Audiodatei.</param>
        /// <returns>
        /// Ein Tupel aus <c>Duration</c> und <c>TrackNumber</c>.
        /// <c>TrackNumber</c> ist 0, wenn keine Tracknummer in den Tags gespeichert ist.
        /// </returns>
        /// <exception cref="TagLib.UnsupportedFormatException">Wenn TagLib# das Format nicht kennt.</exception>
        /// <exception cref="TagLib.CorruptFileException">Wenn die Datei beschädigt ist.</exception>
        /// <exception cref="System.IO.IOException">Wenn die Datei nicht lesbar ist.</exception>
        (TimeSpan Duration, int TrackNumber) Read(string filePath);
    }
}

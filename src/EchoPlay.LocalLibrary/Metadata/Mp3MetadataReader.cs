namespace EchoPlay.LocalLibrary.Metadata
{
    /// <summary>
    /// Liest Metadaten aus lokalen Audiodateien via TagLibSharp.
    /// Unterstützt alle Formate, die TagLibSharp erkennt (.mp3, .m4a, .flac, .ogg u.a.).
    /// </summary>
    public sealed class Mp3MetadataReader : IMp3MetadataReader
    {
        /// <summary>
        /// Liest Abspieldauer und Tracknummer aus der angegebenen Audiodatei.
        /// </summary>
        /// <param name="filePath">Absoluter Pfad zur Audiodatei.</param>
        /// <returns>
        /// Ein Tupel aus <c>Duration</c> und <c>TrackNumber</c>.
        /// <c>TrackNumber</c> ist 0, wenn keine Tracknummer in den Tags gespeichert ist.
        /// </returns>
        /// <exception cref="TagLib.UnsupportedFormatException">
        /// Wird geworfen, wenn das Dateiformat nicht unterstützt wird.
        /// </exception>
        public (TimeSpan Duration, int TrackNumber) Read(string filePath)
        {
            using TagLib.File file = TagLib.File.Create(filePath);

            TimeSpan duration = file.Properties.Duration;

            // TrackNumber ist uint in TagLib – 0 bedeutet "nicht gesetzt"
            int trackNumber = (int)file.Tag.Track;

            return (duration, trackNumber);
        }
    }
}

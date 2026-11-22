namespace EchoPlay.LocalLibrary.Metadata
{
    /// <summary>
    /// Liest Metadaten aus lokalen Audiodateien via TagLibSharp.
    /// Unterstützt alle Formate, die TagLibSharp erkennt, darunter .mp3, .m4a, .flac und .ogg.
    /// </summary>
    public sealed class AudioMetadataReader : IAudioMetadataReader
    {
        /// <inheritdoc/>
        public (TimeSpan Duration, int TrackNumber) Read(string filePath)
        {
            using TagLib.File file = TagLib.File.Create(filePath);

            TimeSpan duration = file.Properties.Duration;

            // TrackNumber ist uint in TagLib, 0 bedeutet "nicht gesetzt"
            int trackNumber = (int)file.Tag.Track;

            return (duration, trackNumber);
        }
    }
}

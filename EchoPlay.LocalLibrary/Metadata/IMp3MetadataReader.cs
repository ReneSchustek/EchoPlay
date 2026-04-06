using System;

namespace EchoPlay.LocalLibrary.Metadata
{
    /// <summary>
    /// Definiert den Vertrag zum Auslesen von Metadaten aus Audiodateien.
    /// Ermöglicht die Entkopplung des SyncService von der konkreten Implementierung.
    /// </summary>
    public interface IMp3MetadataReader
    {
        /// <summary>
        /// Liest Abspieldauer und Tracknummer aus der angegebenen Audiodatei.
        /// </summary>
        /// <param name="filePath">Absoluter Pfad zur Audiodatei.</param>
        /// <returns>
        /// Ein Tupel aus <c>Duration</c> und <c>TrackNumber</c>.
        /// <c>TrackNumber</c> ist 0, wenn keine Tracknummer in den Tags gespeichert ist.
        /// </returns>
        (TimeSpan Duration, int TrackNumber) Read(string filePath);
    }
}

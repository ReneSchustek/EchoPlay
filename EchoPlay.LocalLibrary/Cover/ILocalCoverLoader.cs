namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Lädt ein Cover-Bild aus dem Episodenordner oder den ID3-Tags.
    /// Gibt rohe Bilddaten zurück – die Konvertierung in ein UI-Bild obliegt dem Aufrufer.
    /// </summary>
    public interface ILocalCoverLoader
    {
        /// <summary>
        /// Sucht ein Cover-Bild mit folgender Priorität:
        /// 1. <c>cover.jpg</c> im Episodenordner (<paramref name="episodeFolderPath"/>)
        /// 2. Erstes Bild aus den ID3-Tags der ersten Audiodatei (<paramref name="firstTrackPath"/>)
        /// 3. Kein Cover → gibt <see langword="null"/> zurück
        ///
        /// Fehler beim Lesen werden still ignoriert, damit ein defektes Cover die UI nicht blockiert.
        /// </summary>
        /// <param name="episodeFolderPath">
        /// Absoluter Pfad zum Episodenordner. Wird auf <c>cover.jpg</c> geprüft.
        /// <see langword="null"/> überspringt diesen Schritt.
        /// </param>
        /// <param name="firstTrackPath">
        /// Absoluter Pfad zur ersten Audiodatei der Episode für den ID3-Tag-Fallback.
        /// <see langword="null"/> überspringt diesen Schritt.
        /// </param>
        /// <returns>
        /// Rohe Bilddaten als Byte-Array oder <see langword="null"/> wenn kein Cover gefunden wurde.
        /// </returns>
        Task<byte[]?> LoadAsync(string? episodeFolderPath, string? firstTrackPath);
    }
}

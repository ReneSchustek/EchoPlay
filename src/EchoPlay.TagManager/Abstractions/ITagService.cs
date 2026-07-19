using EchoPlay.TagManager.Models;

namespace EchoPlay.TagManager.Abstractions
{
    /// <summary>
    /// Liest und schreibt Audio-Metadaten (ID3, Vorbis Comment, etc.) für lokale Audiodateien.
    /// Unterstützt MP3, FLAC, OGG, AAC, WMA und WAV über TagLib#.
    /// Felder mit dem Wert <see langword="null"/> in <see cref="AudioTag"/> werden beim Schreiben
    /// übersprungen – der bestehende Wert in der Datei bleibt unverändert.
    /// </summary>
    public interface ITagService
    {
        /// <summary>
        /// Liest alle verfügbaren Metadaten aus der angegebenen Audiodatei.
        /// </summary>
        /// <param name="filePath">Absoluter Pfad zur Audiodatei.</param>
        /// <returns>
        /// Ein <see cref="AudioTag"/> mit den gelesenen Werten.
        /// Felder, die in der Datei nicht gesetzt sind, haben den Wert <see langword="null"/>.
        /// </returns>
        /// <exception cref="System.IO.FileNotFoundException">
        /// Wird geworfen, wenn die Datei unter <paramref name="filePath"/> nicht existiert.
        /// </exception>
        /// <exception cref="TagLib.UnsupportedFormatException">
        /// Wird geworfen, wenn das Dateiformat nicht von TagLib# unterstützt wird.
        /// </exception>
        Task<AudioTag> ReadAsync(string filePath);

        /// <summary>
        /// Schreibt die gesetzten Felder aus <paramref name="tag"/> in die Datei.
        /// Felder mit dem Wert <see langword="null"/> werden ignoriert –
        /// der bestehende Wert in der Datei bleibt erhalten.
        /// </summary>
        /// <param name="filePath">Absoluter Pfad zur Audiodatei.</param>
        /// <param name="tag">
        /// Die zu schreibenden Metadaten. Nur nicht-null-Felder werden übernommen.
        /// </param>
        /// <exception cref="System.IO.FileNotFoundException">
        /// Wird geworfen, wenn die Datei unter <paramref name="filePath"/> nicht existiert.
        /// </exception>
        /// <exception cref="System.UnauthorizedAccessException">
        /// Wird geworfen, wenn die Datei schreibgeschützt ist.
        /// </exception>
        Task WriteAsync(string filePath, AudioTag tag);

        /// <summary>
        /// Ersetzt das Cover-Bild in der Audiodatei.
        /// Wird <see langword="null"/> übergeben, wird das vorhandene Cover entfernt.
        /// </summary>
        /// <param name="filePath">Absoluter Pfad zur Audiodatei.</param>
        /// <param name="imageData">
        /// Rohdaten des neuen Cover-Bilds oder <see langword="null"/>, um das Cover zu entfernen.
        /// </param>
        /// <param name="mimeType">
        /// MIME-Typ des Bilds, z.B. <c>"image/jpeg"</c>. Standard: <c>"image/jpeg"</c>.
        /// Wird ignoriert, wenn <paramref name="imageData"/> <see langword="null"/> ist.
        /// </param>
        /// <exception cref="System.IO.FileNotFoundException">
        /// Wird geworfen, wenn die Datei unter <paramref name="filePath"/> nicht existiert.
        /// </exception>
        Task WriteCoverAsync(string filePath, byte[]? imageData, string mimeType = "image/jpeg");

        /// <summary>
        /// Entfernt alle Metadaten aus der Audiodatei, inklusive Cover-Bild.
        /// Dieser Vorgang ist nicht umkehrbar.
        /// </summary>
        /// <param name="filePath">Absoluter Pfad zur Audiodatei.</param>
        /// <exception cref="System.IO.FileNotFoundException">
        /// Wird geworfen, wenn die Datei unter <paramref name="filePath"/> nicht existiert.
        /// </exception>
        Task RemoveAllTagsAsync(string filePath);

        /// <summary>
        /// Liest die Metadaten aller unterstützten Audiodateien in einem Ordner.
        /// Unterordner werden nicht einbezogen.
        /// Dateien, die nicht von TagLib# unterstützt werden, werden übersprungen.
        /// </summary>
        /// <param name="folderPath">Absoluter Pfad zum Ordner.</param>
        /// <returns>
        /// Eine Liste von Tupeln aus Dateipfad und den zugehörigen Metadaten.
        /// Gibt eine leere Liste zurück, wenn keine unterstützten Dateien gefunden wurden.
        /// </returns>
        /// <exception cref="System.IO.DirectoryNotFoundException">
        /// Wird geworfen, wenn der Ordner unter <paramref name="folderPath"/> nicht existiert.
        /// </exception>
        Task<IReadOnlyList<(string FilePath, AudioTag Tag)>> ReadFolderAsync(string folderPath);
    }
}

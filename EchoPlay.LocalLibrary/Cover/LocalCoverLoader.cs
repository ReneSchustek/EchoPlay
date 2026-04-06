using System.IO;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Lädt Cover-Bilder aus dem lokalen Dateisystem.
    /// Prüft zuerst auf eine <c>cover.jpg</c>-Datei im Episodenordner,
    /// fällt dann auf den ID3-AlbumArt-Tag der ersten Audiodatei zurück.
    /// </summary>
    public sealed class LocalCoverLoader : ILocalCoverLoader
    {
        /// <inheritdoc/>
        public async Task<byte[]?> LoadAsync(string? episodeFolderPath, string? firstTrackPath)
        {
            // Schritt 1: cover.jpg direkt im Episodenordner – schnellster und bevorzugter Weg.
            // Viele Hörspiel-Rips enthalten ein standardisiertes cover.jpg im Ordner.
            if (!string.IsNullOrEmpty(episodeFolderPath))
            {
                string coverPath = Path.Combine(episodeFolderPath, Core.CoverConstants.CoverFileName);

                if (File.Exists(coverPath))
                {
                    try
                    {
                        return await File.ReadAllBytesAsync(coverPath).ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                        // Leserechte fehlen oder Datei ist korrupt – ID3-Fallback versuchen
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Keine Leserechte – ID3-Fallback versuchen
                    }
                }
            }

            // Schritt 2: AlbumArt aus den ID3-Tags der ersten Audiodatei lesen.
            // TagLib# ist synchron – Task.Run verhindert, dass der UI-Thread blockiert.
            if (!string.IsNullOrEmpty(firstTrackPath) && File.Exists(firstTrackPath))
            {
                // Lokale Variable, damit der Compiler die Nicht-Null-Garantie in der Lambda kennt
                string trackPath = firstTrackPath;
                return await Task.Run(() => ReadCoverFromTags(trackPath)).ConfigureAwait(false);
            }

            return null;
        }

        /// <summary>
        /// Liest das erste eingebettete Bild aus den ID3-Tags der Audiodatei.
        /// TagLib# wirft bei defekten Dateien Exceptions – alle werden still ignoriert.
        /// </summary>
        /// <param name="trackPath">Absoluter Pfad zur Audiodatei.</param>
        /// <returns>Rohe Bilddaten des ersten Tags oder <see langword="null"/>.</returns>
        private static byte[]? ReadCoverFromTags(string trackPath)
        {
            try
            {
                using TagLib.File tagFile = TagLib.File.Create(trackPath);

                // Pictures[] enthält alle eingebetteten Bilder – wir nehmen das erste (AlbumArt-Konvention)
                if (tagFile.Tag.Pictures is { Length: > 0 })
                {
                    return tagFile.Tag.Pictures[0].Data.Data;
                }
            }
            catch (TagLib.CorruptFileException)
            {
                // Defekte Tag-Struktur – kein Cover extrahierbar
            }
            catch (TagLib.UnsupportedFormatException)
            {
                // Unbekanntes Dateiformat – TagLib# kann die Datei nicht lesen
            }
            catch (IOException)
            {
                // Datei nicht lesbar – kein Absturz
            }

            return null;
        }
    }
}

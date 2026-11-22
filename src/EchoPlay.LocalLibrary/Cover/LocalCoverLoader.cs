using EchoPlay.Core.Logging;
using EchoPlay.Logger.Abstractions;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Lädt Cover-Bilder aus dem lokalen Dateisystem.
    /// Prüft zuerst auf eine <c>cover.jpg</c>-Datei im Episodenordner,
    /// fällt dann auf den ID3-AlbumArt-Tag der ersten Audiodatei zurück.
    /// </summary>
    public sealed class LocalCoverLoader : ILocalCoverLoader
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Richtet den Lader ein.
        /// </summary>
        /// <param name="loggerFactory">Fabrik für den Protokollkanal; nicht lesbare Dateien werden als Warnung vermerkt.</param>
        public LocalCoverLoader(ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(nameof(LocalCoverLoader));
        }

        /// <inheritdoc/>
        public async Task<byte[]?> LoadAsync(string? episodeFolderPath, string? firstTrackPath)
        {
            // Schritt 1: cover.jpg direkt im Episodenordner als schnellster und bevorzugter Weg.
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
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Danach kommt der ID3-Tag als zweite Quelle; vermerkt wird es trotzdem,
                        // sonst sucht niemand, warum die cover.jpg nicht erscheint.
                        _logger.Warning("cover.jpg nicht lesbar, nehme ID3-Tag: {Path} – {Reason}", PathRedactor.Redact(coverPath), ex.Message);
                    }
                }
            }

            // Schritt 2: AlbumArt aus den ID3-Tags der ersten Audiodatei lesen.
            // TagLib# ist synchron, Task.Run verhindert, dass der UI-Thread blockiert.
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
        /// TagLib# wirft bei defekten Dateien Ausnahmen; sie gelten als „kein Cover" und werden als Warnung vermerkt.
        /// </summary>
        /// <param name="trackPath">Absoluter Pfad zur Audiodatei.</param>
        /// <returns>Rohe Bilddaten des ersten Tags oder <see langword="null"/>.</returns>
        private byte[]? ReadCoverFromTags(string trackPath)
        {
            try
            {
                using TagLib.File tagFile = TagLib.File.Create(trackPath);

                // Pictures[] enthält alle eingebetteten Bilder; nach der AlbumArt-Konvention ist das erste das Cover.
                if (tagFile.Tag.Pictures is { Length: > 0 })
                {
                    return tagFile.Tag.Pictures[0].Data.Data;
                }
            }
            catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or UnauthorizedAccessException)
            {
                _logger.Warning("Cover aus dem ID3-Tag nicht lesbar: {Path} – {Reason}", PathRedactor.Redact(trackPath), ex.Message);
            }

            return null;
        }
    }
}

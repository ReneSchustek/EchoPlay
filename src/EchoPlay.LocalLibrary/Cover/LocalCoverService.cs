using EchoPlay.Core.Http;
using EchoPlay.Core.Logging;
using EchoPlay.Logger.Abstractions;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Ermittelt das Serien-Logo (Erkennungsbild der Serie) aus lokalen Dateien
    /// oder lädt es per URL herunter. ID3-Tags aus Episoden-Tracks werden
    /// ignoriert, denn sie enthalten Episoden-Artwork, nicht das Serien-Logo.
    /// Ein heruntergeladenes Cover wird als <c>cover.jpg</c> im Serienordner abgelegt.
    /// </summary>
    public sealed class LocalCoverService : ILocalCoverService
    {
        private readonly CoverService _coverService;
        private readonly ILogger _logger;

        // Dateinamen im Serienordner, die direkt das Serien-Logo enthalten; der erste vorhandene
        // gewinnt. Groß- und Kleinschreibung spielt unter Windows keine Rolle.
        private static readonly string[] CoverFileNames =
        [
            Core.CoverConstants.CoverFileName,
            "cover.jpeg",
            "folder.jpg",
            "folder.jpeg",
            "album.jpg"
        ];

        // Vorschaubilder, die der Windows Media Player selbst anlegt. Sie kommen zuletzt, weil
        // sie oft klein oder beschnitten sind.
        private const string AlbumArtLargePattern = "AlbumArt_*_Large.jpg";

        /// <summary>
        /// Initialisiert den Service mit dem HTTP-basierten Downloader.
        /// </summary>
        /// <param name="coverService">Downloader für Online-Coverbilder.</param>
        /// <param name="loggerFactory">Fabrik für den Protokollkanal; Lese- und Ladefehler werden als Warnung vermerkt.</param>
        public LocalCoverService(CoverService coverService, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            ArgumentNullException.ThrowIfNull(coverService);
            _coverService = coverService;
            _logger = loggerFactory.CreateLogger(nameof(LocalCoverService));
        }

        /// <inheritdoc/>
        public async Task<byte[]?> ResolveAsync(string seriesFolder, Uri? coverImageUrl, CancellationToken cancellationToken = default)
        {
            // Schritt 1: Cover-Unterordner
            // Manche Serien haben einen "Cover"-Unterordner mit front.jpg / cover.jpg.
            // Rückseiten ("back") bleiben außen vor, gesucht ist das Erkennungsbild.
            byte[]? coverSubfolder = TryReadCoverSubfolder(seriesFolder);

            if (coverSubfolder is not null)
            {
                return coverSubfolder;
            }

            // Schritt 2: Cover-Datei im Serienordner
            byte[]? localFile = TryReadLocalCoverFile(seriesFolder);

            if (localFile is not null)
            {
                return localFile;
            }

            // Schritt 3: Download, nur mit Adresse, etwa dem Spotify- oder Apple-Music-Coverbild.
            if (coverImageUrl is not null)
            {
                try
                {
                    byte[] downloaded = await _coverService.DownloadAsync(coverImageUrl, cancellationToken).ConfigureAwait(false);
                    await CoverService.SaveToDirectoryAsync(seriesFolder, downloaded).ConfigureAwait(false);
                    return downloaded;
                }
                catch (Exception ex) when (TransientRequestError.IsTransient(ex, cancellationToken) || ex is IOException or UnauthorizedAccessException)
                {
                    // Eine Zeitüberschreitung meldet sich als TaskCanceledException und zählt hier
                    // mit; nur ein Abbruch des Aufrufers geht nach oben.
                    // Kein Cover ist kein Abbruchgrund; die Kachel zeigt den Platzhalter.
                    _logger.Warning("Serien-Cover nicht geladen oder nicht gespeichert: {Folder} – {Reason}", PathRedactor.Redact(seriesFolder), ex.Message);
                }
            }

            return null;
        }

        /// <summary>
        /// Sucht im <c>Cover</c>-Unterordner nach einem Front-Cover.
        /// Dateien mit „back" im Namen werden übersprungen, denn sie zeigen die Rückseite.
        /// Dateien mit „front" im Namen haben Vorrang vor generischen Dateinamen.
        /// </summary>
        /// <returns>Rohe Bilddaten oder <see langword="null"/> wenn kein Unterordner oder kein Cover gefunden.</returns>
        private byte[]? TryReadCoverSubfolder(string seriesFolder)
        {
            string coverDir = Path.Combine(seriesFolder, "Cover");

            if (!Directory.Exists(coverDir))
            {
                return null;
            }

            string[] imageFiles;

            try
            {
                imageFiles = Directory.GetFiles(coverDir, "*.jp*g", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning("Cover-Unterordner nicht lesbar: {Folder} – {Reason}", PathRedactor.Redact(coverDir), ex.Message);
                return null;
            }

            return TryReadFirst(SelectFrontCoverCandidates(imageFiles));
        }

        /// <summary>
        /// Wählt aus den Bilddateien die Vorderseiten in Namensreihenfolge.
        /// </summary>
        private static IEnumerable<string> SelectFrontCoverCandidates(string[] imageFiles)
        {
            string[] candidates = imageFiles
                .Where(f => !Path.GetFileNameWithoutExtension(f)
                    .Contains("back", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // Dateien mit "front" im Namen haben Vorrang (z.B. "front.jpg", "cover_front.jpg")
            string[] frontFiles = candidates
                .Where(f => Path.GetFileNameWithoutExtension(f)
                    .Contains("front", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return (frontFiles.Length > 0 ? frontFiles : candidates).Order(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Liest die erste gefundene Cover-Datei direkt aus dem Serienordner.
        /// Prüft bekannte Dateinamen in absteigender Priorität, danach Windows-Media-Player-Thumbnails.
        /// </summary>
        private byte[]? TryReadLocalCoverFile(string seriesFolder)
        {
            byte[]? named = TryReadFirst(CoverFileNames
                .Select(fileName => Path.Combine(seriesFolder, fileName))
                .Where(File.Exists));
            if (named is not null)
            {
                return named;
            }

            try
            {
                string[] wmpFiles = Directory.GetFiles(seriesFolder, AlbumArtLargePattern);
                byte[]? thumbnail = TryReadFirst(wmpFiles.Order(StringComparer.OrdinalIgnoreCase));
                if (thumbnail is not null)
                {
                    return thumbnail;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning("Serienordner nicht lesbar beim Suchen des Covers: {Folder} – {Reason}", PathRedactor.Redact(seriesFolder), ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Liest die erste der Dateien, die sich lesen lässt.
        /// </summary>
        private byte[]? TryReadFirst(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                byte[]? bytes = TryReadBytes(path);
                if (bytes is not null)
                {
                    return bytes;
                }
            }

            return null;
        }

        /// <summary>
        /// Liest den Inhalt einer Datei; liefert <c>null</c> bei IO- oder Zugriffsfehlern,
        /// damit der Aufrufer die nächste Kandidaten-Datei prüfen kann.
        /// </summary>
        /// <param name="path">Der vollständige Dateipfad.</param>
        /// <returns>Die Bytes der Datei oder <c>null</c>, wenn sie nicht lesbar ist.</returns>
        private byte[]? TryReadBytes(string path)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Der Aufrufer prüft danach die nächste Kandidaten-Datei.
                _logger.Warning("Cover-Datei nicht lesbar: {Path} – {Reason}", PathRedactor.Redact(path), ex.Message);
                return null;
            }
        }
    }
}

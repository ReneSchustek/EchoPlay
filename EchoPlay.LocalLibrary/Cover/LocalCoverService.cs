using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Implementierung von <see cref="ILocalCoverService"/>.
    /// Ermittelt das Serien-Logo (Erkennungsbild der Serie) aus lokalen Dateien
    /// oder lädt es per URL herunter. ID3-Tags aus Episoden-Tracks werden bewusst
    /// ignoriert – sie enthalten Episoden-Artwork, nicht das Serien-Logo.
    /// Gefundene Cover werden als <c>cover.jpg</c> im Serienordner persistiert.
    /// </summary>
    public sealed class LocalCoverService : ILocalCoverService
    {
        private readonly CoverService _coverService;

        // Dateinamen im Serienordner, die direkt das Serien-Logo enthalten.
        // Reihenfolge = Priorität: cover.jpg zuerst, Systemdateien zuletzt.
        private static readonly string[] CoverFileNames =
        [
            Core.CoverConstants.CoverFileName,
            "cover.jpeg",
            "folder.jpg",
            "folder.jpeg",
            "Folder.jpg",
            "album.jpg"
        ];

        // Bekannte Windows-Media-Player-Thumbnails – werden nur als letzter Fallback genutzt,
        // da sie automatisch erzeugt und oft klein oder beschnitten sind.
        private const string AlbumArtLargePattern = "AlbumArt_*_Large.jpg";

        /// <summary>
        /// Initialisiert den Service mit dem HTTP-basierten Downloader.
        /// </summary>
        /// <param name="coverService">Downloader für Online-Coverbilder.</param>
        public LocalCoverService(CoverService coverService)
        {
            _coverService = coverService;
        }

        /// <inheritdoc/>
        public async Task<byte[]?> ResolveAsync(string seriesFolder, string? coverImageUrl)
        {
            // ── Schritt 1: Cover-Unterordner ──────────────────────────────────────
            // Manche Serien haben einen "Cover"-Unterordner mit front.jpg / cover.jpg.
            // Rückseiten ("back") werden ignoriert – wir wollen das Erkennungsbild.
            byte[]? coverSubfolder = TryReadCoverSubfolder(seriesFolder);

            if (coverSubfolder is not null)
            {
                return coverSubfolder;
            }

            // ── Schritt 2: Lokale Cover-Datei im Serienordner ─────────────────────
            // Schnellster Weg – kein Netzwerk, kein ID3-Parsing
            byte[]? localFile = TryReadLocalCoverFile(seriesFolder);

            if (localFile is not null)
            {
                return localFile;
            }

            // ── Schritt 3: Online-Download ────────────────────────────────────────
            // Nur wenn eine URL konfiguriert ist (z.B. Spotify- oder Apple-Music-Coverbild).
            // ID3-Cover aus Episoden-Tracks werden nicht verwendet – sie enthalten
            // Episoden-Artwork, nicht das Serien-Logo.
            if (!string.IsNullOrWhiteSpace(coverImageUrl))
            {
                try
                {
                    byte[] downloaded = await _coverService.DownloadAsync(coverImageUrl).ConfigureAwait(false);
                    await CoverService.SaveToDirectoryAsync(seriesFolder, downloaded).ConfigureAwait(false);
                    return downloaded;
                }
                catch (HttpRequestException)
                {
                    // Netzwerkfehler ignorieren – kein Cover ist kein Abbruchgrund
                }
                catch (IOException)
                {
                    // Dateisystem-Fehler beim Speichern ignorieren
                }
            }

            return null;
        }

        /// <summary>
        /// Sucht im <c>Cover</c>-Unterordner nach einem Front-Cover.
        /// Dateien mit „back" im Namen werden übersprungen – sie zeigen die Rückseite.
        /// Dateien mit „front" im Namen haben Vorrang vor generischen Dateinamen.
        /// </summary>
        /// <returns>Rohe Bilddaten oder <see langword="null"/> wenn kein Unterordner oder kein Cover gefunden.</returns>
        private static byte[]? TryReadCoverSubfolder(string seriesFolder)
        {
            string coverDir = Path.Combine(seriesFolder, "Cover");

            if (!Directory.Exists(coverDir))
            {
                return null;
            }

            // Alle Bilddateien im Cover-Unterordner einsammeln
            string[] imageFiles;

            try
            {
                imageFiles = Directory.GetFiles(coverDir, "*.jp*g", SearchOption.TopDirectoryOnly);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            if (imageFiles.Length == 0)
            {
                return null;
            }

            // Rückseiten-Dateien ausfiltern – "back" im Dateinamen (Groß-/Kleinschreibung egal)
            string[] candidates = imageFiles
                .Where(f => !Path.GetFileNameWithoutExtension(f)
                    .Contains("back", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // Dateien mit "front" im Namen haben Vorrang (z.B. "front.jpg", "cover_front.jpg")
            string[] frontFiles = candidates
                .Where(f => Path.GetFileNameWithoutExtension(f)
                    .Contains("front", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            string[] orderedCandidates = frontFiles.Length > 0
                ? frontFiles
                : candidates;

            foreach (string candidate in orderedCandidates.OrderBy(f => f))
            {
                try
                {
                    return File.ReadAllBytes(candidate);
                }
                catch (IOException)
                {
                    // Datei lesbar aber nicht zugänglich – nächste prüfen
                }
                catch (UnauthorizedAccessException)
                {
                    // Keine Leserechte – nächste prüfen
                }
            }

            return null;
        }

        /// <summary>
        /// Liest die erste gefundene Cover-Datei direkt aus dem Serienordner.
        /// Prüft bekannte Dateinamen in absteigender Priorität, danach Windows-Media-Player-Thumbnails.
        /// </summary>
        private static byte[]? TryReadLocalCoverFile(string seriesFolder)
        {
            foreach (string fileName in CoverFileNames)
            {
                string path = Path.Combine(seriesFolder, fileName);

                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    return File.ReadAllBytes(path);
                }
                catch (IOException)
                {
                    // Datei vorhanden aber nicht lesbar – nächste Variante prüfen
                }
                catch (UnauthorizedAccessException)
                {
                    // Keine Leserechte – nächste Variante prüfen
                }
            }

            // Windows-Media-Player erzeugt automatisch „AlbumArt_*_Large.jpg"-Thumbnails.
            // Letzter Fallback: erster Treffer aus diesem Muster.
            try
            {
                string[] wmpFiles = Directory.GetFiles(seriesFolder, AlbumArtLargePattern);

                foreach (string wmpFile in wmpFiles.OrderBy(f => f))
                {
                    try
                    {
                        return File.ReadAllBytes(wmpFile);
                    }
                    catch (IOException)
                    {
                        // Nicht lesbar – nächste prüfen
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Keine Leserechte – nächste prüfen
                    }
                }
            }
            catch (IOException)
            {
                // Directory.GetFiles kann bei nicht vorhandenen Ordnern werfen
            }
            catch (UnauthorizedAccessException)
            {
                // Keine Leserechte auf den Ordner
            }

            return null;
        }
    }
}

using System.Collections.Generic;

namespace EchoPlay.LocalLibrary.Parsing
{
    /// <summary>
    /// Kapselt die Parse-Kaskade für flache Serien-Dateinamen: zuerst Kassetten-Rip
    /// (<see cref="CassetteRipParser"/>), dann die konfigurierte Parser-Kette, sonst der
    /// Fallback (Dateiname als Titel, keine Nummer). Scanner und Umstrukturierungs-Service
    /// teilen so dieselbe Reihenfolge und Regel.
    /// </summary>
    public static class FileNameEpisodeParser
    {
        /// <summary>
        /// Ermittelt Episodennummer und Titel aus einem Dateinamen.
        /// </summary>
        /// <param name="fileNameWithoutExt">Der Dateiname ohne Erweiterung.</param>
        /// <param name="fileParsers">
        /// Die zu probierende Parser-Kette, z.B. aus
        /// <see cref="EpisodeFolderParser.CreateFileNameParserChain"/>.
        /// </param>
        /// <returns>Die erkannte Nummer (oder <c>null</c>) und der Titel (Fallback: Dateiname).</returns>
        public static (int? Number, string Title) Parse(string fileNameWithoutExt, IReadOnlyList<EpisodeFolderParser> fileParsers)
        {
            ArgumentNullException.ThrowIfNull(fileNameWithoutExt);
            ArgumentNullException.ThrowIfNull(fileParsers);

            // Kassetten-Rips zuerst – muss vor den generischen Parsern laufen, damit
            // "{number} {title}" nicht fälschlich "01" als Nummer und "a Titel" als Titel liest.
            if (CassetteRipParser.TryParse(fileNameWithoutExt, out int? cassetteNumber, out string? cassetteTitle))
            {
                return (cassetteNumber, cassetteTitle ?? fileNameWithoutExt);
            }

            foreach (EpisodeFolderParser parser in fileParsers)
            {
                if (parser.TryParse(fileNameWithoutExt, out int? number, out string? title))
                {
                    return (number, title ?? fileNameWithoutExt);
                }
            }

            // Kein Muster erkannt – Dateiname als Titel, keine Nummer.
            return (null, fileNameWithoutExt);
        }
    }
}

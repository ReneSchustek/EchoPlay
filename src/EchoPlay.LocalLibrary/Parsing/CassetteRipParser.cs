using System.Text.RegularExpressions;

namespace EchoPlay.LocalLibrary.Parsing
{
    /// <summary>
    /// Erkennt Kassetten-Rips mit a/b-Seiten im Dateinamen und leitet daraus eine
    /// fortlaufende Episodennummer sowie den Titel ab. Wird sowohl vom Scanner als
    /// auch vom Umstrukturierungs-Service genutzt, damit beide dieselbe Logik teilen.
    /// Unterstützte Formate:
    /// - <c>01a Titel</c> (Pumuckl)
    /// - <c>Serie - 01a - Titel</c> (Black Beauty)
    /// - <c>W03a Titel</c> (Sonderfolgen mit Buchstaben-Präfix)
    /// - <c>11a</c> (ohne Titel)
    /// </summary>
    public static class CassetteRipParser
    {
        // Gruppen: prefix (optional), number, side (a/b), title (optional).
        private static readonly Regex CassettePattern = new(
            @"^(?:.*\s-\s)?(?<prefix>[A-Za-z]*)(?<number>\d{1,3})(?<side>[abAB])(?:\s-\s|\s)(?<title>.+)$|" +
            @"^(?<prefix>[A-Za-z]*)(?<number>\d{1,3})(?<side>[abAB])$",
            RegexOptions.Compiled);

        /// <summary>
        /// Versucht, einen Dateinamen als Kassetten-Rip zu interpretieren.
        /// </summary>
        /// <param name="fileNameWithoutExt">Der Dateiname ohne Erweiterung.</param>
        /// <param name="number">
        /// Bei Erfolg die fortlaufende Episodennummer (Seite a = ungerade, b = gerade:
        /// 01a → 1, 01b → 2, 02a → 3 …), sonst <c>null</c>.
        /// </param>
        /// <param name="title">Bei Erfolg der Titel (oder der Dateiname, falls kein Titel im Muster), sonst <c>null</c>.</param>
        /// <returns><c>true</c>, wenn der Dateiname einem Kassetten-Muster entspricht.</returns>
        public static bool TryParse(string fileNameWithoutExt, out int? number, out string? title)
        {
            ArgumentNullException.ThrowIfNull(fileNameWithoutExt);

            number = null;
            title = null;

            Match cassetteMatch = CassettePattern.Match(fileNameWithoutExt);
            if (!cassetteMatch.Success)
            {
                return false;
            }

            string cassetteNumberStr = cassetteMatch.Groups["number"].Value;
            char side = char.ToLowerInvariant(cassetteMatch.Groups["side"].Value[0]);

            if (!int.TryParse(cassetteNumberStr, out int cassetteNumber))
            {
                return false;
            }

            // Fortlaufende Nummer: Seite a = ungerade, Seite b = gerade.
            // Kassette 01a → 1, 01b → 2, 02a → 3, 02b → 4 usw.
            number = (cassetteNumber * 2) - (side == 'a' ? 1 : 0);
            title = cassetteMatch.Groups["title"].Success && cassetteMatch.Groups["title"].Value.Length > 0
                ? cassetteMatch.Groups["title"].Value.Trim()
                : fileNameWithoutExt;

            return true;
        }
    }
}

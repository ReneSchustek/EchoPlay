using System.Globalization;
using System.Text.RegularExpressions;

namespace EchoPlay.Core.Parsing
{
    /// <summary>
    /// Ermittelt den Anzeigenamen einer Audiospur für Tracklisten.
    /// Bevorzugt wird der Titel aus der Kennzeichnung der Datei; fehlt er oder ist er
    /// nur ein Platzhalter des Rippers, dient der Dateiname als Rückfallebene.
    /// </summary>
    public static partial class TrackDisplayTitle
    {
        /// <summary>
        /// Für Anzeigen ohne eigene Nummernspalte — dort bleibt eine führende Zahl im
        /// Dateinamen stehen, weil sie sonst nirgends mehr auftaucht.
        /// </summary>
        public const int NoNumberShown = -1;

        /// <summary>
        /// Wählt den Anzeigenamen: den Titel aus der Kennzeichnung, sonst den Dateinamen.
        /// </summary>
        /// <param name="tagTitle">Titel aus der Kennzeichnung der Datei, ggf. leer.</param>
        /// <param name="filePath">Absoluter Pfad zur Audiodatei.</param>
        /// <param name="trackNumber">Nummer, die in der Liste bereits eigenständig angezeigt wird.</param>
        /// <returns>Der Anzeigename, nie leer.</returns>
        public static string Choose(string? tagTitle, string filePath, int trackNumber)
        {
            return IsMeaningful(tagTitle) ? tagTitle!.Trim() : FromFilePath(filePath, trackNumber);
        }

        /// <summary>
        /// Räumt den Dateinamen für die Anzeige auf: ohne Endung, ohne die führende Nummer,
        /// die daneben ohnehin in eigener Spalte steht, und mit großem Anfangsbuchstaben.
        /// </summary>
        /// <param name="filePath">Absoluter Pfad zur Audiodatei.</param>
        /// <param name="trackNumber">
        /// Nummer der Spur. Nur eine führende Zahl, die genau dieser Nummer entspricht, wird
        /// entfernt — sonst gingen Titel wie „116 Klassenfahrt" verloren.
        /// </param>
        /// <returns>Der aufgeräumte Dateiname. Leer nur, wenn der Pfad selbst leer ist.</returns>
        public static string FromFilePath(string filePath, int trackNumber)
        {
            ArgumentNullException.ThrowIfNull(filePath);

            string name = Path.GetFileNameWithoutExtension(filePath);

            if (name.Length == 0)
            {
                return string.Empty;
            }

            return Capitalize(StripOwnTrackNumber(name, trackNumber));
        }

        /// <summary>
        /// Prüft, ob ein Titel aus der Kennzeichnung eine echte Auskunft ist.
        /// Leere Titel und Platzhalter wie „Track 01" gelten als nicht gepflegt — dort ist
        /// der Dateiname die bessere Auskunft.
        /// </summary>
        /// <param name="tagTitle">Titel aus der Kennzeichnung der Datei.</param>
        /// <returns><see langword="true"/>, wenn der Titel angezeigt werden soll.</returns>
        public static bool IsMeaningful(string? tagTitle)
        {
            if (string.IsNullOrWhiteSpace(tagTitle))
            {
                return false;
            }

            return !PlaceholderTitlePattern().IsMatch(tagTitle.Trim());
        }

        /// <summary>
        /// Entfernt eine führende Zahl samt Trennzeichen, wenn sie der übergebenen Spurnummer
        /// entspricht. Alles andere bleibt stehen.
        /// </summary>
        private static string StripOwnTrackNumber(string name, int trackNumber)
        {
            Match match = LeadingNumberPattern().Match(name);

            if (!match.Success)
            {
                return name;
            }

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int leadingNumber)
                || leadingNumber != trackNumber)
            {
                return name;
            }

            string stripped = name[match.Length..].Trim();

            // Heißt die Datei nur nach ihrer Nummer, bleibt sonst nichts übrig
            return stripped.Length > 0 ? stripped : name;
        }

        /// <summary>
        /// Setzt den ersten Buchstaben groß. Kassetten-Rips und manche Dateinamen beginnen
        /// klein (z.B. „01a spuk in der werkstatt").
        /// </summary>
        private static string Capitalize(string name)
        {
            return name.Length > 0 && char.IsLower(name[0])
                ? char.ToUpperInvariant(name[0]) + name[1..]
                : name;
        }

        /// <summary>Führende Zahl, gefolgt von mindestens einem Trennzeichen.</summary>
        [GeneratedRegex(@"^(\d{1,4})[\s._\-–—)]+", RegexOptions.None, matchTimeoutMilliseconds: 500)]
        private static partial Regex LeadingNumberPattern();

        /// <summary>Platzhalter-Titel, die Ripper vergeben, wenn nichts gepflegt wurde.</summary>
        [GeneratedRegex(
            @"^(track|title|titel|spur|audio\s*track|untitled|unknown|unbekannt|ohne\s*titel)[\s._\-]*\d*$",
            RegexOptions.IgnoreCase,
            matchTimeoutMilliseconds: 500)]
        private static partial Regex PlaceholderTitlePattern();
    }
}

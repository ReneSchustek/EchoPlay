using System.Text.RegularExpressions;

namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Stellt Methoden zur Textnormalisierung für Hörspiel-Name-Matching bereit.
    /// Die Normalisierung vereinheitlicht Umlaute, Sonderzeichen und Groß-/Kleinschreibung,
    /// um robuste Vergleiche unabhängig von Schreibvarianten zu ermöglichen.
    /// </summary>
    public static class HoerspielTextNormalizer
    {
        /// <summary>
        /// Normalisiert einen Text für den fachlichen Vergleich.
        /// Wandelt in Kleinbuchstaben, ersetzt Umlaute und entfernt Sonderzeichen.
        /// </summary>
        /// <param name="text">Der zu normalisierende Text.</param>
        /// <returns>Der normalisierte Text.</returns>
        public static string Normalize(string text)
        {
            string lower = text.ToLowerInvariant();

            // Umlaute und ß in ASCII-Äquivalente überführen
            string replaced = lower
                .Replace("ä", "ae")
                .Replace("ö", "oe")
                .Replace("ü", "ue")
                .Replace("ß", "ss");

            // Alles außer Buchstaben, Ziffern und Leerzeichen entfernen
            return Regex.Replace(replaced, @"[^a-z0-9\s]", "");
        }
    }
}

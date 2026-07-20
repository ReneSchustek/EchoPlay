using System.Diagnostics.CodeAnalysis;
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
        [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Fachliches Lowercase-Matching gegen ASCII-Regex [a-z0-9]; Uppercase-Variante würde die gesamte Scoring-Pipeline umkrempeln.")]
        public static string Normalize(string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            string lower = text.ToLowerInvariant();

            // Umlaute und ß in ASCII-Äquivalente überführen
            string replaced = lower
                .Replace("ä", "ae", StringComparison.Ordinal)
                .Replace("ö", "oe", StringComparison.Ordinal)
                .Replace("ü", "ue", StringComparison.Ordinal)
                .Replace("ß", "ss", StringComparison.Ordinal);

            // Alles außer Buchstaben, Ziffern und Leerzeichen entfernen
            return Regex.Replace(replaced, @"[^a-z0-9\s]", "");
        }
    }
}

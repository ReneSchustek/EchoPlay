using System.Text.RegularExpressions;

namespace EchoPlay.Core.Parsing
{
    /// <summary>
    /// Extrahiert Folgennummern aus Albumtiteln.
    /// Wird von Spotify- und AppleMusic-Mappern gemeinsam genutzt,
    /// damit die Extraktionslogik nur einmal existiert.
    /// </summary>
    public static partial class EpisodeNumberParser
    {
        /// <summary>
        /// Extrahiert die erste Zahl aus einem Albumtitel als Folgennummer.
        /// Nur Zahlen zwischen 1 und 999 werden als gültige Folgennummern akzeptiert –
        /// größere Werte sind typischerweise Jahreszahlen oder Katalognummern.
        /// </summary>
        /// <param name="albumTitle">Der Albumtitel, z.B. "Folge 42 – Der blaue Tod".</param>
        /// <returns>Die extrahierte Folgennummer oder null wenn keine gültige Zahl gefunden.</returns>
        public static int? Extract(string albumTitle)
        {
            ArgumentNullException.ThrowIfNull(albumTitle);

            Match match = NumberPattern().Match(albumTitle);

            if (match.Success && int.TryParse(match.Value, out int number))
            {
                return number is > 0 and < 1000 ? number : null;
            }

            return null;
        }

        [GeneratedRegex(@"\d+", RegexOptions.Compiled)]
        private static partial Regex NumberPattern();
    }
}

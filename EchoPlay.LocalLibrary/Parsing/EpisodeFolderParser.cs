using System.Text.RegularExpressions;

namespace EchoPlay.LocalLibrary.Parsing
{
    /// <summary>
    /// Parst Episodenordnernamen anhand eines konfigurierbaren Musters.
    /// Unterstützte Platzhalter: <c>{number}</c>, <c>{number:000}</c>, <c>{title}</c>, <c>{*}</c>.
    /// <c>{*}</c> steht für einen beliebigen nicht-leeren Text, der im Ergebnis nicht erfasst wird –
    /// nützlich für Muster wie <c>"{*} - {number:000} - {title}"</c> bei Serien mit Präfix.
    /// </summary>
    public sealed class EpisodeFolderParser
    {
        private readonly Regex _pattern;
        private readonly bool _hasNumber;
        private readonly bool _hasTitle;

        /// <summary>
        /// Initialisiert den Parser mit dem angegebenen Ordnermuster.
        /// Das Muster wird einmalig in einen regulären Ausdruck übersetzt.
        /// </summary>
        /// <param name="folderPattern">
        /// Das Muster, z.B. <c>"{number:000} - {title}"</c> oder <c>"{*} - {number:000} - {title}"</c>.
        /// </param>
        public EpisodeFolderParser(string folderPattern)
        {
            ArgumentNullException.ThrowIfNull(folderPattern);

            string regexPattern = BuildRegex(folderPattern, out _hasNumber, out _hasTitle);
            _pattern = new(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        /// <summary>
        /// Versucht, einen Ordnernamen anhand des Musters zu parsen.
        /// </summary>
        /// <param name="folderName">Nur der Ordnername, kein vollständiger Pfad.</param>
        /// <param name="number">Die geparste Episodennummer oder null.</param>
        /// <param name="title">Der geparste Episodentitel oder null.</param>
        /// <returns><c>true</c> wenn das Muster auf den Ordnernamen passt, sonst <c>false</c>.</returns>
        public bool TryParse(string folderName, out int? number, out string? title)
        {
            number = null;
            title = null;

            Match match = _pattern.Match(folderName);

            if (!match.Success)
            {
                return false;
            }

            if (_hasNumber)
            {
                Group numberGroup = match.Groups["number"];
                if (numberGroup.Success && int.TryParse(numberGroup.Value, out int parsed))
                {
                    number = parsed;
                }
            }

            if (_hasTitle)
            {
                Group titleGroup = match.Groups["title"];
                if (titleGroup.Success)
                {
                    title = titleGroup.Value.Trim();
                }
            }

            return true;
        }

        /// <summary>
        /// Entfernt eine führende laufende Nummer aus einem Ordner- oder Dateinamen.
        /// Nützlich für Bibliotheken, in denen Ordner ein lokales Zählpräfix tragen,
        /// das nicht Teil des Hörspiel-Titels ist (z.B. "001 - 116 Klassenfahrt" → "116 Klassenfahrt").
        /// Muster: 1–4 Stellen, gefolgt von mindestens einem Leerzeichen, Bindestrich oder Punkt.
        /// </summary>
        /// <param name="name">Der zu bereinigende Ordner- oder Dateiname.</param>
        /// <returns>
        /// Name ohne führendes Zahlenpräfix. Unverändert, wenn kein Präfix erkannt wurde.
        /// </returns>
        public static string StripLeadingSequenceNumber(string name)
        {
            return Regex.Replace(name, @"^\d{1,4}[\s\-\.]+", string.Empty).Trim();
        }

        /// <summary>
        /// Wandelt das Ordnermuster in einen regulären Ausdruck um.
        /// Alle Zeichen außerhalb der Platzhalter werden escaped.
        /// <c>{*}</c> wird als nicht-erfassende Gruppe <c>(?:.+?)</c> übersetzt.
        /// </summary>
        private static string BuildRegex(string folderPattern, out bool hasNumber, out bool hasTitle)
        {
            hasNumber = false;
            hasTitle = false;

            // Platzhalter in geordneter Reihenfolge ersetzen, damit die Flags korrekt gesetzt werden
            string working = folderPattern;

            if (working.Contains("{number:000}") || working.Contains("{number}"))
            {
                hasNumber = true;
            }

            if (working.Contains("{title}"))
            {
                hasTitle = true;
            }

            // Platzhalter der Reihe nach ersetzen – {*} muss vor {number} stehen, damit keine
            // Teilmuster-Kollisionen auftreten. Regex.Escape stellt sicher, dass Sonderzeichen im
            // literalen Mustertext (z.B. " - ") korrekt als solche behandelt werden.
            string escaped = Regex.Escape(working)
                .Replace(Regex.Escape("{*}"), "(?:.+?)")
                .Replace(Regex.Escape("{number:000}"), "(?<number>\\d+)")
                .Replace(Regex.Escape("{number}"), "(?<number>\\d+)")
                .Replace(Regex.Escape("{title}"), "(?<title>.+)");

            return $"^{escaped}$";
        }
    }
}

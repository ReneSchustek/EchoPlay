using System.Globalization;
using System.Text.RegularExpressions;

namespace EchoPlay.LocalLibrary.Parsing
{
    /// <summary>
    /// Parst Episodenordnernamen anhand eines konfigurierbaren Musters.
    /// Unterstützte Platzhalter: <c>{number}</c>, <c>{number:000}</c>, <c>{title}</c>, <c>{*}</c>.
    /// <c>{*}</c> steht für einen beliebigen nicht-leeren Text, der im Ergebnis nicht erfasst wird,
    /// nützlich für Muster wie <c>"{*} - {number:000} - {title}"</c> bei Serien mit Präfix.
    /// </summary>
    public sealed class EpisodeFolderParser
    {
        // Ein Muster aus den Einstellungen mit mehreren {*} und {title} hintereinander kann bei
        // einem langen Namen sehr oft zurückverfolgen. Ein Ordnername ist in Mikrosekunden
        // geprüft; 500 ms sind die Grenze, ab der es kein Treffer mehr ist.
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

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
            _pattern = new(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
        }

        /// <summary>
        /// Erstellt die Standard-Kaskade an Parsern für flache Serien-Dateinamen.
        /// Das konfigurierte Muster wird zuerst probiert, danach die üblichen Fallback-Muster.
        /// Scanner und Umstrukturierungs-Service nutzen dieselbe Reihenfolge.
        /// </summary>
        /// <param name="configuredParser">Der konfigurierte Parser, der zuerst greift.</param>
        /// <returns>Die Parser-Kaskade in Prüfreihenfolge.</returns>
        public static EpisodeFolderParser[] CreateFileNameParserChain(EpisodeFolderParser configuredParser)
        {
            ArgumentNullException.ThrowIfNull(configuredParser);

            return
            [
                configuredParser,                        // Konfiguriertes Muster zuerst
                new("{*} - {number:000} - {title}"),     // "Serie - 001 - Titel"
                new("{number:000} - {title}"),           // "001 - Titel"
                new("{number} {title}"),                 // "01 Titel" (einfach)
                new("{*}_FOLGE_{number}_{title}"),       // "SERIE_FOLGE_01_TITEL"
            ];
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
            ArgumentNullException.ThrowIfNull(folderName);

            number = null;
            title = null;

            Match? match = TryMatch(folderName);
            if (match is null || !match.Success)
            {
                return false;
            }

            ReadGroups(match, out number, out title);
            return true;
        }

        private Match? TryMatch(string folderName)
        {
            try
            {
                return _pattern.Match(folderName);
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathologische Eingabe: Der Parser liefert „kein Treffer" statt die App zu blockieren.
                return null;
            }
        }

        /// <summary>
        /// Liest Nummer und Titel aus dem Treffer, soweit das Muster sie enthält.
        /// </summary>
        private void ReadGroups(Match match, out int? number, out string? title)
        {
            number = null;
            title = null;

            if (_hasNumber)
            {
                Group numberGroup = match.Groups["number"];
                if (numberGroup.Success && int.TryParse(numberGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
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
        }

        /// <summary>
        /// Entfernt eine führende laufende Nummer aus einem Ordner- oder Dateinamen.
        /// Nützlich für Bibliotheken, in denen Ordner ein lokales Zählpräfix tragen,
        /// das nicht Teil des Hörspiel-Titels ist (etwa "001 - 116 Klassenfahrt" → "116 Klassenfahrt").
        /// Muster: 1–4 Stellen, gefolgt von mindestens einem Leerzeichen, Bindestrich oder Punkt.
        /// </summary>
        /// <param name="name">Der zu bereinigende Ordner- oder Dateiname.</param>
        /// <returns>
        /// Name ohne führendes Zahlenpräfix. Unverändert, wenn kein Präfix erkannt wurde.
        /// </returns>
        public static string StripLeadingSequenceNumber(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            try
            {
                return Regex.Replace(name, @"^\d{1,4}[\s\-\.]+", string.Empty, RegexOptions.None, RegexTimeout).Trim();
            }
            catch (RegexMatchTimeoutException)
            {
                return name.Trim();
            }
        }

        /// <summary>
        /// Wandelt das Ordnermuster in einen regulären Ausdruck um.
        /// Alle Zeichen außerhalb der Platzhalter gelten wörtlich.
        /// <c>{*}</c> wird als nicht-erfassende Gruppe <c>(?:.+?)</c> übersetzt.
        /// </summary>
        private static string BuildRegex(string folderPattern, out bool hasNumber, out bool hasTitle)
        {
            hasNumber = folderPattern.Contains("{number:000}", StringComparison.Ordinal)
                || folderPattern.Contains("{number}", StringComparison.Ordinal);
            hasTitle = folderPattern.Contains("{title}", StringComparison.Ordinal);

            // Regex.Escape maskiert den ganzen Text, auch die Platzhalter („{" wird zu „\{").
            // Ersetzt wird deshalb deren maskierte Form, sonst fände keiner einen Treffer.
            string escaped = Regex.Escape(folderPattern)
                .Replace(Regex.Escape("{*}"), "(?:.+?)", StringComparison.Ordinal)
                .Replace(Regex.Escape("{number:000}"), "(?<number>\\d+)", StringComparison.Ordinal)
                .Replace(Regex.Escape("{number}"), "(?<number>\\d+)", StringComparison.Ordinal)
                .Replace(Regex.Escape("{title}"), "(?<title>.+)", StringComparison.Ordinal);

            return $"^{escaped}$";
        }
    }
}

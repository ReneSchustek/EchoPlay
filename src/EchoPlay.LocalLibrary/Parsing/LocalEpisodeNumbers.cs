using System.Globalization;
using System.Text.RegularExpressions;
using EchoPlay.Core.Parsing;

namespace EchoPlay.LocalLibrary.Parsing
{
    /// <summary>
    /// Ermittelt aus Ordnernamen einer Serie die vorhandenen Folgennummern und die Lücken dazwischen.
    /// Gemeinsame Grundlage für die Fehlende-Folgen-Analyse und den Online-Abgleich. Läge
    /// dieselbe Logik doppelt vor, liefe sie auseinander.
    /// </summary>
    /// <remarks>
    /// Ein Muster, das für die ganze Serie gewinnt, reicht nicht. Drei Regeln kommen hinzu,
    /// jeweils aus gemessenen Fehlbefunden an einer 92-Serien-Sammlung abgeleitet:
    /// <list type="bullet">
    /// <item>Jeder Ordner wird gegen alle Muster geprüft. Sonst zählen Ordner, die zufällig
    /// anders benannt sind, als fehlend, bei „Sherlock Holmes – Die geheimen Fälle" 38 von 67.</item>
    /// <item>Ordner, die nur aus einer Zahl bestehen („001"), werden erkannt. Alle übrigen Muster
    /// verlangen einen Titelteil.</item>
    /// <item>Nur Zahlen von 1 bis 999 gelten als Folgennummer, dieselbe Regel wie in
    /// <see cref="EpisodeNumberParser"/>. „Stenkelfeld 2008" würde sonst als Folge 2008 gelesen und
    /// erzeugte 1999 erfundene Lücken.</item>
    /// </list>
    /// </remarks>
    public static partial class LocalEpisodeNumbers
    {
        // Reihenfolge zählt: das erste passende Muster gewinnt pro Ordner. Spezifische Muster
        // (mit Präfix oder Schlüsselwort) stehen vor den allgemeinen.
        private static readonly string[] PatternCandidates =
        [
            "Folge {number:000} - {title}",
            "{*} - {number:000} - {title}",
            "{*} - {number} - {title}",
            "{number:000} - {title}",
            "{number} - {title}",
            "{number:000}_{title}",
            "{number:000} {title}",
            "{title} - {number:000}",
            // Ordner ohne Titelteil wie „001"; kein anderes Muster deckt das ab.
            "{number:000}"
        ];

        private static readonly EpisodeFolderParser[] Parsers =
            [.. PatternCandidates.Select(p => new EpisodeFolderParser(p))];

        // Auffangnetz für Tippfehler in der Nummerierung: „203- Titel" (fehlendes Leerzeichen),
        // „203.Titel", „203_Titel". Ohne das Netz gälte eine vorhandene Folge als fehlend, nur
        // weil ein Leerzeichen im Ordnernamen fehlt.
        // (?!\d) verhindert, dass Jahreszahlen wie „1984 - …" als Folge 198 gelesen werden.
        [GeneratedRegex(@"^(\d{1,3})(?!\d)", RegexOptions.None, matchTimeoutMilliseconds: 500)]
        private static partial Regex LeadingNumber();

        // Dasselbe Netz für die Nummer zwischen Serienname und Titel. „Serie - 066 -Titel" mit
        // fehlendem Leerzeichen trifft keines der Muster; ohne dieses Netz stünde Folge 66 als
        // Lücke im Bericht, obwohl der Ordner vorliegt.
        [GeneratedRegex(@"\s-\s*(\d{1,3})(?!\d)\s*-", RegexOptions.None, matchTimeoutMilliseconds: 500)]
        private static partial Regex SeparatedNumber();

        // Toleranz für die einzige-Zahl-Regel: Eine allein stehende Nummer wird nur geglaubt,
        // wenn sie zur Größe der Sammlung passt. „Arsène Lupin – 813" bei 16 Ordnern ist ein
        // Romantitel; Folge 5 bei drei Ordnern ist plausibel.
        private const int SingleNumberTolerance = 10;

        /// <summary>
        /// Liest die Folgennummern aus den übergebenen Ordnernamen.
        /// </summary>
        /// <param name="folderNames">Ordnernamen einer Serie (nur der Name, kein Pfad).</param>
        /// <returns>Die erkannten Nummern, aufsteigend und ohne Duplikate.</returns>
        public static IReadOnlyList<int> Scan(IEnumerable<string> folderNames)
        {
            ArgumentNullException.ThrowIfNull(folderNames);

            int?[] perFolder = ExtractPerFolder([.. folderNames], preferred: null);
            return [.. new SortedSet<int>(perFolder.OfType<int>())];
        }

        /// <summary>
        /// Ordnet jedem Ordner einer Serie seine Folgennummer zu, in der Reihenfolge der Eingabe.
        /// </summary>
        /// <param name="folderNames">Ordnernamen einer Serie (nur der Name, kein Pfad), in Anzeigereihenfolge.</param>
        /// <param name="preferred">
        /// Vorrangige Erkennung, typischerweise das in den Einstellungen gewählte Muster. Liefert sie
        /// eine plausible Nummer, gilt diese; sonst greifen die allgemeinen Muster. Das Muster des
        /// Nutzers kennt seine Sammlung besser: „038 - 01 - Pumuckl und der blaue Klabauter“ ist
        /// Folge 38, die allgemeinen Muster läsen die 01.
        /// </param>
        /// <returns>
        /// Je Ordner die Nummer oder <see langword="null"/>. Ordner ohne Nummer in einer sonst
        /// nummerierten Serie sind Sonderfolgen (Adventskalender, Kinofilm) und bekommen keine,
        /// damit sie keiner regulären Folge die Nummer wegnehmen. Trägt kein einziger Ordner eine
        /// Nummer, ist die Reihenfolge die Nummerierung: dann wird ab 1 durchgezählt.
        /// </returns>
        public static IReadOnlyList<int?> Assign(IReadOnlyList<string> folderNames, Func<string, int?>? preferred)
        {
            ArgumentNullException.ThrowIfNull(folderNames);

            int?[] perFolder = ExtractPerFolder(folderNames, preferred);
            if (perFolder.Any(number => number.HasValue))
            {
                return perFolder;
            }

            return [.. Enumerable.Range(1, perFolder.Length).Select(index => (int?)index)];
        }

        /// <summary>
        /// Ob wenigstens ein Ordner der Serie eine Folgennummer im Namen trägt. Bei
        /// <see langword="false"/> stammen die Nummern aus <see cref="Assign"/> nur aus der
        /// Reihenfolge.
        /// </summary>
        /// <param name="folderNames">Ordnernamen einer Serie.</param>
        /// <param name="preferred">Vorrangige Erkennung wie bei <see cref="Assign"/>.</param>
        /// <returns><see langword="true"/>, wenn die Serie nummeriert ist.</returns>
        public static bool IsNumbered(IReadOnlyList<string> folderNames, Func<string, int?>? preferred)
        {
            ArgumentNullException.ThrowIfNull(folderNames);

            return ExtractPerFolder(folderNames, preferred).Any(number => number.HasValue);
        }

        /// <summary>
        /// Liest je Ordner die Nummer und verwirft eine allein stehende aus den allgemeinen
        /// Mustern, die nicht zur Sammlungsgröße passt.
        /// </summary>
        /// <remarks>
        /// Eine Nummer, die das vorrangige Muster erkannt hat, bleibt von dieser Prüfung
        /// ausgenommen: Der Nutzer hat das Muster für seine Ordner gewählt, ein einzelner
        /// Ordner „042" ist dann Folge 42 und kein Titelbestandteil.
        /// </remarks>
        private static int?[] ExtractPerFolder(IReadOnlyList<string> folderNames, Func<string, int?>? preferred)
        {
            int?[] numbers = new int?[folderNames.Count];
            bool anyFromPreferred = false;
            for (int i = 0; i < folderNames.Count; i++)
            {
                int? fromPreferred = preferred?.Invoke(folderNames[i]);
                if (IsPlausible(fromPreferred))
                {
                    numbers[i] = fromPreferred;
                    anyFromPreferred = true;
                }
                else
                {
                    numbers[i] = ExtractNumber(folderNames[i]);
                }
            }

            // Genau eine Nummer und diese passt nicht zur Sammlungsgröße: eher Titelbestandteil
            // als Nummerierung. Ohne zweite Nummer gibt es nichts, was sie stützt.
            int[] distinct = [.. numbers.OfType<int>().Distinct()];
            if (!anyFromPreferred && distinct.Length == 1 && distinct[0] > folderNames.Count + SingleNumberTolerance)
            {
                Array.Clear(numbers);
            }

            return numbers;
        }

        /// <summary>
        /// Liefert die fehlenden Nummern zwischen der kleinsten und der größten gefundenen Folge.
        /// </summary>
        /// <param name="numbers">Vorhandene Folgennummern, aufsteigend sortiert wie aus <see cref="Scan"/>.</param>
        /// <returns>Die fehlenden Nummern, aufsteigend.</returns>
        /// <remarks>
        /// Gezählt wird ab der kleinsten vorhandenen Nummer, nicht ab 1: Wer eine Serie erst ab Folge 50
        /// sammelt, hat keine 49 Lücken. Er hat 49 Folgen, die er nicht besitzen will.
        /// </remarks>
        public static IReadOnlyList<int> FindGaps(IReadOnlyList<int> numbers)
        {
            ArgumentNullException.ThrowIfNull(numbers);

            if (numbers.Count == 0)
            {
                return [];
            }

            HashSet<int> present = [.. numbers];
            int min = numbers[0];
            int max = numbers[^1];

            List<int> gaps = [];
            for (int i = min; i <= max; i++)
            {
                if (!present.Contains(i))
                {
                    gaps.Add(i);
                }
            }

            return gaps;
        }

        /// <summary>
        /// Höchste erkannte Folgennummer, oder 0 wenn keine erkannt wurde.
        /// </summary>
        /// <param name="folderNames">Ordnernamen einer Serie.</param>
        /// <returns>Die höchste Nummer oder 0.</returns>
        public static int Highest(IEnumerable<string> folderNames)
        {
            IReadOnlyList<int> numbers = Scan(folderNames);
            return numbers.Count == 0 ? 0 : numbers[^1];
        }

        /// <summary>
        /// Ermittelt die Folgennummer eines einzelnen Ordnernamens über alle bekannten Muster.
        /// </summary>
        /// <param name="folderName">Der Ordnername.</param>
        /// <returns>Die Nummer oder <see langword="null"/>, wenn keine plausible gefunden wurde.</returns>
        public static int? ExtractNumber(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return null;
            }

            foreach (EpisodeFolderParser parser in Parsers)
            {
                if (parser.TryParse(folderName, out int? number, out _) && IsPlausible(number))
                {
                    return number;
                }
            }

            // Kein Muster passt: führende Zahl, dann die Zahl zwischen zwei Trennern als letzte Chance.
            return MatchNumber(LeadingNumber(), folderName) ?? MatchNumber(SeparatedNumber(), folderName);
        }

        private static int? MatchNumber(Regex pattern, string folderName)
        {
            try
            {
                Match match = pattern.Match(folderName);
                return match.Success
                    && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                    && IsPlausible(number)
                    ? number
                    : null;
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathologischer Ordnername, gilt als „keine Nummer".
                return null;
            }
        }

        // Jahreszahlen und Katalognummern sind keine Folgennummern (gleiche Grenze wie EpisodeNumberParser).
        private static bool IsPlausible(int? number) => number is > 0 and < 1000;
    }
}

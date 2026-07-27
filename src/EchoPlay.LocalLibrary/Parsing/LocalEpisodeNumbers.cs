using System.Text.RegularExpressions;
using EchoPlay.Core.Parsing;

namespace EchoPlay.LocalLibrary.Parsing
{
    /// <summary>
    /// Ermittelt aus Ordnernamen einer Serie die vorhandenen Folgennummern und die Lücken dazwischen.
    /// Gemeinsame Grundlage für die Fehlende-Folgen-Analyse und den Online-Abgleich – vorher lag
    /// dieselbe Logik doppelt vor und lief auseinander.
    /// </summary>
    /// <remarks>
    /// Gegenüber dem früheren Vorgehen (ein Muster gewinnt für die ganze Serie) sind drei Dinge anders,
    /// jeweils aus gemessenen Fehlbefunden an einer 92-Serien-Sammlung abgeleitet:
    /// <list type="bullet">
    /// <item>Jeder Ordner wird gegen <b>alle</b> Muster geprüft. Vorher zählten Ordner, die zufällig
    /// anders benannt sind, als fehlend — bei „Sherlock Holmes – Die geheimen Fälle" waren das 38 von 67.</item>
    /// <item>Ordner, die nur aus einer Zahl bestehen („001"), werden erkannt. Alle bisherigen Muster
    /// verlangten einen Titelteil.</item>
    /// <item>Nur Zahlen von 1 bis 999 gelten als Folgennummer — dieselbe Regel wie in
    /// <see cref="EpisodeNumberParser"/>. „Stenkelfeld 2008" wurde sonst als Folge 2008 gelesen und
    /// erzeugte 1999 erfundene Lücken.</item>
    /// </list>
    /// </remarks>
    public static class LocalEpisodeNumbers
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
            // Ordner ohne Titelteil, z.B. „001" — kein bisheriges Muster deckte das ab.
            "{number:000}"
        ];

        private static readonly EpisodeFolderParser[] Parsers =
            [.. PatternCandidates.Select(p => new EpisodeFolderParser(p))];

        // Auffangnetz für Tippfehler in der Nummerierung: „203- Titel" (fehlendes Leerzeichen),
        // „203.Titel", „203_Titel". Ohne das galt in der Sammlung eine vorhandene TKKG-Folge als
        // fehlend, nur weil ein Leerzeichen im Ordnernamen fehlte.
        // (?!\d) verhindert, dass Jahreszahlen wie „1984 - …" als Folge 198 gelesen werden.
        private static readonly Regex LeadingNumber =
            new(@"^(\d{1,3})(?!\d)", RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

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

            SortedSet<int> numbers = [];
            int folderCount = 0;

            foreach (string name in folderNames)
            {
                folderCount++;
                int? number = ExtractNumber(name);
                if (number.HasValue)
                {
                    _ = numbers.Add(number.Value);
                }
            }

            // Genau eine Nummer und diese passt nicht zur Sammlungsgröße: eher Titelbestandteil
            // als Nummerierung. Ohne zweite Nummer gibt es nichts, was sie stützt.
            if (numbers.Count == 1 && numbers.Min > folderCount + SingleNumberTolerance)
            {
                return [];
            }

            return [.. numbers];
        }

        /// <summary>
        /// Liefert die fehlenden Nummern zwischen der kleinsten und der größten gefundenen Folge.
        /// </summary>
        /// <param name="numbers">Vorhandene Folgennummern.</param>
        /// <returns>Die fehlenden Nummern, aufsteigend.</returns>
        /// <remarks>
        /// Bewusst ab der kleinsten vorhandenen Nummer statt ab 1: Wer eine Serie erst ab Folge 50
        /// sammelt, hat keine 49 Lücken — er hat 49 Folgen, die er nicht besitzen will.
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

            // Kein Muster passt: führende Zahl als letzte Chance.
            try
            {
                Match match = LeadingNumber.Match(folderName);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int leading) && IsPlausible(leading))
                {
                    return leading;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathologischer Ordnername – gilt als „keine Nummer".
            }

            return null;
        }

        // Jahreszahlen und Katalognummern sind keine Folgennummern (gleiche Grenze wie EpisodeNumberParser).
        private static bool IsPlausible(int? number) => number is > 0 and < 1000;
    }
}

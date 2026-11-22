using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EchoPlay.LocalLibrary.Parsing
{
    /// <summary>
    /// Bildet aus Folgennummer und Album-Tag den Namen eines Folgenordners nach dem
    /// eingestellten Muster, etwa <c>{number:000} - {title}</c>.
    /// </summary>
    /// <remarks>
    /// Album-Tags tragen oft mehr als den Titel: „Folge 87: Geheimnisvoller Liebestrank",
    /// „Die drei !!! 87: Geheimnisvoller Liebestrank". Nummer und Serienname stehen im Muster
    /// schon vorn; im Titel noch einmal ergäben sie „087 - Folge 87: …".
    /// </remarks>
    public static partial class EpisodeFolderNaming
    {
        // Zeichen, die Windows in Ordnernamen nicht zulässt. Der Doppelpunkt wird eigens zu
        // „ -", weil er in Titeln wie „Mission: Gipfelglück" einen Untertitel trennt.
        private static readonly char[] RemovedCharacters = ['?', '*', '<', '>', '|', '"'];

        /// <summary>
        /// Entfernt Nummer und Serienname vorn aus einem Album-Tag.
        /// </summary>
        /// <param name="album">Der Album-Tag, wie er in der Datei steht.</param>
        /// <param name="episodeNumber">Die Folgennummer des Ordners; nur eine gleiche Zahl im Tag wird entfernt.</param>
        /// <param name="seriesName">Name des Serienordners.</param>
        /// <returns>Der Titel ohne Vorspann, oder eine leere Zeichenkette.</returns>
        public static string CleanAlbumTitle(string album, int episodeNumber, string seriesName)
        {
            ArgumentNullException.ThrowIfNull(album);
            ArgumentNullException.ThrowIfNull(seriesName);

            string title = album.Trim();

            // Nur eine Zahl, die der Folgennummer gleicht, gilt als Vorspann. „Teil 2: …" in
            // Folge 14 bleibt so stehen, wie es ist.
            Match numbered = NumberedPrefix().Match(title);
            if (numbered.Success
                && int.TryParse(numbered.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                && number == episodeNumber)
            {
                title = numbered.Groups["rest"].Value;
            }
            else if (seriesName.Length > 0 && title.StartsWith(seriesName, StringComparison.OrdinalIgnoreCase))
            {
                title = title[seriesName.Length..];
            }

            return title.Trim(' ', '-', '–', '—', ':', '.');
        }

        /// <summary>
        /// Ob zwei Titel dasselbe sagen und sich nur in Groß- und Kleinschreibung, Satzzeichen
        /// oder Leerzeichen unterscheiden. Ein Umlaut statt „ae" ist dagegen eine echte Änderung.
        /// </summary>
        /// <param name="first">Erster Titel.</param>
        /// <param name="second">Zweiter Titel.</param>
        /// <returns><see langword="true"/>, wenn nur die Schreibweise abweicht.</returns>
        public static bool SameTitle(string first, string second)
        {
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(second);

            return string.Equals(LettersAndDigits(first), LettersAndDigits(second), StringComparison.Ordinal);
        }

        private static string LettersAndDigits(string text)
            => string.Concat(text.Where(char.IsLetterOrDigit)).ToUpperInvariant();

        /// <summary>
        /// Setzt Nummer und Titel in das Muster ein und macht daraus einen gültigen Ordnernamen.
        /// </summary>
        /// <param name="pattern">Das Ordnermuster mit <c>{number}</c>, <c>{number:000}</c> und <c>{title}</c>.</param>
        /// <param name="episodeNumber">Die Folgennummer.</param>
        /// <param name="title">Der Titel der Folge.</param>
        /// <returns>
        /// Der Ordnername, oder <see langword="null"/>, wenn das Muster einen anderen Platzhalter
        /// enthält. Ein Muster mit <c>{*}</c> beschreibt, was im Namen stehen darf, nicht, was
        /// hineingehört; daraus lässt sich kein Name bilden.
        /// </returns>
        public static string? Render(string pattern, int episodeNumber, string title)
        {
            ArgumentNullException.ThrowIfNull(pattern);
            ArgumentNullException.ThrowIfNull(title);

            string withNumber = NumberPlaceholder().Replace(pattern, match =>
                episodeNumber.ToString(
                    new string('0', Math.Max(1, match.Groups["zeros"].Length)),
                    CultureInfo.InvariantCulture));
            string rendered = withNumber.Replace("{title}", title, StringComparison.Ordinal);

            if (rendered.Contains('{', StringComparison.Ordinal) || rendered.Contains('}', StringComparison.Ordinal))
            {
                return null;
            }

            return Sanitize(rendered);
        }

        /// <summary>
        /// Ersetzt oder entfernt Zeichen, die in Ordnernamen nicht erlaubt sind, und glättet
        /// doppelte Leerzeichen sowie Punkte und Leerzeichen am Ende, die Windows abschneidet.
        /// </summary>
        /// <param name="name">Der gewünschte Name.</param>
        /// <returns>Ein Name, den Windows unverändert anlegt.</returns>
        public static string Sanitize(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            StringBuilder builder = new(name.Length);
            foreach (char character in name)
            {
                if (character == ':')
                {
                    _ = builder.Append(" -");
                }
                else if (character is '/' or '\\')
                {
                    _ = builder.Append('-');
                }
                else if (Array.IndexOf(RemovedCharacters, character) < 0 && !char.IsControl(character))
                {
                    _ = builder.Append(character);
                }
            }

            string collapsed = MultipleSpaces().Replace(builder.ToString(), " ");
            return collapsed.Trim().TrimEnd('.', ' ');
        }

        // Vorspann bis zur Nummer, gefolgt von einem Trenner: „Folge 87: ", „087 - ",
        // „Die drei !!! 87 - ". Der Vorspann ist kurz begrenzt, damit eine Zahl tief im
        // Titel nicht als Nummer gilt.
        [GeneratedRegex(@"^(?<prefix>.{0,60}?)(?<!\d)0*(?<number>\d{1,3})(?!\d)\s*[:\-–—.)]\s*(?<rest>.*)$", RegexOptions.None, matchTimeoutMilliseconds: 500)]
        private static partial Regex NumberedPrefix();

        [GeneratedRegex(@"\{number(?::(?<zeros>0+))?\}", RegexOptions.None, matchTimeoutMilliseconds: 500)]
        private static partial Regex NumberPlaceholder();

        [GeneratedRegex(@"\s{2,}", RegexOptions.None, matchTimeoutMilliseconds: 500)]
        private static partial Regex MultipleSpaces();
    }
}

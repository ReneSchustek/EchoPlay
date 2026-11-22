using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Ordnet Titel dem Buchstaben zu, unter dem sie in einer Liste stehen. Das ist die Grundlage
    /// für die Sprungleiste über der Mediathek und für deren Gruppenüberschriften.
    /// <para>
    /// Buchstabe und Sortierung folgen derselben Regel: Die Listen unter der Leiste sortieren mit
    /// <see cref="TitleOrder"/>, und der sortiert zuerst nach <see cref="BucketOf"/>. „Ärger"
    /// steht damit unter <c>A</c> und auch zwischen den übrigen A-Titeln, „hui buh" unter
    /// <c>H</c>, Titel mit Ziffer am Ende wie das Fach <c>#</c> in der Leiste. Führende Artikel
    /// bleiben Teil des Titels; „Die drei ???" steht unter <c>D</c>.
    /// </para>
    /// </summary>
    public static class AlphabetIndex
    {
        /// <summary>
        /// Sammelbuchstabe für alles, was nicht mit A–Z beginnt: Ziffern, Zeichen, leere Titel
        /// und Schriften außerhalb des lateinischen Alphabets.
        /// </summary>
        public const char OtherBucket = '#';

        /// <summary>
        /// Die Buchstaben der Leiste in fester Reihenfolge: A–Z, danach der Sammelbuchstabe.
        /// <para>
        /// Bewusst unveränderlich und vollständig: Eine Leiste, die je nach Bestand ihre Breite
        /// ändert, ist kein verlässlicher Anlaufpunkt. Buchstaben ohne Einträge werden
        /// deaktiviert dargestellt, nicht weggelassen.
        /// </para>
        /// </summary>
        public static IReadOnlyList<char> Buckets { get; } =
            Enumerable.Range('A', 26).Select(c => (char)c).Append(OtherBucket).ToArray();

        /// <summary>
        /// Sortierung für Listen unter der Sprungleiste: zuerst das Fach in der Reihenfolge von
        /// <see cref="Buckets"/>, darin deutsch und ohne Groß-/Kleinschreibung, bei Gleichstand
        /// nach Zeichencode, damit die Reihenfolge eindeutig bleibt.
        /// </summary>
        public static IComparer<string?> TitleOrder { get; } = Comparer<string?>.Create(CompareTitles);

        /// <summary>
        /// Bestimmt den Buchstaben, unter dem ein Titel einsortiert wird.
        /// </summary>
        /// <param name="title">Titel, so wie er auch angezeigt und sortiert wird.</param>
        /// <returns>Ein Buchstabe aus <c>A</c>–<c>Z</c> oder <see cref="OtherBucket"/>.</returns>
        public static char BucketOf(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return OtherBucket;
            }

            char first = title.TrimStart()[0];

            // Umlaute und diakritische Zeichen laufen auf ihren Grundbuchstaben: Wer „Ärger"
            // sucht, erwartet ihn unter A und nicht in einem eigenen Fach am Ende der Leiste.
            char basisLetter = RemoveDiacritics(first);

            // ß hat keinen Grundbuchstaben in der Zerlegung und wird gesondert behandelt.
            if (basisLetter is 'ß' or 'ẞ')
            {
                return 'S';
            }

            char upper = char.ToUpperInvariant(basisLetter);

            return upper is >= 'A' and <= 'Z' ? upper : OtherBucket;
        }

        /// <summary>
        /// Ermittelt, welche Buchstaben im übergebenen Bestand tatsächlich vorkommen.
        /// </summary>
        /// <param name="titles">Titel des Bestands.</param>
        /// <returns>Menge der belegten Buchstaben; leer, wenn der Bestand leer ist.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="titles"/> ist <see langword="null"/>.</exception>
        public static IReadOnlySet<char> OccupiedBuckets(IEnumerable<string?> titles)
        {
            ArgumentNullException.ThrowIfNull(titles);

            return titles.Select(BucketOf).ToHashSet();
        }

        private static readonly CompareInfo GermanCompare = CultureInfo.GetCultureInfo("de-DE").CompareInfo;

        private static int CompareTitles(string? left, string? right)
        {
            int byBucket = BucketRank(left).CompareTo(BucketRank(right));
            if (byBucket != 0)
            {
                return byBucket;
            }

            int byText = GermanCompare.Compare(left, right, CompareOptions.IgnoreCase);
            return byText != 0 ? byText : string.CompareOrdinal(left, right);
        }

        // A–Z liegen auf 0–25, das Sammelfach dahinter auf 26: wie in der Leiste.
        private static int BucketRank(string? title)
        {
            char bucket = BucketOf(title);
            return bucket == OtherBucket ? 26 : bucket - 'A';
        }

        /// <summary>
        /// Zerlegt ein Zeichen und entfernt die diakritische Marke, sofern es eine trägt.
        /// </summary>
        /// <param name="value">Zu prüfendes Zeichen.</param>
        /// <returns>Das Zeichen ohne diakritische Marke, sonst unverändert.</returns>
        private static char RemoveDiacritics(char value)
        {
            // FormD trennt Grundbuchstabe und Marke: 'Ä' wird zu 'A' + Trema. Das erste
            // Zeichen der Zerlegung ist damit der gesuchte Grundbuchstabe.
            string decomposed = value.ToString().Normalize(NormalizationForm.FormD);

            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    return c;
                }
            }

            return value;
        }
    }
}

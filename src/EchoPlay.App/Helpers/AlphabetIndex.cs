using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Ordnet Titel dem Buchstaben zu, unter dem sie in einer Liste stehen — die Grundlage
    /// für die Sprungleiste über der Mediathek und für deren Gruppenüberschriften.
    /// <para>
    /// Der Buchstabe folgt der Sortierung, die die Anwendung ohnehin verwendet, und erfindet
    /// keine eigene. <c>SeriesDataService</c> sortiert nach <c>Title</c>, ohne führende Artikel
    /// abzutrennen; „Die drei ???" steht damit unter <c>D</c>. Würde die Leiste hier eine
    /// eigene Regel anwenden und den Artikel abschneiden, zeigte der Sprung auf <c>D</c> auf
    /// einen Eintrag, der in der Liste unter <c>T</c> steht — und der Nutzer landete im Nichts.
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
            [.. Enumerable.Range('A', 26).Select(c => (char)c).Append(OtherBucket)];

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

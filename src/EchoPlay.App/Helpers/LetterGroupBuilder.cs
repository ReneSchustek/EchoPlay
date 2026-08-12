using System;
using System.Collections.Generic;
using System.Linq;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Baut aus einer nach Titel sortierten Kachelliste die Buchstaben-Abschnitte der
    /// Mediathek und teilt dabei genau den Abschnitt auf, in dem die gewählte Kachel steht.
    /// <para>
    /// Die Reihenfolge der Kacheln bleibt unangetastet: Die Liste kommt bereits sortiert aus
    /// der Datenschicht, und der Buchstabe wird aus demselben Titel gebildet. Würde hier neu
    /// sortiert, könnten Abschnittsüberschrift und Inhalt auseinanderlaufen.
    /// </para>
    /// </summary>
    public static class LetterGroupBuilder
    {
        /// <summary>
        /// Gruppiert die Kacheln nach ihrem Anfangsbuchstaben.
        /// </summary>
        /// <typeparam name="T">Typ der Kachel-ViewModels.</typeparam>
        /// <param name="items">Kacheln in Anzeigereihenfolge.</param>
        /// <param name="titleOf">Liefert den Titel einer Kachel.</param>
        /// <param name="selectedIndex">Index der gewählten Kachel in <paramref name="items"/>; negativ, wenn keine gewählt ist.</param>
        /// <param name="availableWidth">Verfügbare Breite in Pixel für die Aufteilung.</param>
        /// <returns>Die Abschnitte in Anzeigereihenfolge; leer, wenn keine Kacheln vorliegen.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="items"/> oder <paramref name="titleOf"/> ist <see langword="null"/>.</exception>
        public static IReadOnlyList<LetterGroup<T>> Build<T>(
            IReadOnlyList<T> items,
            Func<T, string?> titleOf,
            int selectedIndex,
            double availableWidth) where T : class
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(titleOf);

            if (items.Count == 0)
            {
                return [];
            }

            T? selected = selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex] : null;

            List<LetterGroup<T>> groups = [];

            // Aufeinanderfolgende Kacheln mit gleichem Buchstaben bilden einen Abschnitt.
            // Das setzt die sortierte Eingabe voraus und hält genau deshalb die Reihenfolge:
            // Ein Titel wandert nie in einen Abschnitt, der weiter oben schon geschlossen ist.
            int start = 0;
            while (start < items.Count)
            {
                char letter = AlphabetIndex.BucketOf(titleOf(items[start]));

                int end = start + 1;
                while (end < items.Count && AlphabetIndex.BucketOf(titleOf(items[end])) == letter)
                {
                    end++;
                }

                List<T> slice = [.. items.Skip(start).Take(end - start)];
                int selectedInSlice = selected is null ? -1 : slice.IndexOf(selected);

                groups.Add(BuildGroup(letter, slice, selectedInSlice, availableWidth));
                start = end;
            }

            return groups;
        }

        /// <summary>
        /// Ermittelt die Buchstaben, unter denen die Liste Einträge führt.
        /// </summary>
        /// <typeparam name="T">Typ der Kachel-ViewModels.</typeparam>
        /// <param name="items">Kacheln des Bestands.</param>
        /// <param name="titleOf">Liefert den Titel einer Kachel.</param>
        /// <returns>Belegte Buchstaben; alle übrigen erscheinen in der Leiste deaktiviert.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="items"/> oder <paramref name="titleOf"/> ist <see langword="null"/>.</exception>
        public static IReadOnlySet<char> OccupiedLetters<T>(IReadOnlyList<T> items, Func<T, string?> titleOf)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(titleOf);

            return items.Select(item => AlphabetIndex.BucketOf(titleOf(item))).ToHashSet();
        }

        private static LetterGroup<T> BuildGroup<T>(
            char letter,
            List<T> slice,
            int selectedInSlice,
            double availableWidth) where T : class
        {
            // Ohne Auswahl in diesem Abschnitt gibt es keine Aufklappstelle — alles steht oben.
            if (selectedInSlice < 0)
            {
                return new LetterGroup<T>(letter, slice, [], holdsSelection: false);
            }

            int splitIndex = AccordionSplitHelper.CalculateSplitIndex(
                selectedInSlice, slice.Count, availableWidth);

            (IReadOnlyList<T> top, IReadOnlyList<T> bottom) = AccordionSplitHelper.Split(slice, splitIndex);

            return new LetterGroup<T>(letter, top, bottom, holdsSelection: true);
        }
    }
}

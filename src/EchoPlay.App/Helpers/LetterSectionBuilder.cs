using System;
using System.Collections.Generic;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Bereitet die Buchstaben-Abschnitte der Mediathek für die Anzeige auf und teilt sie an
    /// der Stelle, an der der Folgenbereich aufklappt.
    /// <para>
    /// Der Folgenbereich steht fest im Seitenaufbau; über ihm liegt eine Abschnittsliste,
    /// unter ihm die zweite. Deshalb liefert diese Klasse zwei Listen und nicht eine: Die
    /// Seite muss den aufgeklappten Bereich nicht in eine Liste hineinreichen, was in einer
    /// Vorlage ohnehin nicht ginge.
    /// </para>
    /// </summary>
    public static class LetterSectionBuilder
    {
        /// <summary>
        /// Baut die Abschnitte oberhalb und unterhalb der Aufklappstelle.
        /// </summary>
        /// <typeparam name="T">Typ der Kachel-ViewModels.</typeparam>
        /// <param name="items">Kacheln in Anzeigereihenfolge.</param>
        /// <param name="titleOf">Liefert den Titel einer Kachel.</param>
        /// <param name="selectedIndex">Index der gewählten Kachel; negativ, wenn keine gewählt ist.</param>
        /// <param name="availableWidth">Verfügbare Breite in Pixel für die Aufteilung.</param>
        /// <param name="automationNameFormat">
        /// Vorlage für die Sprachausgabe mit <c>{0}</c> als Platzhalter für den Buchstaben,
        /// etwa „Abschnitt {0}". Ohne Platzhalter wird nur der Buchstabe gemeldet.
        /// </param>
        /// <returns>
        /// Die Abschnitte oberhalb der Aufklappstelle und die darunter. Ohne Auswahl steht
        /// alles oben und die untere Liste bleibt leer.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="items"/> oder <paramref name="titleOf"/> ist <see langword="null"/>.</exception>
        public static (IReadOnlyList<LetterSection> Top, IReadOnlyList<LetterSection> Bottom) Build<T>(
            IReadOnlyList<T> items,
            Func<T, string?> titleOf,
            int selectedIndex,
            double availableWidth,
            string automationNameFormat = "{0}") where T : class
        {
            ArgumentNullException.ThrowIfNull(automationNameFormat);

            IReadOnlyList<LetterGroup<T>> groups =
                LetterGroupBuilder.Build(items, titleOf, selectedIndex, availableWidth);

            List<LetterSection> top = [];
            List<LetterSection> bottom = [];
            bool passedSelection = false;

            foreach (LetterGroup<T> group in groups)
            {
                string automationName = FormatAutomationName(automationNameFormat, group.Header);

                if (!group.HoldsSelection)
                {
                    // Abschnitte ohne Auswahl sind ungeteilt: Der Erbauer legt alle Kacheln
                    // in den oberen Teil und lässt den unteren leer.
                    List<LetterSection> target = passedSelection ? bottom : top;
                    target.Add(new LetterSection(group.Header, AsObjects(group.TopItems), showsHeader: true, automationName));
                    continue;
                }

                // Der geteilte Abschnitt beginnt oberhalb des aufgeklappten Bereichs und trägt
                // dort seine Überschrift; der Rest darunter ist seine Fortsetzung.
                top.Add(new LetterSection(group.Header, AsObjects(group.TopItems), showsHeader: true, automationName));

                if (group.BottomItems.Count > 0)
                {
                    bottom.Add(new LetterSection(group.Header, AsObjects(group.BottomItems), showsHeader: false, automationName));
                }

                passedSelection = true;
            }

            return (top, bottom);
        }

        /// <summary>
        /// Teilt die Liste ohne Buchstaben-Gliederung an der Aufklappstelle.
        /// <para>
        /// Für Listen, die nicht nach Namen sortiert sind. Buchstaben-Abschnitte wären dort
        /// irreführend: Sie entstehen aus der Reihenfolge, und wer nach Folgenanzahl sortiert,
        /// bekäme ein Dutzend Abschnitte mit demselben Buchstaben. Die Sprungleiste zeigte dann
        /// auf Stellen, die es in dieser Ordnung gar nicht gibt.
        /// </para>
        /// </summary>
        /// <typeparam name="T">Typ der Kachel-ViewModels.</typeparam>
        /// <param name="items">Kacheln in Anzeigereihenfolge.</param>
        /// <param name="selectedIndex">Index der gewählten Kachel; negativ, wenn keine gewählt ist.</param>
        /// <param name="availableWidth">Verfügbare Breite in Pixel für die Aufteilung.</param>
        /// <param name="automationName">
        /// Beschriftung für die Sprachausgabe, etwa „Serien". Sie darf nicht leer bleiben: Ohne
        /// Namen liest WinUI den Typnamen des Anzeigemodells vor.
        /// </param>
        /// <returns>Je ein Abschnitt ohne Überschrift ober- und unterhalb der Aufklappstelle.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="items"/> ist <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="automationName"/> ist leer.</exception>
        public static (IReadOnlyList<LetterSection> Top, IReadOnlyList<LetterSection> Bottom) BuildUngrouped<T>(
            IReadOnlyList<T> items,
            int selectedIndex,
            double availableWidth,
            string automationName) where T : class
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentException.ThrowIfNullOrWhiteSpace(automationName);

            if (items.Count == 0)
            {
                return ([], []);
            }

            if (selectedIndex < 0 || selectedIndex >= items.Count)
            {
                return ([Ungrouped(items, automationName)], []);
            }

            int splitIndex = AccordionSplitHelper.CalculateSplitIndex(selectedIndex, items.Count, availableWidth);
            (IReadOnlyList<T> top, IReadOnlyList<T> bottom) = AccordionSplitHelper.Split(items, splitIndex);

            List<LetterSection> above = top.Count > 0 ? [Ungrouped(top, automationName)] : [];
            List<LetterSection> below = bottom.Count > 0 ? [Ungrouped(bottom, automationName)] : [];

            return (above, below);
        }

        private static LetterSection Ungrouped<T>(IReadOnlyList<T> items, string automationName) where T : class =>
            new(string.Empty, AsObjects(items), showsHeader: false, automationName);

        private static string FormatAutomationName(string format, string header)
        {
            // Eine Vorlage ohne Platzhalter gäbe jedem Abschnitt denselben Namen — die
            // Sprachausgabe könnte sie dann nicht auseinanderhalten.
            return format.Contains("{0}", StringComparison.Ordinal)
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, format, header)
                : header;
        }

        private static List<object> AsObjects<T>(IReadOnlyList<T> items) where T : class
        {
            List<object> result = new(items.Count);

            foreach (T item in items)
            {
                result.Add(item);
            }

            return result;
        }
    }
}

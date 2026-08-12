using System;
using System.Collections.Generic;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Führt die Buchstaben-Abschnitte einer Mediathek-Seite: füllt die Listen ober- und
    /// unterhalb des aufklappbaren Folgenbereichs und springt auf Wunsch zu einem Buchstaben.
    /// <para>
    /// Er kennt die Buchstabenleiste nicht, sondern meldet nur, welche Buchstaben belegt sind.
    /// Damit bleibt er an jeder Seite verwendbar, gleich ob sie eine Leiste zeigt.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Typ der Kachel-ViewModels.</typeparam>
    public sealed class LetterSectionSplitHandler<T> where T : class
    {
        private readonly ILetterSectionHost _topHost;
        private readonly ILetterSectionHost _bottomHost;
        private readonly Func<IReadOnlyList<T>> _getItems;
        private readonly Func<T, string?> _titleOf;
        private readonly Func<int> _getSelectedIndex;
        private readonly Func<double> _getAvailableWidth;
        private readonly string _automationNameFormat;
        private readonly Func<bool>? _groupByLetter;
        private readonly string? _ungroupedAutomationName;

        private int _lastTilesPerRow;

        /// <summary>
        /// Erzeugt einen Handler für ein Paar aus oberer und unterer Abschnittsliste.
        /// </summary>
        /// <param name="topHost">Liste oberhalb des Folgenbereichs.</param>
        /// <param name="bottomHost">Liste unterhalb des Folgenbereichs.</param>
        /// <param name="getItems">Liefert die aktuelle Kachelliste aus dem ViewModel.</param>
        /// <param name="titleOf">Liefert den Titel einer Kachel — er bestimmt den Buchstaben.</param>
        /// <param name="getSelectedIndex">Liefert den gewählten Index; -1, wenn nichts gewählt ist.</param>
        /// <param name="getAvailableWidth">Liefert die verfügbare Breite in Pixel.</param>
        /// <param name="automationNameFormat">
        /// Vorlage für die Sprachausgabe der Abschnitte, mit <c>{0}</c> für den Buchstaben.
        /// </param>
        /// <param name="groupByLetter">
        /// Ob nach Buchstaben gegliedert wird. <see langword="null"/> heißt: immer. Seiten, die
        /// auch anders sortieren können, geben hier ihre Sortierung weiter — bei einer Liste
        /// nach Folgenanzahl wäre eine Buchstaben-Gliederung nur noch Zufall.
        /// </param>
        /// <param name="ungroupedAutomationName">
        /// Beschriftung der ungegliederten Liste für die Sprachausgabe. Pflicht, sobald
        /// <paramref name="groupByLetter"/> gesetzt ist.
        /// </param>
        public LetterSectionSplitHandler(
            ILetterSectionHost topHost,
            ILetterSectionHost bottomHost,
            Func<IReadOnlyList<T>> getItems,
            Func<T, string?> titleOf,
            Func<int> getSelectedIndex,
            Func<double> getAvailableWidth,
            string automationNameFormat,
            Func<bool>? groupByLetter = null,
            string? ungroupedAutomationName = null)
        {
            _topHost = topHost;
            _bottomHost = bottomHost;
            _getItems = getItems;
            _titleOf = titleOf;
            _getSelectedIndex = getSelectedIndex;
            _getAvailableWidth = getAvailableWidth;
            _automationNameFormat = automationNameFormat;
            _groupByLetter = groupByLetter;
            _ungroupedAutomationName = ungroupedAutomationName;

            if (groupByLetter is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(ungroupedAutomationName);
            }
        }

        /// <summary>
        /// Ob die Liste gerade nach Buchstaben gegliedert ist. Nur dann taugt die Sprungleiste
        /// als Anlaufpunkt; sonst gehört sie ausgeblendet.
        /// </summary>
        public bool IsGroupedByLetter => _groupByLetter is null || _groupByLetter();

        /// <summary>
        /// Buchstaben, unter denen der aktuelle Bestand Einträge führt. Die Leiste stellt alle
        /// übrigen deaktiviert dar.
        /// </summary>
        public IReadOnlySet<char> OccupiedLetters { get; private set; } = new HashSet<char>();

        /// <summary>
        /// Baut die Abschnitte aus dem aktuellen Bestand und der aktuellen Auswahl neu auf.
        /// </summary>
        public void UpdateSections()
        {
            IReadOnlyList<T> items = _getItems();

            (IReadOnlyList<LetterSection> top, IReadOnlyList<LetterSection> bottom) = IsGroupedByLetter
                ? LetterSectionBuilder.Build(
                    items, _titleOf, _getSelectedIndex(), _getAvailableWidth(), _automationNameFormat)
                : LetterSectionBuilder.BuildUngrouped(
                    items, _getSelectedIndex(), _getAvailableWidth(), _ungroupedAutomationName!);

            _topHost.Sections = top;
            _bottomHost.Sections = bottom;
            OccupiedLetters = LetterGroupBuilder.OccupiedLetters(items, _titleOf);
        }

        /// <summary>
        /// Rechnet bei Größenänderungen nur dann neu, wenn sich die Anzahl der Kacheln je Reihe
        /// tatsächlich geändert hat. Sonst baute jede Pixelbewegung am Fensterrand die ganze
        /// Liste neu auf.
        /// </summary>
        public void HandleSizeChanged()
        {
            int tilesPerRow = AccordionSplitHelper.CalculateTilesPerRow(_getAvailableWidth());

            if (tilesPerRow != _lastTilesPerRow)
            {
                _lastTilesPerRow = tilesPerRow;
                UpdateSections();
            }
        }

        /// <summary>
        /// Holt den Abschnitt eines Buchstabens ins Blickfeld.
        /// </summary>
        /// <param name="letter">Angesprungener Buchstabe.</param>
        /// <returns><see langword="true"/>, wenn ein Abschnitt dazu gefunden wurde.</returns>
        public bool BringLetterIntoView(char letter)
        {
            string header = letter.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return BringIntoView(_topHost, header) || BringIntoView(_bottomHost, header);
        }

        /// <summary>
        /// Holt den Abschnitt ins Blickfeld, in dem eine bestimmte Kachel steht.
        /// <para>
        /// Gebraucht nach dem Zuklappen des Folgenbereichs: Der klappt weit unten auf, und wer
        /// ihn schließt, stünde sonst irgendwo in der Liste statt bei der Serie, die er gerade
        /// offen hatte.
        /// </para>
        /// </summary>
        /// <param name="item">Die Kachel, zu der zurückgekehrt wird.</param>
        /// <returns><see langword="true"/>, wenn ihr Abschnitt erreicht wurde.</returns>
        public bool BringItemIntoView(T item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return BringSectionContaining(_topHost, item) || BringSectionContaining(_bottomHost, item);
        }

        private static bool BringSectionContaining(ILetterSectionHost host, T item)
        {
            foreach (LetterSection section in host.Sections)
            {
                foreach (object tile in section.Items)
                {
                    if (ReferenceEquals(tile, item))
                    {
                        return host.TryBringIntoView(section);
                    }
                }
            }

            return false;
        }

        private static bool BringIntoView(ILetterSectionHost host, string header)
        {
            foreach (LetterSection section in host.Sections)
            {
                // Nur Abschnitte mit Überschrift kommen als Sprungziel infrage: Der
                // Fortsetzungsteil unterhalb des Folgenbereichs trägt denselben Buchstaben,
                // ist aber nicht der Anfang des Abschnitts.
                if (!section.ShowsHeader || !string.Equals(section.Header, header, StringComparison.Ordinal))
                {
                    continue;
                }

                if (host.TryBringIntoView(section))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

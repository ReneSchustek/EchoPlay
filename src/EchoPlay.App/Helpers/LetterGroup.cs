using System.Collections.Generic;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Ein Buchstaben-Abschnitt der Mediathek: die Kacheln unter einem Buchstaben, bereits
    /// aufgeteilt für das Akkordeon.
    /// <para>
    /// Die Aufteilung steckt im Abschnitt selbst, weil das Akkordeon innerhalb der Gruppe
    /// aufklappt, in der die gewählte Kachel steht. Ohne Gruppen wäre es eine Liste mit
    /// einer Trennstelle; mit Gruppen hat genau ein Abschnitt eine Trennstelle und alle
    /// übrigen keine.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Typ der Kachel-ViewModels.</typeparam>
    public sealed class LetterGroup<T> where T : class
    {
        /// <summary>
        /// Erzeugt einen Buchstaben-Abschnitt.
        /// </summary>
        /// <param name="letter">Buchstabe des Abschnitts, <c>A</c>–<c>Z</c> oder <c>#</c>.</param>
        /// <param name="topItems">Kacheln vor der Aufklappstelle.</param>
        /// <param name="bottomItems">Kacheln nach der Aufklappstelle.</param>
        /// <param name="holdsSelection">Ob die gewählte Kachel in diesem Abschnitt steht.</param>
        public LetterGroup(char letter, IReadOnlyList<T> topItems, IReadOnlyList<T> bottomItems, bool holdsSelection)
        {
            Letter = letter;
            TopItems = topItems;
            BottomItems = bottomItems;
            HoldsSelection = holdsSelection;
        }

        /// <summary>Buchstabe des Abschnitts — zugleich seine Überschrift.</summary>
        public char Letter { get; }

        /// <summary>Beschriftung der Abschnittsüberschrift.</summary>
        public string Header => Letter.ToString();

        /// <summary>Kacheln oberhalb der Aufklappstelle.</summary>
        public IReadOnlyList<T> TopItems { get; }

        /// <summary>Kacheln unterhalb der Aufklappstelle.</summary>
        public IReadOnlyList<T> BottomItems { get; }

        /// <summary>
        /// Ob die gewählte Kachel in diesem Abschnitt steht. Nur dort gehört das Akkordeon hin.
        /// </summary>
        public bool HoldsSelection { get; }

        /// <summary>Alle Kacheln des Abschnitts in ihrer Reihenfolge.</summary>
        public int Count => TopItems.Count + BottomItems.Count;
    }
}

using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Ein Buchstaben-Abschnitt, so wie die Seite ihn zeigt: eine Überschrift und die Kacheln
    /// darunter.
    /// <para>
    /// Die Kacheln stehen als <see cref="object"/> und nicht typisiert, weil beide
    /// Mediathek-Ansichten verschiedene Kachel-ViewModels führen. Welcher Typ es ist, legt die
    /// Kachel-Vorlage der jeweiligen Seite fest; der Abschnitt selbst muss es nicht wissen.
    /// </para>
    /// </summary>
    public sealed class LetterSection
    {
        /// <summary>
        /// Erzeugt einen Abschnitt.
        /// </summary>
        /// <param name="header">Buchstabe als Überschrift, <c>A</c>–<c>Z</c> oder <c>#</c>.</param>
        /// <param name="items">Kacheln des Abschnitts in Anzeigereihenfolge.</param>
        /// <param name="showsHeader">Ob die Überschrift gezeigt wird.</param>
        /// <param name="automationName">Beschriftung für die Sprachausgabe, etwa „Abschnitt B".</param>
        public LetterSection(string header, IReadOnlyList<object> items, bool showsHeader, string automationName)
        {
            Header = header;
            Items = items;
            ShowsHeader = showsHeader;
            AutomationName = automationName;
        }

        /// <summary>Buchstabe des Abschnitts — zugleich seine Überschrift.</summary>
        public string Header { get; }

        /// <summary>Kacheln des Abschnitts.</summary>
        public IReadOnlyList<object> Items { get; }

        /// <summary>
        /// Ob die Überschrift gezeigt wird.
        /// <para>
        /// Sie fehlt genau einmal: unterhalb des aufgeklappten Folgenbereichs. Dort geht der
        /// Abschnitt weiter, in dem die gewählte Serie steht — seine Überschrift stand schon
        /// oberhalb. Ein zweites Mal derselbe Buchstabe läse sich wie ein neuer Abschnitt.
        /// </para>
        /// </summary>
        public bool ShowsHeader { get; }

        /// <summary>Sichtbarkeit der Überschrift für die Bindung in der Vorlage.</summary>
        public Visibility HeaderVisibility => ShowsHeader ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Beschriftung für die Sprachausgabe. Ohne sie liest WinUI den Typnamen des
        /// Anzeigemodells vor — für einen Abschnitt der Mediathek eine leere Auskunft.
        /// </summary>
        public string AutomationName { get; }
    }
}

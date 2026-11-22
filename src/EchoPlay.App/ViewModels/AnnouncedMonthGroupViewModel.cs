using System.Collections.Generic;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Ein Monat auf der Seite „Angekündigt" mit seinen Kacheln.
    /// </summary>
    public sealed class AnnouncedMonthGroupViewModel
    {
        /// <summary>
        /// Initialisiert die Gruppe.
        /// </summary>
        /// <param name="label">Monatsname mit Jahr, etwa „Dezember 2026".</param>
        /// <param name="items">Die Kacheln des Monats.</param>
        public AnnouncedMonthGroupViewModel(string label, IReadOnlyList<ReleaseCalendarItemViewModel> items)
        {
            Label = label;
            Items = items;
        }

        /// <summary>Monatsname mit Jahr.</summary>
        public string Label { get; }

        /// <summary>Die Kacheln des Monats, nach Erscheinungstag.</summary>
        public IReadOnlyList<ReleaseCalendarItemViewModel> Items { get; }
    }
}

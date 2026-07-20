using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Gemeinsame Basis für die Dashboard-Sektions-Sub-ViewModels, die eine Kachelliste halten
    /// und deren Sichtbarkeit von der Anzahl abhängt. Reiner Daten-Halter: das Top-VM befüllt
    /// über <see cref="SetItems"/>.
    /// </summary>
    /// <typeparam name="T">Der Typ der Kachel-ViewModels.</typeparam>
    public abstract class DashboardListSectionViewModel<T> : ObservableObject
    {
        private IReadOnlyList<T> _items = [];

        /// <summary>
        /// Die Einträge der Sektion. Änderungen benachrichtigen zusätzlich
        /// <see cref="SectionVisibility"/>.
        /// </summary>
        public IReadOnlyList<T> Items
        {
            get => _items;
            private set
            {
                if (SetProperty(ref _items, value))
                {
                    OnPropertyChanged(nameof(SectionVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit der Sektion – sichtbar sobald mindestens ein Eintrag vorhanden ist.
        /// </summary>
        public Visibility SectionVisibility =>
            _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Ersetzt die Einträge der Sektion.</summary>
        /// <param name="items">Die neuen Einträge.</param>
        public void SetItems(IReadOnlyList<T> items)
        {
            Items = items;
        }
    }
}

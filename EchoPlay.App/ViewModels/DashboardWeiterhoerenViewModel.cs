using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den „Weiterhören"-Abschnitt des Dashboards.
    /// Hält angefangene Serien mit noch ungehörten Folgen – der Nutzer hat mindestens eine
    /// Folge gehört, aber die Serie ist noch nicht vollständig abgeschlossen.
    /// Reiner Daten-Halter: das Top-VM befüllt über <see cref="SetItems"/>.
    /// </summary>
    public sealed class DashboardWeiterhoerenViewModel : ObservableObject
    {
        private IReadOnlyList<UnheardSeriesCardViewModel> _unheardSeries = [];

        /// <summary>
        /// Angefangene Serien mit noch ungehörten Folgen. Klick auf eine Kachel navigiert
        /// zur Seriendetailseite.
        /// </summary>
        public IReadOnlyList<UnheardSeriesCardViewModel> UnheardSeries
        {
            get => _unheardSeries;
            private set
            {
                if (SetProperty(ref _unheardSeries, value))
                {
                    OnPropertyChanged(nameof(UnheardSectionVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Abschnitts – sichtbar sobald mindestens eine angefangene Serie da ist.
        /// </summary>
        public Visibility UnheardSectionVisibility =>
            _unheardSeries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Ersetzt die Liste der angefangenen Serien.</summary>
        /// <param name="items">Die neuen Einträge.</param>
        public void SetItems(IReadOnlyList<UnheardSeriesCardViewModel> items)
        {
            UnheardSeries = items;
        }
    }
}

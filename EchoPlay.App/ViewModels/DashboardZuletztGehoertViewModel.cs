using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den „Zuletzt gehört"-Abschnitt des Dashboards.
    /// Hält die zuletzt gehörten Serien – pro Serie nur der jüngste Eintrag, sortiert nach
    /// Wiedergabezeitpunkt. Reiner Daten-Halter.
    /// </summary>
    public sealed class DashboardZuletztGehoertViewModel : ObservableObject
    {
        private IReadOnlyList<RecentSeriesCardViewModel> _recentSeries = [];

        /// <summary>
        /// Zuletzt gehörte Serien, neueste zuerst. Wird als horizontale Kachelreihe angezeigt.
        /// </summary>
        public IReadOnlyList<RecentSeriesCardViewModel> RecentSeries
        {
            get => _recentSeries;
            private set
            {
                if (SetProperty(ref _recentSeries, value))
                {
                    OnPropertyChanged(nameof(RecentSectionVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Abschnitts – sichtbar sobald mindestens ein Eintrag da ist.
        /// </summary>
        public Visibility RecentSectionVisibility =>
            _recentSeries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Ersetzt die Liste der zuletzt gehörten Serien.</summary>
        /// <param name="items">Die neuen Einträge.</param>
        public void SetItems(IReadOnlyList<RecentSeriesCardViewModel> items)
        {
            RecentSeries = items;
        }
    }
}

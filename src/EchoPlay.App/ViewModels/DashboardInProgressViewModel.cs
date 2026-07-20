using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den „In Progress"-Abschnitt des Dashboards.
    /// Hält die aktuell laufenden Episoden – Wiedergabestand größer als Null, noch nicht
    /// abgeschlossen – sortiert nach letzter Wiedergabe. Reiner Daten-Halter.
    /// </summary>
    public sealed class DashboardInProgressViewModel : ObservableObject
    {
        private IReadOnlyList<NewEpisodeCardViewModel> _inProgressEpisodes = [];

        /// <summary>
        /// Episoden, die aktuell gehört werden. Sortiert nach dem Zeitpunkt der letzten
        /// Wiedergabe, neueste zuerst.
        /// </summary>
        public IReadOnlyList<NewEpisodeCardViewModel> InProgressEpisodes
        {
            get => _inProgressEpisodes;
            private set
            {
                if (SetProperty(ref _inProgressEpisodes, value))
                {
                    OnPropertyChanged(nameof(InProgressSectionVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Abschnitts – sichtbar sobald mindestens eine laufende Episode da ist.
        /// </summary>
        public Visibility InProgressSectionVisibility =>
            _inProgressEpisodes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Ersetzt die Liste der laufenden Episoden.</summary>
        /// <param name="items">Die neuen Einträge.</param>
        public void SetItems(IReadOnlyList<NewEpisodeCardViewModel> items)
        {
            InProgressEpisodes = items;
        }
    }
}

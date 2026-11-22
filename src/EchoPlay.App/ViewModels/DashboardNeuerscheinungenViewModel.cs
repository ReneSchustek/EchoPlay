using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den Neuerscheinungen-Abschnitt des Dashboards.
    /// Hält die nach Monat (bzw. „Angekündigt") gruppierten Neuerscheinungen. Abgefragt
    /// werden sie im Startbild, die Startseite liest nur den Zwischenspeicher; einen Ladezustand
    /// gibt es deshalb nicht. Der Aufbau der Gruppen liegt im <see cref="NewReleaseGroupBuilder"/>;
    /// dieses VM ist ein reiner Daten- und Visibility-Halter.
    /// </summary>
    public sealed class DashboardNeuerscheinungenViewModel : ObservableObject
    {
        private ObservableCollection<NewEpisodesGroupViewModel> _newEpisodeGroups = [];

        /// <summary>
        /// Neue, ungehörte Episoden überwachter Serien, gruppiert nach Monat.
        /// </summary>
        public ObservableCollection<NewEpisodesGroupViewModel> NewEpisodeGroups
        {
            get => _newEpisodeGroups;
            private set
            {
                if (SetProperty(ref _newEpisodeGroups, value))
                {
                    OnPropertyChanged(nameof(NewEpisodeGroupsVisibility));
                    OnPropertyChanged(nameof(NewReleasesSectionVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit der Neuerscheinungs-Gruppen-Liste, sichtbar, sobald mindestens
        /// eine Gruppe mit Episoden vorhanden ist.
        /// </summary>
        public Visibility NewEpisodeGroupsVisibility =>
            _newEpisodeGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit der Überschrift des Abschnitts, sichtbar, sobald Gruppen vorliegen.
        /// </summary>
        public Visibility NewReleasesSectionVisibility => NewEpisodeGroupsVisibility;

        /// <summary>Ersetzt die Neuerscheinungs-Gruppen mit der übergebenen Liste.</summary>
        /// <param name="groups">Die neuen Gruppen, bereits nach Monat sortiert.</param>
        public void SetGroups(IReadOnlyList<NewEpisodesGroupViewModel> groups)
        {
            NewEpisodeGroups = new ObservableCollection<NewEpisodesGroupViewModel>(groups);
        }
    }
}

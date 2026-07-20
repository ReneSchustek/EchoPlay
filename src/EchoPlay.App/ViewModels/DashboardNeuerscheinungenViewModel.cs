using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den Neuerscheinungen-Abschnitt des Dashboards.
    /// Hält die nach Monat (bzw. „Angekündigt") gruppierten Neuerscheinungen und den
    /// Lade-Zustand, solange die iTunes-Abfrage im Hintergrund läuft. Der eigentliche Aufbau
    /// der Gruppen liegt im <see cref="DashboardDataLoader"/>; dieses VM ist ein reiner
    /// Daten- und Visibility-Halter.
    /// </summary>
    public sealed class DashboardNeuerscheinungenViewModel : ObservableObject
    {
        private ObservableCollection<NewEpisodesGroupViewModel> _newEpisodeGroups = [];
        private bool _isLoadingNewReleases;

        /// <summary>
        /// Neue, ungehörte Episoden favorisierter Serien, gruppiert nach Monat.
        /// <see cref="ObservableCollection{T}"/>, damit das ListView mit <c>CanReorderItems</c>
        /// die Sammlung direkt per Drag &amp; Drop umsortieren könnte.
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
                    OnPropertyChanged(nameof(NewReleasesLoadingVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit der Neuerscheinungs-Gruppen-Liste – sichtbar sobald mindestens
        /// eine Gruppe mit Episoden vorhanden ist.
        /// </summary>
        public Visibility NewEpisodeGroupsVisibility =>
            _newEpisodeGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Gibt an, ob gerade Neuerscheinungen im Hintergrund geladen werden.
        /// Steuert den Lade-Hinweis im Abschnitt, damit der Nutzer weiß, dass die Daten
        /// abgerufen werden (dauert bei vielen Serien mehrere Minuten).
        /// </summary>
        public bool IsLoadingNewReleases
        {
            get => _isLoadingNewReleases;
            set
            {
                if (SetProperty(ref _isLoadingNewReleases, value))
                {
                    OnPropertyChanged(nameof(NewReleasesLoadingVisibility));
                    OnPropertyChanged(nameof(NewReleasesSectionVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Lade-Hinweises – sichtbar solange die Abfrage läuft und noch keine
        /// Einträge vorliegen.
        /// </summary>
        public Visibility NewReleasesLoadingVisibility =>
            _isLoadingNewReleases && _newEpisodeGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Sichtbarkeit des gesamten Neuerscheinungs-Abschnitts (Überschrift und Inhalt).
        /// Sichtbar wenn Daten vorhanden sind ODER gerade geladen wird.
        /// </summary>
        public Visibility NewReleasesSectionVisibility =>
            _isLoadingNewReleases || _newEpisodeGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Ersetzt die Neuerscheinungs-Gruppen mit der übergebenen Liste.</summary>
        /// <param name="groups">Die neuen Gruppen, bereits nach Monat sortiert.</param>
        public void SetGroups(IReadOnlyList<NewEpisodesGroupViewModel> groups)
        {
            NewEpisodeGroups = new ObservableCollection<NewEpisodesGroupViewModel>(groups);
        }
    }
}

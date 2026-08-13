using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.Core.Abstractions;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Folgenliste einer Serienansicht: welcher Reiter gilt, welcher Status-Filter,
    /// welcher Suchbegriff und welche Sortierung — und was davon übrig bleibt.
    /// </summary>
    /// <remarks>
    /// Die vier Kriterien wirken zusammen und hängen alle an derselben Neuberechnung.
    /// Deshalb stehen sie beieinander und nicht verteilt im Ansichtsmodell der Seite: Dort
    /// führte jede einzelne Eigenschaft ihre eigene Meldung, und die Reihenfolge, in der
    /// sie greifen, war nur noch aus dem Ablauf zu erschließen.
    /// </remarks>
    public sealed class SeriesEpisodeList : ObservableObject
    {
        /// <summary>Reiter-Nummer der Sonderfolgen.</summary>
        private const int SpecialEpisodesTab = 1;

        private IReadOnlyList<EpisodeTileViewModel> _allEpisodes = [];
        private IReadOnlyList<EpisodeTileViewModel> _episodes = [];
        private EpisodeSortOrder _sortOrder = EpisodeSortOrder.EpisodeNumber;
        private int _episodeFilterIndex;
        private string _episodeSearchText = string.Empty;
        private int _episodeTabIndex;

        /// <summary>Die Folgen, die nach Reiter, Filter und Suche übrig bleiben — sortiert.</summary>
        public IReadOnlyList<EpisodeTileViewModel> Episodes
        {
            get => _episodes;
            private set => SetProperty(ref _episodes, value);
        }

        /// <summary>
        /// Sortierkriterium der Folgenliste. Jede Änderung sortiert sofort neu.
        /// </summary>
        public EpisodeSortOrder SortOrder
        {
            get => _sortOrder;
            set
            {
                if (SetProperty(ref _sortOrder, value))
                {
                    Refresh();
                }
            }
        }

        /// <summary>
        /// Aktueller Status-Filter (0 = Alle, 1 = Ungehört, 2 = Gehört, 3 = Angefangen).
        /// Eine Änderung filtert und sortiert sofort neu.
        /// </summary>
        public int EpisodeFilterIndex
        {
            get => _episodeFilterIndex;
            set
            {
                if (SetProperty(ref _episodeFilterIndex, value))
                {
                    Refresh();
                }
            }
        }

        /// <summary>
        /// Freitextsuche über die Folgentitel. Wirkt beim Tippen und zusätzlich zum
        /// Status-Filter; leerer Text zeigt wieder alle Folgen des Reiters.
        /// </summary>
        public string EpisodeSearchText
        {
            get => _episodeSearchText;
            set
            {
                if (SetProperty(ref _episodeSearchText, value))
                {
                    Refresh();
                }
            }
        }

        /// <summary>Aktiver Reiter: 0 = Folgen, 1 = Sonderfolgen.</summary>
        public int EpisodeTabIndex
        {
            get => _episodeTabIndex;
            set
            {
                if (SetProperty(ref _episodeTabIndex, value))
                {
                    Refresh();
                }
            }
        }

        /// <summary>Ob Suche oder Status-Filter die Folgenliste gerade einschränken.</summary>
        public bool HasActiveFilter =>
            !string.IsNullOrWhiteSpace(_episodeSearchText) || _episodeFilterIndex != 0;

        /// <summary>
        /// Sichtbarkeit des „Nichts gefunden"-Hinweises — die Serie hat Folgen, aber Suche
        /// oder Filter lassen keine übrig.
        /// </summary>
        public Visibility NoResultsVisibility =>
            _allEpisodes.Count > 0 && _episodes.Count == 0 && HasActiveFilter
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>Ob die Serie Sonderfolgen hat — nur dann erscheint deren Reiter.</summary>
        public bool HasSpecialEpisodes => _allEpisodes.Any(e => e.IsSpecialEpisode);

        /// <summary>Anzahl der Sonderfolgen für die Beschriftung des Reiters.</summary>
        public int SpecialEpisodeCount => _allEpisodes.Count(e => e.IsSpecialEpisode);

        /// <summary>
        /// Übernimmt den Bestand einer neu geladenen Serie. Reiter und Status-Filter fallen
        /// dabei auf ihren Ausgangswert zurück — sonst zeigte eine frisch geöffnete Serie
        /// die Einschränkung der vorigen.
        /// </summary>
        /// <param name="episodes">Alle Folgen der Serie, Sonderfolgen eingeschlossen.</param>
        public void SetSource(IReadOnlyList<EpisodeTileViewModel> episodes)
        {
            _allEpisodes = episodes ?? [];
            _episodeTabIndex = 0;
            _episodeFilterIndex = 0;

            OnPropertyChanged(nameof(EpisodeTabIndex));
            OnPropertyChanged(nameof(EpisodeFilterIndex));
            OnPropertyChanged(nameof(HasSpecialEpisodes));
            OnPropertyChanged(nameof(SpecialEpisodeCount));

            Refresh();
        }

        /// <summary>Nimmt Suche und Status-Filter zurück.</summary>
        public void ResetFilters()
        {
            _episodeSearchText = string.Empty;
            _episodeFilterIndex = 0;

            OnPropertyChanged(nameof(EpisodeSearchText));
            OnPropertyChanged(nameof(EpisodeFilterIndex));

            Refresh();
        }

        /// <summary>
        /// Wendet Reiter, Status-Filter, Suche und Sortierung in dieser Reihenfolge an.
        /// </summary>
        private void Refresh()
        {
            IEnumerable<EpisodeTileViewModel> remaining = FilterByTab(_allEpisodes);
            remaining = FilterByStatus(remaining);
            remaining = FilterBySearch(remaining);

            Episodes = [.. Sort(remaining)];

            OnPropertyChanged(nameof(HasActiveFilter));
            OnPropertyChanged(nameof(NoResultsVisibility));
        }

        /// <summary>Trennt reguläre Folgen von Sonderfolgen.</summary>
        private IEnumerable<EpisodeTileViewModel> FilterByTab(IEnumerable<EpisodeTileViewModel> episodes)
            => _episodeTabIndex == SpecialEpisodesTab
                ? episodes.Where(e => e.IsSpecialEpisode)
                : episodes.Where(e => !e.IsSpecialEpisode);

        /// <summary>
        /// Wendet den Status-Filter an.
        /// </summary>
        /// <remarks>
        /// „Angefangen" fragt nach der offenen Stelle, nicht nach dem Hörstatus: Eine
        /// bereits gehörte Folge, die erneut begonnen wurde, gehört dorthin — sonst gibt es
        /// keinen Weg zurück in die laufende Wiedergabe. Sie bleibt dabei auch unter
        /// „Durchgehört" stehen, denn gehört hat man sie.
        /// </remarks>
        private IEnumerable<EpisodeTileViewModel> FilterByStatus(IEnumerable<EpisodeTileViewModel> episodes)
            => _episodeFilterIndex switch
            {
                1 => episodes.Where(e => e.Progress == PlaybackStatus.NotStarted),
                2 => episodes.Where(e => e.Progress == PlaybackStatus.Finished),
                3 => episodes.Where(e => e.Progress == PlaybackStatus.InProgress || e.HasOpenPosition),
                _ => episodes
            };

        /// <summary>Wendet die Freitextsuche über die Folgentitel an.</summary>
        private IEnumerable<EpisodeTileViewModel> FilterBySearch(IEnumerable<EpisodeTileViewModel> episodes)
            => string.IsNullOrWhiteSpace(_episodeSearchText)
                ? episodes
                : episodes.Where(e => e.Title.Contains(_episodeSearchText, StringComparison.CurrentCultureIgnoreCase));

        /// <summary>
        /// Sortiert nach dem gewählten Kriterium. Folgen ohne Nummer beziehungsweise ohne
        /// Datum stehen dabei hinten, nicht vorn.
        /// </summary>
        private IEnumerable<EpisodeTileViewModel> Sort(IEnumerable<EpisodeTileViewModel> episodes)
            => _sortOrder switch
            {
                EpisodeSortOrder.Title => episodes.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase),
                EpisodeSortOrder.ReleaseDate => episodes
                    .OrderBy(e => e.ReleaseDate.HasValue ? 0 : 1)
                    .ThenBy(e => e.ReleaseDate),
                _ => episodes
                    .OrderBy(e => e.EpisodeNumber.HasValue ? 0 : 1)
                    .ThenBy(e => e.EpisodeNumber)
            };
    }
}

using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für die Serienliste der Online-Mediathek.
    /// Hält die gefilterte und sortierte Serienliste, die aktuelle Auswahl im Akkordeon
    /// und die Filter-Kriterien (Suchtext, Statusfilter, Sortierindex). Das Top-VM
    /// <see cref="MediathekOnlineViewModel"/> steuert über diese Properties, welche Serien
    /// sichtbar sind.
    /// </summary>
    public sealed class OnlineSeriesViewModel : ObservableObject
    {
        private List<SeriesCardViewModel> _allSeries = [];
        private IReadOnlyList<SeriesCardViewModel> _series = [];
        private string _searchText = string.Empty;
        private SeriesStatusFilter _statusFilter = SeriesStatusFilter.Alle;
        private int _seriesSortIndex;
        private int _selectedSeriesIndex = -1;

        /// <summary>
        /// Die aktuell sichtbaren Serien – gefiltert nach <see cref="SearchText"/> und
        /// <see cref="StatusFilter"/>, sortiert nach <see cref="SeriesSortIndex"/>.
        /// </summary>
        public IReadOnlyList<SeriesCardViewModel> Series
        {
            get => _series;
            private set
            {
                if (SetProperty(ref _series, value))
                {
                    OnPropertyChanged(nameof(HasFilteredSeries));
                }
            }
        }

        /// <summary>
        /// Zugriff auf die vollständige, ungefilterte Serienliste – wird vom Top-VM
        /// für Hintergrund-Cover-Downloads und für das Entfernen einzelner Serien gelesen.
        /// </summary>
        public IReadOnlyList<SeriesCardViewModel> AllSeries => _allSeries;

        /// <summary>Gibt an, ob nach dem Filter mindestens eine Serie übrig ist.</summary>
        public bool HasFilteredSeries => _series.Count > 0;

        /// <summary>Freitext-Suchfilter. Filtert die Bibliothek clientseitig.</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilter();
                }
            }
        }

        /// <summary>Filter nach Wiedergabefortschritt.</summary>
        public SeriesStatusFilter StatusFilter
        {
            get => _statusFilter;
            set
            {
                if (SetProperty(ref _statusFilter, value))
                {
                    ApplyFilter();
                }
            }
        }

        /// <summary>
        /// Index der Serien-Sortierung: 0 = Name A–Z, 1 = Meiste Folgen, 2 = Nach Fortschritt.
        /// </summary>
        public int SeriesSortIndex
        {
            get => _seriesSortIndex;
            set
            {
                if (SetProperty(ref _seriesSortIndex, value))
                {
                    ApplyFilter();
                }
            }
        }

        /// <summary>
        /// Index der gewählten Serie in <see cref="Series"/>. <c>-1</c> wenn keine Serie gewählt.
        /// Steuert über <see cref="EpisodesAccordionVisibility"/> die Sichtbarkeit des Folgenbereichs.
        /// </summary>
        public int SelectedSeriesIndex
        {
            get => _selectedSeriesIndex;
            private set
            {
                if (SetProperty(ref _selectedSeriesIndex, value))
                {
                    OnPropertyChanged(nameof(EpisodesAccordionVisibility));
                }
            }
        }

        /// <summary>Akkordeon sichtbar wenn eine Serie ausgewählt ist.</summary>
        public Visibility EpisodesAccordionVisibility =>
            _selectedSeriesIndex >= 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Ersetzt die vollständige Serienliste und wendet den aktuellen Filter neu an.
        /// Wird nach <c>LoadAsync</c> und nach dem Entfernen einer Serie aufgerufen.
        /// </summary>
        /// <param name="allSeries">Die komplette, ungefilterte Liste.</param>
        public void SetAllSeries(IReadOnlyList<SeriesCardViewModel> allSeries)
        {
            _allSeries = [.. allSeries];
            DeselectSeries();
            ApplyFilter();
        }

        /// <summary>
        /// Markiert die übergebene Serie als im Akkordeon gewählt und setzt den Index.
        /// Vorherige Auswahl wird zurückgesetzt, sodass immer nur eine Serie markiert ist.
        /// </summary>
        /// <param name="card">Die auszuwählende Serie aus der aktuellen Sicht.</param>
        public void SelectSeries(SeriesCardViewModel card)
        {
            foreach (SeriesCardViewModel c in _allSeries)
            {
                c.IsSelectedInAccordion = false;
            }

            card.IsSelectedInAccordion = true;

            // Index in der gefilterten Liste finden
            int idx = -1;
            for (int i = 0; i < _series.Count; i++)
            {
                if (ReferenceEquals(_series[i], card))
                {
                    idx = i;
                    break;
                }
            }

            SelectedSeriesIndex = idx;
        }

        /// <summary>
        /// Hebt die Auswahl im Akkordeon auf und setzt das <c>IsSelectedInAccordion</c>-Flag
        /// bei allen Serien zurück.
        /// </summary>
        public void DeselectSeries()
        {
            foreach (SeriesCardViewModel c in _allSeries)
            {
                c.IsSelectedInAccordion = false;
            }

            SelectedSeriesIndex = -1;
        }

        /// <summary>
        /// Entfernt die Serie mit der angegebenen ID aus der internen Liste und wendet
        /// den Filter neu an. Die DB-Löschung übernimmt das Top-VM.
        /// </summary>
        /// <param name="seriesId">Die zu entfernende Serien-ID.</param>
        public void RemoveSeries(Guid seriesId)
        {
            List<SeriesCardViewModel> updated = new(_allSeries.Count);
            foreach (SeriesCardViewModel c in _allSeries)
            {
                if (c.Id != seriesId)
                {
                    updated.Add(c);
                }
            }

            _allSeries = updated;
            DeselectSeries();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            List<SeriesCardViewModel> filtered = [];

            foreach (SeriesCardViewModel card in _allSeries)
            {
                if (!MatchesSearchText(card))
                {
                    continue;
                }

                if (!MatchesStatusFilter(card))
                {
                    continue;
                }

                filtered.Add(card);
            }

            // Serien-Sortierung anwenden
            IEnumerable<SeriesCardViewModel> sorted = _seriesSortIndex switch
            {
                1 => filtered.OrderByDescending(c => c.TotalEpisodeCount),
                2 => filtered.OrderBy(c => c.FinishedCount).ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
                _ => filtered.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            };

            Series = [.. sorted];
        }

        private bool MatchesSearchText(SeriesCardViewModel card)
        {
            return string.IsNullOrWhiteSpace(_searchText)
                || card.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
        }

        private bool MatchesStatusFilter(SeriesCardViewModel card)
        {
            return _statusFilter switch
            {
                SeriesStatusFilter.Neu      => card.HasNewEpisodes,
                SeriesStatusFilter.AmHoeren => card.HasInProgressEpisodes,
                SeriesStatusFilter.Gehört   => card.AllEpisodesFinished,
                _                           => true
            };
        }
    }
}

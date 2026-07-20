using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für die Episoden-Sektion der Online-Mediathek.
    /// Hält die Episodenliste der gewählten Serie, das Sortierkriterium und den
    /// Lade-Zustand. Die Async-Logik zum Laden und Nachladen der Cover liegt im Top-VM
    /// bzw. im <see cref="MediathekOnlineActions"/>-Helfer; dieses Sub-VM ist ein
    /// reiner Daten- und Sortier-Halter.
    /// </summary>
    public sealed class OnlineEpisodesViewModel : ObservableObject
    {
        private List<OnlineEpisodeCardViewModel> _allEpisodes = [];
        private IReadOnlyList<OnlineEpisodeCardViewModel> _episodes = [];
        private int _episodeSortIndex;
        private bool _isLoadingEpisodes;

        /// <summary>
        /// Die nach <see cref="EpisodeSortIndex"/> sortierten Episoden der aktuellen Serie.
        /// </summary>
        public IReadOnlyList<OnlineEpisodeCardViewModel> Episodes
        {
            get => _episodes;
            private set => SetProperty(ref _episodes, value);
        }

        /// <summary>
        /// Index der Episoden-Sortierung: 0 = Titel A–Z, 1 = Titel Z–A, 2 = Neueste zuerst.
        /// </summary>
        public int EpisodeSortIndex
        {
            get => _episodeSortIndex;
            set
            {
                if (SetProperty(ref _episodeSortIndex, value))
                {
                    ApplySort();
                }
            }
        }

        /// <summary>
        /// Gibt an, ob gerade Episoden einer Serie geladen werden.
        /// Steuert den ProgressRing im Akkordeon-Bereich.
        /// </summary>
        public bool IsLoadingEpisodes
        {
            get => _isLoadingEpisodes;
            set
            {
                if (SetProperty(ref _isLoadingEpisodes, value))
                {
                    OnPropertyChanged(nameof(LoadingEpisodesVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Lade-Indikators im Akkordeon-Bereich.</summary>
        public Visibility LoadingEpisodesVisibility =>
            _isLoadingEpisodes ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Ersetzt die Episodenliste komplett und wendet die aktuelle Sortierung an.
        /// Setzt <see cref="EpisodeSortIndex"/> auf 0 zurück, damit nach dem Serienwechsel
        /// wieder die Standard-Sortierung aktiv ist.
        /// </summary>
        /// <param name="episodes">Die neuen Episoden-Kacheln.</param>
        public void SetEpisodes(IReadOnlyList<OnlineEpisodeCardViewModel> episodes)
        {
            _allEpisodes = [.. episodes];
            _episodeSortIndex = 0;
            OnPropertyChanged(nameof(EpisodeSortIndex));
            ApplySort();
        }

        /// <summary>Leert die Episodenliste – z.B. beim Deselect oder Serienwechsel.</summary>
        public void Clear()
        {
            _allEpisodes = [];
            Episodes = [];
        }

        /// <summary>
        /// Zugriff auf die Roh-Liste (alle Episoden ohne Sortierung), damit das Top-VM
        /// nach dem Hintergrund-Download neue Cover nachtragen kann.
        /// </summary>
        public IReadOnlyList<OnlineEpisodeCardViewModel> AllEpisodes => _allEpisodes;

        /// <summary>
        /// Sortiert die Episoden nach dem gewählten Kriterium. Wird automatisch nach
        /// Änderungen an <see cref="EpisodeSortIndex"/> und nach <see cref="SetEpisodes"/> aufgerufen.
        /// </summary>
        private void ApplySort()
        {
            IEnumerable<OnlineEpisodeCardViewModel> sorted = _episodeSortIndex switch
            {
                1 => _allEpisodes.OrderByDescending(e => e.Title, StringComparer.OrdinalIgnoreCase),
                2 => _allEpisodes.OrderByDescending(e => e.ReleaseDate ?? DateTime.MinValue),
                _ => _allEpisodes.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            };

            Episodes = [.. sorted];
        }
    }
}

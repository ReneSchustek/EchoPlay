using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Gemeinsame Basis für Serien-Kachel-ViewModels (Online wie Lokal). Kapselt die zwischen
    /// beiden identische Property-Fläche für Favorit, Neuerscheinungs-Überwachung und
    /// Akkordeon-Auswahl samt zugehöriger Glyph-/Sichtbarkeits-Ableitungen. Erbt das Cover
    /// von <see cref="CoverCardViewModelBase"/>.
    /// </summary>
    public abstract class SeriesTileViewModelBase : CoverCardViewModelBase, IAccordionSelectable
    {
        private bool _isFavorite;
        private bool _isWatched;
        private bool _isSelectedInAccordion;

        /// <summary>Gibt an, ob die Serie als Favorit markiert ist.</summary>
        public bool IsFavorite
        {
            get => _isFavorite;
            protected set
            {
                if (SetProperty(ref _isFavorite, value))
                {
                    OnPropertyChanged(nameof(FavoriteGlyph));
                }
            }
        }

        /// <summary>Segoe-Fluent-Glyph für den Favoriten-Stern (gefüllt/leer).</summary>
        public string FavoriteGlyph => _isFavorite ? "" : "";

        /// <summary>Gibt an, ob die Serie auf Neuerscheinungen überwacht wird.</summary>
        public bool IsWatched
        {
            get => _isWatched;
            set
            {
                if (SetProperty(ref _isWatched, value))
                {
                    OnPropertyChanged(nameof(WatchedGlyph));
                    OnPropertyChanged(nameof(WatchedVisibility));
                }
            }
        }

        /// <summary>Segoe-Fluent-Glyph für das Überwachungs-Icon.</summary>
        public string WatchedGlyph => "";

        /// <summary>Sichtbarkeit des Überwachungs-Icons.</summary>
        public Visibility WatchedVisibility =>
            _isWatched ? Visibility.Visible : Visibility.Collapsed;

        /// <inheritdoc/>
        public bool IsSelectedInAccordion
        {
            get => _isSelectedInAccordion;
            set
            {
                if (SetProperty(ref _isSelectedInAccordion, value))
                {
                    OnPropertyChanged(nameof(SelectedIndicatorVisibility));
                }
            }
        }

        /// <inheritdoc/>
        public Visibility SelectedIndicatorVisibility =>
            _isSelectedInAccordion ? Visibility.Visible : Visibility.Collapsed;
    }
}

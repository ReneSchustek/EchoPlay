using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für die Provider-Suche der Online-Mediathek.
    /// Hält die Treffer aus Spotify/Apple-Music-Suchen, das Such-Flag und den Suchtyp-Filter
    /// (Alle/Serien/Folgen). Die eigentliche Suche koordiniert das Top-VM, weil sie den
    /// <c>ImportService</c> und Fehler-Dialoge braucht.
    /// </summary>
    public sealed class OnlineProviderSearchViewModel : ObservableObject
    {
        private IReadOnlyList<SearchResultViewModel> _providerSearchResults = [];
        private bool _isSearchingProvider;
        private int _searchTypeIndex;

        /// <summary>
        /// Die Suchergebnisse vom Provider. Leer solange keine Suche durchgeführt wurde
        /// oder nach einem Reset.
        /// </summary>
        public IReadOnlyList<SearchResultViewModel> ProviderSearchResults
        {
            get => _providerSearchResults;
            set
            {
                if (SetProperty(ref _providerSearchResults, value))
                {
                    OnPropertyChanged(nameof(ProviderSearchResultsVisibility));
                    OnPropertyChanged(nameof(HasResults));
                }
            }
        }

        /// <summary>Gibt an, ob gerade eine Provider-Suche läuft.</summary>
        public bool IsSearchingProvider
        {
            get => _isSearchingProvider;
            set => SetProperty(ref _isSearchingProvider, value);
        }

        /// <summary>
        /// Suchtyp-Index: 0 = Alle, 1 = nur Serien, 2 = nur Folgen. Steuert ob
        /// <c>SearchAsync</c> und/oder <c>SearchAlbumsAsync</c> aufgerufen wird.
        /// </summary>
        public int SearchTypeIndex
        {
            get => _searchTypeIndex;
            set => SetProperty(ref _searchTypeIndex, value);
        }

        /// <summary>Sichtbarkeit der Provider-Suchergebnisliste.</summary>
        public Visibility ProviderSearchResultsVisibility =>
            _providerSearchResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Gibt an, ob Suchergebnisse vorliegen – wird von den Top-VM-Visibilities gelesen.</summary>
        public bool HasResults => _providerSearchResults.Count > 0;

        /// <summary>Leert die Suchergebnisse – z.B. nach einem Import oder beim Clear des Suchfelds.</summary>
        public void ClearResults()
        {
            ProviderSearchResults = [];
        }
    }
}

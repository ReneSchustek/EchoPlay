using EchoPlay.Core.Models.Import;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-Actions: Provider-Suche (Serie und Album), Import-Status-Check und
    /// Batch-Import der angehakten Treffer.
    /// </summary>
    internal sealed class OnlineProviderSearchActions
    {
        private readonly MediathekOnlineActionsContext _ctx;
        private readonly OnlineSeriesViewModel _seriesVM;
        private readonly OnlineEpisodesViewModel _episodesVM;
        private readonly OnlineProviderSearchViewModel _providerSearchVM;
        private readonly Func<Task> _reloadAfterImportAsync;

        /// <summary>Public Call-Counter für Tests.</summary>
        public int SearchCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int AddSelectedCallCount { get; private set; }

        public OnlineProviderSearchActions(
            MediathekOnlineActionsContext context,
            OnlineSeriesViewModel seriesVM,
            OnlineEpisodesViewModel episodesVM,
            OnlineProviderSearchViewModel providerSearchVM,
            Func<Task> reloadAfterImportAsync)
        {
            _ctx = context;
            _seriesVM = seriesVM;
            _episodesVM = episodesVM;
            _providerSearchVM = providerSearchVM;
            _reloadAfterImportAsync = reloadAfterImportAsync;
        }

        /// <summary>
        /// Startet eine Provider-Suche und befüllt das <see cref="OnlineProviderSearchViewModel"/>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Provider-Suche in der Online-Mediathek: HTTP-/Parser-/Timeout-Fehler aus Spotify/AppleMusic werden als Nutzer-Fehlermeldung angezeigt, damit der Suche-Command nicht reisst.")]
        public async Task SearchProviderAsync(string searchText)
        {
            SearchCallCount++;

            if (_providerSearchVM.IsSearchingProvider || string.IsNullOrWhiteSpace(searchText))
            {
                return;
            }

            // Offenes Akkordeon schließen – sonst überlagern sich Suchergebnisse und Folgen-Panel
            _seriesVM.DeselectSeries();
            _episodesVM.Clear();

            _providerSearchVM.IsSearchingProvider = true;
            _providerSearchVM.ProviderSearchResults = [];

            try
            {
                int searchTypeIndex = _providerSearchVM.SearchTypeIndex;

                IReadOnlyList<ImportSeries> seriesResults = searchTypeIndex == 2
                    ? []
                    : await _ctx.ImportService.SearchAsync(searchText);
                IReadOnlyList<ImportSeries> albumResults = searchTypeIndex == 1
                    ? []
                    : await _ctx.ImportService.SearchAlbumsAsync(searchText);

                string searchLower = searchText.ToUpperInvariant();
                List<ImportSeries> combined = new(seriesResults.Count + albumResults.Count);
                combined.AddRange(seriesResults);
                combined.AddRange(albumResults);
                combined.Sort((a, b) =>
                {
                    bool aContains = a.Title.Contains(searchLower, StringComparison.OrdinalIgnoreCase)
                                  || (a.ArtistName?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false);
                    bool bContains = b.Title.Contains(searchLower, StringComparison.OrdinalIgnoreCase)
                                  || (b.ArtistName?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false);

                    if (aContains != bContains)
                    {
                        return aContains ? -1 : 1;
                    }

                    return b.Score.CompareTo(a.Score);
                });

                List<SearchResultViewModel> viewModels = new(combined.Count);
                foreach (ImportSeries series in combined)
                {
                    bool alreadyImported = await _ctx.ImportService.IsAlreadyImportedAsync(series);
                    viewModels.Add(new SearchResultViewModel(
                        series, alreadyImported, _ctx.ImportService, _ctx.ErrorDialogService,
                        _ctx.LocalizationService, _ctx.CoverBrightnessAnalyzer,
                        onImportCompleted: _reloadAfterImportAsync));
                }

                _providerSearchVM.ProviderSearchResults = viewModels;
            }
            catch (Exception ex)
            {
                await _ctx.ErrorDialogService.ShowAsync(
                    _ctx.LocalizationService.Get("OnlineSearchFailedTitle"), ex.Message);
            }
            finally
            {
                _providerSearchVM.IsSearchingProvider = false;
            }
        }

        /// <summary>
        /// Importiert alle angehakten Suchergebnisse nacheinander. Bereits importierte
        /// Einträge werden übersprungen.
        /// </summary>
        public void AddSelected()
        {
            AddSelectedCallCount++;

            List<SearchResultViewModel> selected = [];
            foreach (SearchResultViewModel result in _providerSearchVM.ProviderSearchResults)
            {
                if (result.IsSelected && !result.IsImported)
                {
                    selected.Add(result);
                }
            }

            if (selected.Count == 0)
            {
                return;
            }

            foreach (SearchResultViewModel result in selected)
            {
                if (result.ImportCommand.CanExecute(null))
                {
                    result.ImportCommand.Execute(null);
                }
            }
        }
    }
}

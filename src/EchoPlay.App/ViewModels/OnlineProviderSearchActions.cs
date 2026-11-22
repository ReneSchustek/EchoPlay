using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-Actions: Provider-Suche (Serie und Album), Import-Status-Check und
    /// Batch-Import der angehakten Treffer.
    /// </summary>
    internal sealed class OnlineProviderSearchActions : IDisposable
    {
        private readonly OnlineLibraryActionsContext _ctx;
        private readonly OnlineSeriesViewModel _seriesVM;
        private readonly OnlineEpisodesViewModel _episodesVM;
        private readonly OnlineProviderSearchViewModel _providerSearchVM;
        private readonly Func<Task> _reloadAfterImportAsync;

        // Pro Suche neu erzeugt; Cover-Loads der vorigen Trefferliste werden hier abgebrochen,
        // damit kein verwaister HTTP-Verkehr im Hintergrund weiterläuft.
        private CancellationTokenSource? _searchCoversCts;

        /// <summary>Public Call-Counter für Tests.</summary>
        public int SearchCallCount { get; private set; }

        /// <summary>Public Call-Counter für Tests.</summary>
        public int AddSelectedCallCount { get; private set; }

        /// <summary>Richtet die Anbietersuche ein.</summary>
        /// <param name="context">Die Dienste der Online-Mediathek.</param>
        /// <param name="seriesVM">Die Serienliste; die Suche schließt ihre offene Serie.</param>
        /// <param name="episodesVM">Die Folgen der gewählten Serie.</param>
        /// <param name="providerSearchVM">Nimmt die Treffer auf.</param>
        /// <param name="reloadAfterImportAsync">Lädt die Mediathek nach einem Import neu.</param>
        public OnlineProviderSearchActions(
            OnlineLibraryActionsContext context,
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
        ///
        /// Reset-/Abbruch-Disziplin: Trefferliste und Status-Hinweise werden noch
        /// vor dem ersten <c>await</c> geleert. <see cref="StartNewCoverScope"/> macht
        /// gleichzeitig eine eventuell laufende Vorgänger-Suche obsolet. Nach jedem Await
        /// prüft der Code <see cref="CancellationToken.IsCancellationRequested"/> und verwirft
        /// Treffer der veralteten Suche.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Provider-Suche in der Online-Mediathek: HTTP-/Parser-/Timeout-Fehler aus Spotify/AppleMusic werden als Nutzer-Fehlermeldung angezeigt, damit der Suche-Command nicht reißt.")]
        public async Task SearchProviderAsync(string searchText)
        {
            SearchCallCount++;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                return;
            }

            // Vorherige Cover-Loads abbrechen UND ältere Provider-Suche obsolet machen,
            // denn beide nutzen denselben Token-Lebenszyklus.
            ResetForSearch();
            CancellationToken coverToken = StartNewCoverScope();

            try
            {
                await QueryAndShowAsync(searchText, coverToken);
            }
            catch (Exception ex)
            {
                // Obsolete Suche: Fehler nicht mehr anzeigen, die neue Suche bestimmt den Status.
                if (coverToken.IsCancellationRequested) return;

                await _ctx.ErrorDialogService.ShowAsync(
                    _ctx.LocalizationService.Get("OnlineSearchFailedTitle"), ex.Message, DialogKey.OnlineSearchFailed);
            }
            finally
            {
                // Loader nur zurücksetzen, wenn diese Suche noch die aktuelle ist,
                // sonst flackert der Spinner beim Back-to-Back-Wechsel zwischen den Suchen.
                if (!coverToken.IsCancellationRequested)
                {
                    _providerSearchVM.IsSearchingProvider = false;
                }
            }
        }

        /// <summary>
        /// Fragt die Anbieter und zeigt die Treffer; eine überholte Suche liefert nichts und
        /// lässt die Liste der neuen.
        /// </summary>
        private async Task QueryAndShowAsync(string searchText, CancellationToken coverToken)
        {
            List<ImportSeries>? combined = await QueryAsync(searchText, coverToken);
            List<SearchResultViewModel>? viewModels = combined is null ? null : await BuildResultsAsync(combined, coverToken);
            if (viewModels is not null)
            {
                _providerSearchVM.ProviderSearchResults = viewModels;
            }
        }

        /// <summary>
        /// Leert Liste und Hinweise vor dem ersten <c>await</c>. Ein offenes Akkordeon wird
        /// geschlossen, sonst überlagern sich Suchergebnisse und Folgen-Panel.
        /// </summary>
        private void ResetForSearch()
        {
            _seriesVM.DeselectSeries();
            _episodesVM.Clear();

            _providerSearchVM.IsSearchingProvider = true;
            _providerSearchVM.ProviderSearchResults = [];
            _providerSearchVM.IsSpotifyFallbackHintVisible = false;
        }

        /// <summary>
        /// Fragt Serien und Alben ab, je nach gewählter Suchart, und ordnet die Treffer.
        /// </summary>
        /// <returns>Die geordneten Treffer, oder leer, wenn eine neuere Suche diese abgelöst hat.</returns>
        private async Task<List<ImportSeries>?> QueryAsync(string searchText, CancellationToken coverToken)
        {
            int searchTypeIndex = _providerSearchVM.SearchTypeIndex;

            SearchOutcome seriesOutcome = searchTypeIndex == 2
                ? new SearchOutcome([], SpotifyFallbackApplied: false)
                : await _ctx.ImportService.SearchAsync(searchText, coverToken);
            if (coverToken.IsCancellationRequested) return null;

            SearchOutcome albumsOutcome = searchTypeIndex == 1
                ? new SearchOutcome([], SpotifyFallbackApplied: false)
                : await _ctx.ImportService.SearchAlbumsAsync(searchText, coverToken);
            if (coverToken.IsCancellationRequested) return null;

            _providerSearchVM.IsSpotifyFallbackHintVisible =
                seriesOutcome.SpotifyFallbackApplied || albumsOutcome.SpotifyFallbackApplied;

            List<ImportSeries> combined = [.. seriesOutcome.Results, .. albumsOutcome.Results];

            // Dieselbe Rangfolge wie in der Suche: Namenstreffer zuerst, dann die Bewertung.
            combined.Sort((a, b) =>
            {
                SearchResultRank rankA = SearchResultRank.Create(a, searchText);
                SearchResultRank rankB = SearchResultRank.Create(b, searchText);
                return rankA.RanksBefore(rankB) ? -1 : rankB.RanksBefore(rankA) ? 1 : 0;
            });
            return combined;
        }

        /// <summary>Baut die Trefferzeilen samt „schon importiert“.</summary>
        /// <returns>Die Zeilen, oder leer, wenn eine neuere Suche diese abgelöst hat.</returns>
        private async Task<List<SearchResultViewModel>?> BuildResultsAsync(List<ImportSeries> combined, CancellationToken coverToken)
        {
            List<SearchResultViewModel> viewModels = new(combined.Count);
            foreach (ImportSeries series in combined)
            {
                bool alreadyImported = await _ctx.ImportService.IsAlreadyImportedAsync(series, coverToken);
                if (coverToken.IsCancellationRequested) return null;

                viewModels.Add(new SearchResultViewModel(
                    series, alreadyImported, _ctx.ImportService, _ctx.ErrorDialogService,
                    _ctx.LocalizationService, _ctx.BackgroundCoverService,
                    onImportCompleted: _reloadAfterImportAsync,
                    cancellationToken: coverToken));
            }

            return viewModels;
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

        /// <summary>
        /// Beendet den vorherigen Cover-Scope (Cancel + Dispose) und liefert das Token
        /// einer frischen <see cref="CancellationTokenSource"/> für die neue Suche.
        /// </summary>
        private CancellationToken StartNewCoverScope()
        {
            CancelPendingSearchCovers();
            _searchCoversCts = new CancellationTokenSource();
            return _searchCoversCts.Token;
        }

        /// <summary>
        /// Bricht alle laufenden Cover-Loads der aktuellen Trefferliste ab.
        /// Wird beim Dispose und vor dem Start einer neuen Suche aufgerufen.
        /// </summary>
        public void CancelPendingSearchCovers()
        {
            CancellationTokenSource? old = _searchCoversCts;
            _searchCoversCts = null;
            if (old is null) return;

            try { old.Cancel(); }
            catch (ObjectDisposedException)
            {
                // CTS war bereits disposed; Cancel ist dann ein No-Op, der weiter unten folgende
                // Dispose-Aufruf ist idempotent. Bewusster Schluck ohne Logging.
            }
            old.Dispose();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            CancelPendingSearchCovers();
        }
    }
}

using EchoPlay.Core.Models.Import;
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
        private readonly MediathekOnlineActionsContext _ctx;
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
        ///
        /// Reset-/Abbruch-Disziplin: Trefferliste und Status-Hinweise werden noch
        /// vor dem ersten <c>await</c> geleert. <see cref="StartNewCoverScope"/> macht
        /// gleichzeitig eine eventuell laufende Vorgänger-Suche obsolet – nach jedem Await
        /// prüfen wir auf <see cref="CancellationToken.IsCancellationRequested"/> und
        /// verwerfen Treffer der veralteten Suche.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Provider-Suche in der Online-Mediathek: HTTP-/Parser-/Timeout-Fehler aus Spotify/AppleMusic werden als Nutzer-Fehlermeldung angezeigt, damit der Suche-Command nicht reißt.")]
        public async Task SearchProviderAsync(string searchText)
        {
            SearchCallCount++;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                return;
            }

            // Offenes Akkordeon schließen – sonst überlagern sich Suchergebnisse und Folgen-Panel
            _seriesVM.DeselectSeries();
            _episodesVM.Clear();

            _providerSearchVM.IsSearchingProvider = true;
            _providerSearchVM.ProviderSearchResults = [];
            _providerSearchVM.IsSpotifyFallbackHintVisible = false;

            // Vorherige Cover-Loads abbrechen UND ältere Provider-Suche obsolet machen –
            // beide nutzen denselben Token-Lebenszyklus.
            CancellationToken coverToken = StartNewCoverScope();

            try
            {
                int searchTypeIndex = _providerSearchVM.SearchTypeIndex;

                SearchOutcome seriesOutcome = searchTypeIndex == 2
                    ? new SearchOutcome([], SpotifyFallbackApplied: false)
                    : await _ctx.ImportService.SearchAsync(searchText);
                if (coverToken.IsCancellationRequested) return;

                SearchOutcome albumsOutcome = searchTypeIndex == 1
                    ? new SearchOutcome([], SpotifyFallbackApplied: false)
                    : await _ctx.ImportService.SearchAlbumsAsync(searchText);
                if (coverToken.IsCancellationRequested) return;

                _providerSearchVM.IsSpotifyFallbackHintVisible =
                    seriesOutcome.SpotifyFallbackApplied || albumsOutcome.SpotifyFallbackApplied;

                IReadOnlyList<ImportSeries> seriesResults = seriesOutcome.Results;
                IReadOnlyList<ImportSeries> albumResults = albumsOutcome.Results;

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
                    if (coverToken.IsCancellationRequested) return;

                    viewModels.Add(new SearchResultViewModel(
                        series, alreadyImported, _ctx.ImportService, _ctx.ErrorDialogService,
                        _ctx.LocalizationService, _ctx.BackgroundCoverService,
                        onImportCompleted: _reloadAfterImportAsync,
                        cancellationToken: coverToken));
                }

                _providerSearchVM.ProviderSearchResults = viewModels;
            }
            catch (Exception ex)
            {
                // Obsolete Suche: Fehler nicht mehr anzeigen – die neue Suche bestimmt den Status.
                if (coverToken.IsCancellationRequested) return;

                await _ctx.ErrorDialogService.ShowAsync(
                    _ctx.LocalizationService.Get("OnlineSearchFailedTitle"), ex.Message);
            }
            finally
            {
                // Loader nur zurücksetzen, wenn diese Suche noch die aktuelle ist –
                // sonst flackert der Spinner beim Back-to-Back-Wechsel zwischen den Suchen.
                if (!coverToken.IsCancellationRequested)
                {
                    _providerSearchVM.IsSearchingProvider = false;
                }
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
                // CTS war bereits disposed – Cancel ist dann ein No-Op, der weiter unten folgende
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

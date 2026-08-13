using EchoPlay.App.Services;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Search;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Führt eine einzelne Suche aus: fragt den Anbieter, durchsucht den eigenen Bestand,
    /// führt beides zusammen und baut die Trefferkarten.
    /// </summary>
    /// <remarks>
    /// Der Ablauf stand vorher im Ansichtsmodell und machte gut ein Drittel davon aus. Die
    /// Trennung ist die zwischen „was gesucht wird" und „was die Seite anzeigt": Das
    /// Ansichtsmodell hält Eingabe, Zustand und Liste, dieser Typ die Durchführung.
    /// </remarks>
    internal sealed class SearchRunner
    {
        private readonly ImportService _importService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly ILocalizationService _localizationService;
        private readonly IServiceScopeFactory? _scopeFactory;
        private readonly BackgroundCoverService? _backgroundCoverService;
        private readonly SearchViewModel _parent;

        /// <summary>
        /// Richtet die Durchführung auf die Dienste des Ansichtsmodells ein.
        /// </summary>
        /// <param name="importService">Sucht beim Anbieter und kennt den Import-Stand.</param>
        /// <param name="errorDialogService">Wird an die Trefferkarten weitergereicht.</param>
        /// <param name="localizationService">Wird an die Trefferkarten weitergereicht.</param>
        /// <param name="scopeFactory">
        /// Für die Suche im eigenen Bestand. Ohne sie bleibt der lokale Zweig leer — so
        /// laufen Tests ohne Datenbank.
        /// </param>
        /// <param name="backgroundCoverService">Lädt die Cover der Trefferkarten nach.</param>
        /// <param name="parent">
        /// Das Ansichtsmodell, an dem die Karten hängen. Sie melden ihm einen erfolgreichen
        /// Import zurück, deshalb kennt die Durchführung es.
        /// </param>
        public SearchRunner(
            ImportService importService,
            IErrorDialogService errorDialogService,
            ILocalizationService localizationService,
            IServiceScopeFactory? scopeFactory,
            BackgroundCoverService? backgroundCoverService,
            SearchViewModel parent)
        {
            _importService = importService;
            _errorDialogService = errorDialogService;
            _localizationService = localizationService;
            _scopeFactory = scopeFactory;
            _backgroundCoverService = backgroundCoverService;
            _parent = parent;
        }

        /// <summary>
        /// Das Ergebnis einer Suche.
        /// </summary>
        /// <param name="Results">Die Trefferkarten in Anzeigereihenfolge.</param>
        /// <param name="SpotifyFallbackApplied">Ob der Anbieter auf Spotify ausgewichen ist.</param>
        /// <param name="OnlineError">
        /// Ein Fehler des Online-Zweigs, falls einer auftrat. Er wird mitgegeben statt
        /// geworfen, damit die lokalen Treffer trotzdem erscheinen.
        /// </param>
        internal sealed record SearchRunResult(
            IReadOnlyList<SearchResultViewModel> Results,
            bool SpotifyFallbackApplied,
            Exception? OnlineError);

        /// <summary>
        /// Führt die Suche im gewünschten Bereich aus.
        /// </summary>
        /// <param name="searchText">Der eingefrorene Suchbegriff.</param>
        /// <param name="scope">Online, lokal oder beides.</param>
        /// <param name="coverToken">Bricht ab, sobald eine neue Suche begonnen hat.</param>
        /// <returns>
        /// Das Ergebnis, oder <see langword="null"/> bei Abbruch — dann gehören die Treffer
        /// zu einer überholten Suche und dürfen die Anzeige nicht mehr anfassen.
        /// </returns>
        public async Task<SearchRunResult?> RunAsync(
            string searchText, SearchSource scope, CancellationToken coverToken)
        {
            List<SearchResultViewModel> viewModels = [];
            Exception? onlineError = null;
            bool spotifyFallback = false;

            // Online- und Lokal-Zweig sind entkoppelt: Ein Fehler im Online-Zweig (kein Netz,
            // Zeitüberschreitung, Parser-Fehler) darf die lokalen Treffer nicht verschlucken.
            // Der Fehler wird gemerkt und erst nach dem Anzeigen der lokalen Treffer gezeigt.
            if (scope is SearchSource.Online or SearchSource.Both)
            {
                OnlineOutcome? online = await CollectOnlineResultsAsync(searchText, coverToken);
                if (online is null) return null;

                viewModels.AddRange(online.Results);
                onlineError = online.Error;
                spotifyFallback = online.SpotifyFallbackApplied;
            }

            if (coverToken.IsCancellationRequested) return null;

            if (scope is SearchSource.Local or SearchSource.Both)
            {
                List<SearchResultViewModel>? localResults =
                    await CollectLocalResultsAsync(searchText, coverToken);

                if (localResults is null) return null;
                viewModels.AddRange(localResults);
            }

            return new SearchRunResult(viewModels, spotifyFallback, onlineError);
        }

        /// <summary>Das Zwischenergebnis des Online-Zweigs.</summary>
        private sealed record OnlineOutcome(
            List<SearchResultViewModel> Results,
            bool SpotifyFallbackApplied,
            Exception? Error);

        /// <summary>
        /// Der Online-Zweig: Serien und Alben beim Anbieter suchen, zusammenführen und in
        /// Trefferkarten übersetzen.
        /// </summary>
        /// <returns>Das Zwischenergebnis, oder <see langword="null"/> bei Abbruch.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Online-Zweig der Suche: Provider-HTTP-/Parser-/Timeout-Fehler werden gemerkt und nach den lokalen Treffern gezeigt, damit ein Ausfall der Gegenstelle die lokalen Treffer nicht verschluckt.")]
        private async Task<OnlineOutcome?> CollectOnlineResultsAsync(
            string searchText, CancellationToken coverToken)
        {
            List<SearchResultViewModel> viewModels = [];
            bool spotifyFallback = false;

            try
            {
                // Das Abbruchzeichen geht mit: Eine überholte Suche soll nicht noch zu Ende
                // laufen und Kontingent beim Anbieter verbrauchen. Das Ergebnis ist dasselbe —
                // ihre Treffer wurden ohnehin verworfen.
                SearchOutcome seriesOutcome = await _importService.SearchAsync(searchText, coverToken);
                if (coverToken.IsCancellationRequested) return null;

                SearchOutcome albumsOutcome = await _importService.SearchAlbumsAsync(searchText, coverToken);
                if (coverToken.IsCancellationRequested) return null;

                spotifyFallback = seriesOutcome.SpotifyFallbackApplied || albumsOutcome.SpotifyFallbackApplied;

                List<ImportSeries> combined = MergeAndRank(
                    seriesOutcome.Results, albumsOutcome.Results, searchText);

                foreach (ImportSeries series in combined)
                {
                    bool alreadyImported = series.IsAlbumResult
                        ? false
                        : await _importService.IsAlreadyImportedAsync(series, coverToken);
                    if (coverToken.IsCancellationRequested) return null;

                    viewModels.Add(new SearchResultViewModel(
                        series, alreadyImported, _importService, _errorDialogService,
                        _localizationService, _backgroundCoverService,
                        parentViewModel: _parent, cancellationToken: coverToken));
                }
            }
            catch (Exception ex) when (!coverToken.IsCancellationRequested)
            {
                // Fehler merken, aber die bisher gebauten Karten behalten — der Lokal-Zweig
                // läuft danach trotzdem.
                return new OnlineOutcome(viewModels, spotifyFallback, ex);
            }

            return new OnlineOutcome(viewModels, spotifyFallback, null);
        }

        /// <summary>
        /// Führt Serien- und Albentreffer zusammen und sortiert nach Relevanz: Treffer mit dem
        /// Suchbegriff in Titel oder Künstler zuerst, danach nach Bewertung.
        /// </summary>
        /// <remarks>
        /// Der Vergleich ignoriert Groß- und Kleinschreibung, deshalb geht der Suchbegriff
        /// unverändert ein — ein vorheriges Umwandeln in Großbuchstaben war irreführend.
        /// </remarks>
        private static List<ImportSeries> MergeAndRank(
            IReadOnlyList<ImportSeries> seriesResults,
            IReadOnlyList<ImportSeries> albumResults,
            string searchNeedle)
        {
            List<ImportSeries> combined = new(seriesResults.Count + albumResults.Count);
            combined.AddRange(seriesResults);
            combined.AddRange(albumResults);

            combined.Sort((a, b) =>
            {
                bool aContains = a.Title.Contains(searchNeedle, StringComparison.OrdinalIgnoreCase)
                              || (a.ArtistName?.Contains(searchNeedle, StringComparison.OrdinalIgnoreCase) ?? false);
                bool bContains = b.Title.Contains(searchNeedle, StringComparison.OrdinalIgnoreCase)
                              || (b.ArtistName?.Contains(searchNeedle, StringComparison.OrdinalIgnoreCase) ?? false);

                if (aContains != bContains) return aContains ? -1 : 1;
                return b.Score.CompareTo(a.Score);
            });

            return combined;
        }

        /// <summary>
        /// Der lokale Zweig: Treffer aus dem eigenen Bestand als Karten ohne Import-Schaltfläche.
        /// </summary>
        /// <returns>Die Trefferkarten, oder <see langword="null"/> bei Abbruch.</returns>
        private async Task<List<SearchResultViewModel>?> CollectLocalResultsAsync(
            string searchText, CancellationToken coverToken)
        {
            IReadOnlyList<ImportSeries> localResults = await SearchLocalAsync(searchText);
            if (coverToken.IsCancellationRequested) return null;

            List<SearchResultViewModel> viewModels = new(localResults.Count);

            foreach (ImportSeries series in localResults)
            {
                // Lokale Einträge stehen bereits in der Datenbank — eine Import-Schaltfläche
                // wäre hier ohne Sinn.
                viewModels.Add(new SearchResultViewModel(
                    series, true, _importService, _errorDialogService,
                    _localizationService, _backgroundCoverService,
                    cancellationToken: coverToken));
            }

            return viewModels;
        }

        /// <summary>
        /// Durchsucht den eigenen Bestand nach Serien, deren Titel den Suchbegriff enthält.
        /// Ohne Bereichsfabrik bleibt das Ergebnis leer — so laufen Tests ohne Datenbank.
        /// </summary>
        /// <param name="query">Der Suchbegriff; Groß- und Kleinschreibung wird ignoriert.</param>
        /// <returns>Die gefundenen Serien mit <c>Source = "Lokal"</c>.</returns>
        private async Task<IReadOnlyList<ImportSeries>> SearchLocalAsync(string query)
        {
            if (_scopeFactory is null)
            {
                return [];
            }

            // Führende und abschließende Leerzeichen aus dem Vorschlagsfeld entfernen, damit
            // sie nicht in den Textvergleich einfließen.
            string trimmedQuery = query.Trim();
            if (trimmedQuery.Length == 0)
            {
                return [];
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();

            List<ImportSeries> localResults = [];

            foreach (Series series in allSeries)
            {
                if (series.Title.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase))
                {
                    // SourceSeriesId ist die Kennung der Serie in der Datenbank — damit bleibt
                    // der Eintrag eindeutig zuzuordnen.
                    localResults.Add(new ImportSeries
                    {
                        Title = series.Title,
                        Source = "Lokal",
                        SourceSeriesId = series.Id.ToString()
                    });
                }
            }

            return localResults;
        }
    }
}

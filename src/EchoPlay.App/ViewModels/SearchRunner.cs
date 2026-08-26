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
    /// Führt eine einzelne Suche aus: fragt den Anbieter, durchsucht den eigenen Bestand
    /// und reicht jede fertige Trefferkarte an die Seite weiter.
    /// </summary>
    /// <remarks>
    /// Der Ablauf stand vorher im Ansichtsmodell und machte gut ein Drittel davon aus. Die
    /// Trennung ist die zwischen „was gesucht wird" und „was die Seite anzeigt": Das
    /// Ansichtsmodell hält Eingabe, Zustand und Liste, dieser Typ die Durchführung.
    /// <para>
    /// Die Karten werden einzeln gemeldet, statt am Ende als Liste. Eine Anbieter-Suche über
    /// mehrere Künstler dauert Sekunden; die Seite zeigte solange nur den Ladekreis und wirkte
    /// tot. Der Preis dafür ist die Reihenfolge: Sie entsteht beim Einfügen, nicht am Schluss.
    /// </para>
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
        /// Import zurück und es nimmt die fertigen Karten entgegen, deshalb kennt die
        /// Durchführung es.
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
        /// Das Ergebnis einer Suche. Die Treffer selbst sind zu diesem Zeitpunkt längst
        /// gemeldet; hier steht nur noch, was die Seite darüber hinaus wissen muss.
        /// </summary>
        /// <param name="SpotifyFallbackApplied">Ob der Anbieter auf Spotify ausgewichen ist.</param>
        /// <param name="OnlineError">
        /// Ein Fehler des Online-Zweigs, falls einer auftrat. Er wird mitgegeben statt
        /// geworfen, damit die lokalen Treffer trotzdem erscheinen.
        /// </param>
        internal sealed record SearchRunResult(
            bool SpotifyFallbackApplied,
            Exception? OnlineError);

        /// <summary>
        /// Führt die Suche im gewünschten Bereich aus und meldet jede fertige Karte an das
        /// Ansichtsmodell.
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
            Exception? onlineError = null;
            bool spotifyFallback = false;

            // Online- und Lokal-Zweig sind entkoppelt: Ein Fehler im Online-Zweig (kein Netz,
            // Zeitüberschreitung, Parser-Fehler) darf die lokalen Treffer nicht verschlucken.
            // Der Fehler wird gemerkt und erst nach dem Anzeigen der lokalen Treffer gezeigt.
            if (scope is SearchSource.Online or SearchSource.Both)
            {
                OnlineOutcome? online = await CollectOnlineResultsAsync(searchText, coverToken);
                if (online is null) return null;

                onlineError = online.Error;
                spotifyFallback = online.SpotifyFallbackApplied;
            }

            if (coverToken.IsCancellationRequested) return null;

            if (scope is SearchSource.Local or SearchSource.Both)
            {
                if (!await PublishLocalResultsAsync(searchText, coverToken)) return null;
            }

            return new SearchRunResult(spotifyFallback, onlineError);
        }

        /// <summary>Das Zwischenergebnis des Online-Zweigs.</summary>
        private sealed record OnlineOutcome(
            bool SpotifyFallbackApplied,
            Exception? Error);

        /// <summary>
        /// Der Online-Zweig: erst die Alben, dann die Serien.
        /// </summary>
        /// <remarks>
        /// Die Reihenfolge ist Absicht. Die Albensuche ist eine einzige Anfrage und bringt
        /// Treffer samt Cover zurück — damit steht die erste Kachel nach gut einer Sekunde.
        /// Die Seriensuche bewertet Künstler einzeln und braucht länger; ihre Treffer wandern
        /// beim Einfügen an die richtige Stelle.
        /// </remarks>
        /// <returns>Das Zwischenergebnis, oder <see langword="null"/> bei Abbruch.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Online-Zweig der Suche: Provider-HTTP-/Parser-/Timeout-Fehler werden gemerkt und nach den lokalen Treffern gezeigt, damit ein Ausfall der Gegenstelle die lokalen Treffer nicht verschluckt.")]
        private async Task<OnlineOutcome?> CollectOnlineResultsAsync(
            string searchText, CancellationToken coverToken)
        {
            bool spotifyFallback = false;

            try
            {
                // Das Abbruchzeichen geht mit: Eine überholte Suche soll nicht noch zu Ende
                // laufen und Kontingent beim Anbieter verbrauchen. Das Ergebnis ist dasselbe —
                // ihre Treffer wurden ohnehin verworfen.
                SearchOutcome albumsOutcome = await _importService.SearchAlbumsAsync(searchText, coverToken);
                if (coverToken.IsCancellationRequested) return null;

                spotifyFallback = albumsOutcome.SpotifyFallbackApplied;

                foreach (ImportSeries album in albumsOutcome.Results)
                {
                    if (!await PublishOnlineResultAsync(album, searchText, coverToken)) return null;
                }

                await foreach (ImportSeries series in _importService.SearchStreamAsync(searchText, coverToken))
                {
                    if (!await PublishOnlineResultAsync(series, searchText, coverToken)) return null;
                }
            }
            catch (Exception ex) when (!coverToken.IsCancellationRequested)
            {
                // Fehler merken, aber die bisher gemeldeten Karten stehen lassen — der
                // Lokal-Zweig läuft danach trotzdem.
                return new OnlineOutcome(spotifyFallback, ex);
            }

            return new OnlineOutcome(spotifyFallback, null);
        }

        /// <summary>
        /// Baut die Karte zu einem Anbieter-Treffer und meldet sie an das Ansichtsmodell.
        /// </summary>
        /// <param name="series">Der Treffer.</param>
        /// <param name="searchText">Der Suchbegriff, für die Reihenfolge in der Liste.</param>
        /// <param name="coverToken">Bricht ab, sobald eine neue Suche begonnen hat.</param>
        /// <returns><see langword="false"/>, wenn die Suche inzwischen überholt ist.</returns>
        private async Task<bool> PublishOnlineResultAsync(
            ImportSeries series, string searchText, CancellationToken coverToken)
        {
            bool alreadyImported = !series.IsAlbumResult
                && await _importService.IsAlreadyImportedAsync(series, coverToken);

            if (coverToken.IsCancellationRequested) return false;

            SearchResultViewModel card = new(
                series, alreadyImported, _importService, _errorDialogService,
                _localizationService, _backgroundCoverService,
                parentViewModel: _parent, cancellationToken: coverToken);

            _parent.PublishOnlineResult(card, SearchResultRank.Create(series, searchText));
            return true;
        }

        /// <summary>
        /// Der lokale Zweig: Treffer aus dem eigenen Bestand als Karten ohne Import-Schaltfläche.
        /// Sie stehen am Ende der Liste, damit die Anbieter-Treffer ihre Reihenfolge behalten.
        /// </summary>
        /// <param name="searchText">Der Suchbegriff.</param>
        /// <param name="coverToken">Bricht ab, sobald eine neue Suche begonnen hat.</param>
        /// <returns><see langword="false"/>, wenn die Suche inzwischen überholt ist.</returns>
        private async Task<bool> PublishLocalResultsAsync(
            string searchText, CancellationToken coverToken)
        {
            IReadOnlyList<ImportSeries> localResults = await SearchLocalAsync(searchText);
            if (coverToken.IsCancellationRequested) return false;

            foreach (ImportSeries series in localResults)
            {
                // Lokale Einträge stehen bereits in der Datenbank — eine Import-Schaltfläche
                // wäre hier ohne Sinn.
                _parent.PublishLocalResult(new SearchResultViewModel(
                    series, true, _importService, _errorDialogService,
                    _localizationService, _backgroundCoverService,
                    cancellationToken: coverToken));
            }

            return true;
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
                    // SourceSeriesId ist die Kennung der Serie in der Datenbank — damit findet
                    // die Trefferkarte das Cover in CoverImages, ganz ohne Netz.
                    localResults.Add(new ImportSeries
                    {
                        Title = series.Title,
                        Source = ProviderKeys.Local,
                        SourceSeriesId = series.Id.ToString()
                    });
                }
            }

            return localResults;
        }
    }
}

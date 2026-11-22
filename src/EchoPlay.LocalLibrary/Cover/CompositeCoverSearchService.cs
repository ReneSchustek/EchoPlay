using EchoPlay.Logger.Abstractions;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Fasst mehrere <see cref="ICoverSearchService"/>-Implementierungen zusammen
    /// und führt die Ergebnisse aller Anbieter in einer einzigen Liste zusammen.
    /// Die Suchen laufen parallel, ein fehlgeschlagener Anbieter blockiert die anderen nicht.
    /// </summary>
    public sealed class CompositeCoverSearchService : ICoverSearchService
    {
        private readonly IReadOnlyList<ICoverSearchService> _providers;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Composite-Service mit den einzelnen Anbietern.
        /// </summary>
        /// <param name="providers">Die Anbieter in Anzeigereihenfolge; ihre Treffer folgen in dieser Reihenfolge aufeinander.</param>
        /// <param name="loggerFactory">Für Ausfälle, die ein Anbieter nicht selbst abfängt.</param>
        public CompositeCoverSearchService(IReadOnlyList<ICoverSearchService> providers, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(providers);
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _providers = providers;
            _logger = loggerFactory.CreateLogger(nameof(CompositeCoverSearchService));
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CancellationToken ct = default) =>
            SearchAsync(title, CoverSearchPage.First, ct);

        /// <inheritdoc/>
        public async Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CoverSearchPage page,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return [];
            }

            IReadOnlyList<CoverSearchResult>[] allResults = await Task.WhenAll(
                _providers.Select(provider => SafeSearchAsync(provider, title, page, ct))).ConfigureAwait(false);

            return [.. allResults.SelectMany(results => results)];
        }

        /// <summary>
        /// Fragt einen Anbieter ab. Ein Ausfall ergibt eine leere Liste statt die gesamte Suche
        /// abzubrechen.
        /// </summary>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Die Anbieter laufen parallel und melden Ausfälle mit unterschiedlichen, nicht gemeinsam abgeleiteten Typen. Fällt einer aus, liefern die anderen weiter; ein Abbruch des Aufrufers geht vorher durch.")]
        private async Task<IReadOnlyList<CoverSearchResult>> SafeSearchAsync(
            ICoverSearchService provider,
            string title,
            CoverSearchPage page,
            CancellationToken ct)
        {
            try
            {
                return await provider.SearchAsync(title, page, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Ein Abbruch des Aufrufers ist kein Fehlschlag und gehört nach oben.
                throw;
            }
            catch (Exception ex)
            {
                // Netz- und Lesefehler fangen die Anbieter selbst ab und protokollieren sie. Was
                // hier ankommt, ist unerwartet und gehört deshalb als Fehler ins Protokoll.
                _logger.Error("Cover-Suche bei {Provider} abgebrochen: {Reason}", ex, provider.GetType().Name, ex.Message);
                return [];
            }
        }
    }
}

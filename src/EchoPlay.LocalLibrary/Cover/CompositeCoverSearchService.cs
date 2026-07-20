using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Fasst mehrere <see cref="ICoverSearchService"/>-Implementierungen zusammen
    /// und führt die Ergebnisse aller Anbieter in einer einzigen Liste zusammen.
    /// Die Suchen laufen parallel – ein fehlgeschlagener Anbieter blockiert die anderen nicht.
    /// </summary>
    public sealed class CompositeCoverSearchService : ICoverSearchService
    {
        private readonly IReadOnlyList<ICoverSearchService> _providers;

        /// <summary>
        /// Initialisiert den Composite-Service mit den einzelnen Anbietern.
        /// </summary>
        /// <param name="providers">Die konkreten Such-Dienste (z.B. Cover Art Archive, iTunes).</param>
        public CompositeCoverSearchService(IReadOnlyList<ICoverSearchService> providers)
        {
            _providers = providers;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return [];
            }

            // Alle Anbieter parallel abfragen
            Task<IReadOnlyList<CoverSearchResult>>[] tasks =
                new Task<IReadOnlyList<CoverSearchResult>>[_providers.Count];

            for (int i = 0; i < _providers.Count; i++)
            {
                tasks[i] = SafeSearchAsync(_providers[i], title, ct);
            }

            IReadOnlyList<CoverSearchResult>[] allResults = await Task.WhenAll(tasks).ConfigureAwait(false);

            // Ergebnisse aller Anbieter zusammenführen
            List<CoverSearchResult> combined = [];
            foreach (IReadOnlyList<CoverSearchResult> providerResults in allResults)
            {
                combined.AddRange(providerResults);
            }

            return combined;
        }

        /// <summary>
        /// Wrapper, der Exceptions eines einzelnen Anbieters abfängt.
        /// Ein fehlerhafter Anbieter liefert eine leere Liste statt die gesamte Suche abzubrechen.
        /// </summary>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Safe-Wrapper für Provider-Fan-Out; ein einzelner Provider-Fehler (egal welcher Typ) darf die parallelen Provider-Aufrufe nicht abbrechen.")]
        private static async Task<IReadOnlyList<CoverSearchResult>> SafeSearchAsync(
            ICoverSearchService provider,
            string title,
            CancellationToken ct)
        {
            try
            {
                return await provider.SearchAsync(title, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return [];
            }
        }
    }
}

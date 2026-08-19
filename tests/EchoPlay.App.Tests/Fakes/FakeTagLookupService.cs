using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ITagLookupService"/>.
    /// Gibt vorab konfigurierte Suchergebnisse zurück, ohne das Netzwerk zu verwenden.
    /// Zeichnet die zuletzt empfangene Suchanfrage auf – für Assertions in Tests.
    /// </summary>
    internal sealed class FakeTagLookupService : ITagLookupService
    {
        private readonly IReadOnlyList<TagLookupResult> _results;

        /// <summary>Zuletzt empfangene Suchanfrage – für Assertions.</summary>
        public string? LastQuery { get; private set; }

        /// <summary>
        /// Erstellt den Fake mit vorab konfigurierten Ergebnissen.
        /// </summary>
        /// <param name="results">Ergebnisse, die bei jedem Aufruf von <see cref="SearchAsync"/> zurückgegeben werden.</param>
        /// <param name="searchFailure">
        /// Wird statt eines Ergebnisses geworfen, wenn gesetzt. Die Suche geht über das Netz;
        /// eine Zeitüberschreitung ist dort der Regelfall und kein Sonderfall.
        /// </param>
        public FakeTagLookupService(
            IReadOnlyList<TagLookupResult>? results = null,
            Exception? searchFailure = null)
        {
            _results = results ?? [];
            _searchFailure = searchFailure;
        }

        private readonly Exception? _searchFailure;

        /// <inheritdoc/>
        public Task<IReadOnlyList<TagLookupResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;

            return _searchFailure is not null
                ? Task.FromException<IReadOnlyList<TagLookupResult>>(_searchFailure)
                : Task.FromResult(_results);
        }
    }
}

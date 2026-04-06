using EchoPlay.LocalLibrary.Cover;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ICoverSearchService"/> zur Verwendung in Tests.
    /// Gibt eine konfigurierbare Liste von Cover-Kandidaten zurück, ohne
    /// Netzwerkzugriff zu benötigen.
    /// </summary>
    internal sealed class FakeCoverSearchService : ICoverSearchService
    {
        private IReadOnlyList<CoverSearchResult> _results = [];

        /// <summary>
        /// Letzter Suchbegriff, der an <see cref="SearchAsync"/> übergeben wurde.
        /// Nützlich für Assertions in Tests.
        /// </summary>
        public string? LastSearchTitle { get; private set; }

        /// <summary>
        /// Konfiguriert die Ergebnisse, die bei der nächsten Suche zurückgegeben werden.
        /// </summary>
        /// <param name="results">Simulierte Suchergebnisse.</param>
        public void SetResults(IReadOnlyList<CoverSearchResult> results)
        {
            _results = results;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CancellationToken ct = default)
        {
            LastSearchTitle = title;
            return Task.FromResult(_results);
        }
    }
}

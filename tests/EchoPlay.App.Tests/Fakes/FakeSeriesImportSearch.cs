using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ISeriesImportSearch"/>.
    /// Gibt eine vorab konfigurierte Liste zurück, ohne externe APIs aufzurufen.
    /// </summary>
    internal sealed class FakeSeriesImportSearch : ISeriesImportSearch
    {
        private readonly IReadOnlyList<ImportSeries> _results;
        private readonly string _source;

        /// <summary>
        /// Erstellt den Fake mit festen Rückgabewerten.
        /// </summary>
        /// <param name="results">Die zurückzugebende Ergebnisliste.</param>
        /// <param name="source">Die Quellbezeichnung dieser Implementierung (z.B. "Spotify").</param>
        public FakeSeriesImportSearch(IReadOnlyList<ImportSeries> results, string source = "Spotify")
        {
            _results = results;
            _source = source;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<ImportSeries>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_results);
        }
    }
}

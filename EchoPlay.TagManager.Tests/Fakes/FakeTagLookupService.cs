using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;

namespace EchoPlay.TagManager.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ITagLookupService"/> mit vorkonfigurierten Suchergebnissen.
    /// Wird in Tests verwendet, die einen Lookup-Service benötigen, ohne echte HTTP-Anfragen.
    /// </summary>
    internal sealed class FakeTagLookupService : ITagLookupService
    {
        private readonly IReadOnlyList<TagLookupResult> _results;

        /// <summary>
        /// Initialisiert den Fake mit einer optionalen Liste von Ergebnissen.
        /// Wird keine Liste übergeben, gibt <see cref="SearchAsync"/> eine leere Liste zurück.
        /// </summary>
        /// <param name="results">Vorkonfigurierte Ergebnisse oder <see langword="null"/> für eine leere Liste.</param>
        public FakeTagLookupService(IReadOnlyList<TagLookupResult>? results = null)
        {
            _results = results ?? [];
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<TagLookupResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(_results);
    }
}

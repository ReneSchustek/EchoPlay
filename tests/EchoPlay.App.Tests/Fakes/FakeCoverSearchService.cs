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
            => SearchAsync(title, CoverSearchPage.First, ct);

        /// <inheritdoc/>
        public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(
            string title,
            CoverSearchPage page,
            CancellationToken ct = default)
        {
            LastSearchTitle = title;
            LastPage = page;
            _ = _firstSearch.TrySetResult(title);

            // Nachladen liefert nichts mehr — Tests, die nur die erste Seite brauchen, bleiben
            // damit unverändert, und der Dialog blendet das Nachladen korrekt aus.
            return Task.FromResult(page.Index == 0 ? _results : []);
        }

        /// <summary>Letzte angefragte Seite. Für Assertions zum Nachladen.</summary>
        public CoverSearchPage LastPage { get; private set; }

        private readonly TaskCompletionSource<string> _firstSearch =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Wird abgeschlossen, sobald zum ersten Mal gesucht wurde. Wartepunkt für Abläufe,
        /// die im Hintergrund starten und keinen Rückgabewert haben.
        /// </summary>
        public Task<string> FirstSearch => _firstSearch.Task;
    }
}

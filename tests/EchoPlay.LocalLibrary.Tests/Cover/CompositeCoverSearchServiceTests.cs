using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Tests.Fakes;

namespace EchoPlay.LocalLibrary.Tests.Cover
{
    /// <summary>
    /// Tests für <see cref="CompositeCoverSearchService"/>: Fan-Out und Fehlerisolation.
    /// </summary>
    public sealed class CompositeCoverSearchServiceTests
    {
        [Fact]
        public async Task SearchAsync_OneProviderHasResults_AggregatesResults()
        {
            CoverSearchResult expected = new(
                ThumbnailUrl: new Uri("https://thumb.example/1.jpg"),
                FullUrl: new Uri("https://full.example/1.jpg"),
                ReleaseTitle: "TKKG",
                Source: "Stub-A");
            StubCoverSearchService providerA = new([expected]);
            StubCoverSearchService providerB = new([]);
            CompositeCoverSearchService composite = new([providerA, providerB], new FakeLoggerFactory());

            IReadOnlyList<CoverSearchResult> result = await composite.SearchAsync("TKKG", ct: TestContext.Current.CancellationToken);

            CoverSearchResult only = Assert.Single(result);
            Assert.Equal(expected, only);
        }

        [Fact]
        public async Task SearchAsync_AllProvidersEmpty_ReturnsEmptyList()
        {
            StubCoverSearchService providerA = new([]);
            StubCoverSearchService providerB = new([]);
            CompositeCoverSearchService composite = new([providerA, providerB], new FakeLoggerFactory());

            IReadOnlyList<CoverSearchResult> result = await composite.SearchAsync("Unbekannt", ct: TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchAsync_OneProviderThrows_OtherResultsStillReturned()
        {
            CoverSearchResult fromA = new(
                ThumbnailUrl: new Uri("https://thumb.example/a.jpg"),
                FullUrl: new Uri("https://full.example/a.jpg"),
                ReleaseTitle: "Treffer A",
                Source: "Stub-A");
            StubCoverSearchService providerA = new([fromA]);
            ThrowingCoverSearchService providerB = new();
            CapturingLoggerFactory protocol = new();
            CompositeCoverSearchService composite = new([providerA, providerB], protocol);

            IReadOnlyList<CoverSearchResult> result = await composite.SearchAsync("TKKG", ct: TestContext.Current.CancellationToken);

            CoverSearchResult only = Assert.Single(result);
            Assert.Equal(fromA, only);

            // Netzfehler fangen die Anbieter selbst; was bis hierher durchschlägt, darf nicht stumm bleiben.
            Assert.Contains(protocol.Entries, entry => entry.Level == "Error");
        }

        [Fact]
        public async Task SearchAsync_EmptyTitle_ReturnsEmptyListWithoutCallingProviders()
        {
            CountingCoverSearchService provider = new();
            CompositeCoverSearchService composite = new([provider], new FakeLoggerFactory());

            IReadOnlyList<CoverSearchResult> result = await composite.SearchAsync(string.Empty, ct: TestContext.Current.CancellationToken);

            Assert.Empty(result);
            Assert.Equal(0, provider.CallCount);
        }

        [Fact]
        public async Task SearchAsync_PassesThePageToEveryProvider()
        {
            // Ohne das würden alle Anbieter beim Nachladen wieder die erste Seite liefern und
            // der Dialog bekäme nur Dubletten zu sehen.
            StubCoverSearchService firstProvider = new([Hit("A")]);
            StubCoverSearchService secondProvider = new([Hit("B")]);
            CompositeCoverSearchService composite = new([firstProvider, secondProvider], new FakeLoggerFactory());

            _ = await composite.SearchAsync("TKKG", new CoverSearchPage(2), TestContext.Current.CancellationToken);

            Assert.Equal(2, firstProvider.LastPage.Index);
            Assert.Equal(2, secondProvider.LastPage.Index);
        }

        [Fact]
        public async Task SearchAsync_WithoutPage_QueriesTheFirstPage()
        {
            StubCoverSearchService onlyProvider = new([Hit("A")]);
            CompositeCoverSearchService composite = new([onlyProvider], new FakeLoggerFactory());

            _ = await composite.SearchAsync("TKKG", ct: TestContext.Current.CancellationToken);

            Assert.Equal(0, onlyProvider.LastPage.Index);
        }

        [Fact]
        public async Task SearchAsync_CancelledByTheCaller_PassesTheCancellationOn()
        {
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();
            CompositeCoverSearchService composite = new([new CancellingCoverSearchService()], new FakeLoggerFactory());

            // Ein Abbruch ist kein Ausfall eines Anbieters; er darf nicht als leere Trefferliste enden.
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => composite.SearchAsync("TKKG", ct: cts.Token));
        }

        private static CoverSearchResult Hit(string name) =>
            new(new Uri($"https://example.invalid/{name}-thumb.jpg"), new Uri($"https://example.invalid/{name}.jpg"), name, "Test");

        /// <summary>
        /// Liefert die vorgegebenen Treffer und merkt sich die zuletzt angefragte Seite.
        /// </summary>
        private sealed class StubCoverSearchService : ICoverSearchService
        {
            private readonly IReadOnlyList<CoverSearchResult> _results;

            public StubCoverSearchService(IReadOnlyList<CoverSearchResult> results)
            {
                _results = results;
            }

            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CancellationToken ct = default)
                => SearchAsync(title, CoverSearchPage.First, ct);

            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CoverSearchPage page, CancellationToken ct = default)
            {
                LastPage = page;
                return Task.FromResult(_results);
            }

            public CoverSearchPage LastPage { get; private set; }
        }

        /// <summary>
        /// Reicht einen ausgelösten Abbruch als OperationCanceledException weiter, sonst keine
        /// Treffer.
        /// </summary>
        private sealed class CancellingCoverSearchService : ICoverSearchService
        {
            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CancellationToken ct = default)
                => SearchAsync(title, CoverSearchPage.First, ct);

            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CoverSearchPage page, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<CoverSearchResult>>([]);
            }
        }

        /// <summary>
        /// Scheitert wie ein ausgefallener Anbieter.
        /// </summary>
        private sealed class ThrowingCoverSearchService : ICoverSearchService
        {
            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CancellationToken ct = default)
                => SearchAsync(title, CoverSearchPage.First, ct);

            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CoverSearchPage page, CancellationToken ct = default)
            {
                throw new InvalidOperationException("Simulierter Provider-Fehler");
            }
        }

        /// <summary>
        /// Zählt die Anfragen, damit ein Test sieht, ob ein Anbieter übersprungen wurde.
        /// </summary>
        private sealed class CountingCoverSearchService : ICoverSearchService
        {
            public int CallCount { get; private set; }

            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CancellationToken ct = default)
                => SearchAsync(title, CoverSearchPage.First, ct);

            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CoverSearchPage page, CancellationToken ct = default)
            {
                CallCount++;
                return Task.FromResult<IReadOnlyList<CoverSearchResult>>([]);
            }
        }
    }
}

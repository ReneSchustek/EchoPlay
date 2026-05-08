using EchoPlay.LocalLibrary.Cover;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
                ThumbnailUrl: "https://thumb.example/1.jpg",
                FullUrl: "https://full.example/1.jpg",
                ReleaseTitle: "TKKG",
                Source: "Stub-A");
            StubCoverSearchService providerA = new([expected]);
            StubCoverSearchService providerB = new([]);
            CompositeCoverSearchService composite = new([providerA, providerB]);

            IReadOnlyList<CoverSearchResult> result = await composite.SearchAsync("TKKG");

            CoverSearchResult only = Assert.Single(result);
            Assert.Equal(expected, only);
        }

        [Fact]
        public async Task SearchAsync_AllProvidersEmpty_ReturnsEmptyList()
        {
            StubCoverSearchService providerA = new([]);
            StubCoverSearchService providerB = new([]);
            CompositeCoverSearchService composite = new([providerA, providerB]);

            IReadOnlyList<CoverSearchResult> result = await composite.SearchAsync("Unbekannt");

            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchAsync_OneProviderThrows_OtherResultsStillReturned()
        {
            CoverSearchResult fromA = new(
                ThumbnailUrl: "https://thumb.example/a.jpg",
                FullUrl: "https://full.example/a.jpg",
                ReleaseTitle: "Treffer A",
                Source: "Stub-A");
            StubCoverSearchService providerA = new([fromA]);
            ThrowingCoverSearchService providerB = new();
            CompositeCoverSearchService composite = new([providerA, providerB]);

            IReadOnlyList<CoverSearchResult> result = await composite.SearchAsync("TKKG");

            CoverSearchResult only = Assert.Single(result);
            Assert.Equal(fromA, only);
        }

        [Fact]
        public async Task SearchAsync_EmptyTitle_ReturnsEmptyListWithoutCallingProviders()
        {
            CountingCoverSearchService provider = new();
            CompositeCoverSearchService composite = new([provider]);

            IReadOnlyList<CoverSearchResult> result = await composite.SearchAsync(string.Empty);

            Assert.Empty(result);
            Assert.Equal(0, provider.CallCount);
        }

        // ── Test-Doubles ────────────────────────────────────────────────────────

        private sealed class StubCoverSearchService : ICoverSearchService
        {
            private readonly IReadOnlyList<CoverSearchResult> _results;

            public StubCoverSearchService(IReadOnlyList<CoverSearchResult> results)
            {
                _results = results;
            }

            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CancellationToken ct = default)
            {
                return Task.FromResult(_results);
            }
        }

        private sealed class ThrowingCoverSearchService : ICoverSearchService
        {
            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CancellationToken ct = default)
            {
                throw new InvalidOperationException("Simulierter Provider-Fehler");
            }
        }

        private sealed class CountingCoverSearchService : ICoverSearchService
        {
            public int CallCount { get; private set; }

            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CancellationToken ct = default)
            {
                CallCount++;
                return Task.FromResult<IReadOnlyList<CoverSearchResult>>([]);
            }
        }
    }
}

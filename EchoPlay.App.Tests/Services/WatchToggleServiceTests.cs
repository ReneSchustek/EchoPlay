using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests fuer <see cref="WatchToggleService"/>.
    /// Verwendet eine echte ServiceCollection mit Fake-Implementierungen, um den
    /// Scope-Factory-Pfad realitaetsnah zu pruefen.
    /// </summary>
    public sealed class WatchToggleServiceTests
    {
        private static (WatchToggleService Service, FakeSeriesDataService Series, FakeCachedNewReleaseDataService Cache)
            BuildService()
        {
            FakeSeriesDataService series = new();
            FakeCachedNewReleaseDataService cache = new();

            ServiceCollection services = new();
            _ = services.AddHttpClient();
            _ = services.AddScoped<ISeriesDataService>(_ => series);
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => cache);
            ServiceProvider provider = services.BuildServiceProvider();

            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            WatchToggleService toggle = new(scopeFactory);
            return (toggle, series, cache);
        }

        private static async Task<Guid> AddSeriesAsync(FakeSeriesDataService series, bool watched)
        {
            Series s = new() { Title = "Test", IsWatched = watched };
            await series.AddAsync(s);
            return s.Id;
        }

        [Fact]
        public async Task ToggleAsync_DisableWatch_RemovesCachedReleasesForSeries()
        {
            (WatchToggleService toggle, FakeSeriesDataService series, FakeCachedNewReleaseDataService cache) = BuildService();
            Guid seriesId = await AddSeriesAsync(series, watched: true);

            // Cache pre-fuellen, damit der Toggle-Pfad einen Eintrag findet, den er entfernen muss.
            await cache.UpsertRangeAsync([new() { SeriesId = seriesId, CollectionId = 12345L, Title = "Folge", EpisodeNumber = 1 }]);
            IReadOnlyList<CachedNewRelease> before = await cache.GetBySeriesIdAsync(seriesId);
            _ = Assert.Single(before);

            await toggle.ToggleAsync(seriesId, watch: false, CancellationToken.None);

            IReadOnlyList<CachedNewRelease> after = await cache.GetBySeriesIdAsync(seriesId);
            Assert.Empty(after);
        }

        [Fact]
        public async Task ToggleAsync_DisableWatch_PersistsWatchedFlag()
        {
            (WatchToggleService toggle, FakeSeriesDataService series, _) = BuildService();
            Guid seriesId = await AddSeriesAsync(series, watched: true);

            await toggle.ToggleAsync(seriesId, watch: false, CancellationToken.None);

            Series? after = await series.GetByIdAsync(seriesId);
            Assert.NotNull(after);
            Assert.False(after!.IsWatched);
        }

        [Fact]
        public async Task ToggleAsync_UnknownSeries_DoesNotThrow()
        {
            (WatchToggleService toggle, _, _) = BuildService();
            Guid unknown = Guid.NewGuid();

            // Toggle einer nicht existierenden Serie darf nicht schmeissen — der Service
            // tolleriert den NotFound-Pfad, weil SeriesDataService.SetWatchedAsync nur
            // eine Warnung loggt und kein Werfen vorsieht.
            await toggle.ToggleAsync(unknown, watch: false, CancellationToken.None);
        }
    }
}

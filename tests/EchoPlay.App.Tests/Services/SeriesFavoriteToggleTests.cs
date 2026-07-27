using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="SeriesFavoriteToggle"/>.
    /// Sichert die Regel „Favorit impliziert Überwachung" samt Nachziehen der
    /// Neuerscheinungen ab.
    /// </summary>
    public sealed class SeriesFavoriteToggleTests
    {
        private static (IServiceScopeFactory ScopeFactory, FakeSeriesDataService Series, FakeOnlineEpisodeChecker Checker)
            BuildScopeFactory(bool offlineMode = false)
        {
            FakeSeriesDataService series = new();
            FakeOnlineEpisodeChecker checker = new();

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => series);
            _ = services.AddScoped<IOnlineEpisodeChecker>(_ => checker);
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => new FakeCachedNewReleaseDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new AppSettings { OfflineMode = offlineMode }));
            _ = services.AddSingleton<IClock>(_ => new FakeClock());

            ServiceProvider provider = services.BuildServiceProvider();
            return (provider.GetRequiredService<IServiceScopeFactory>(), series, checker);
        }

        private static async Task<Guid> AddSeriesAsync(FakeSeriesDataService series, bool favorite, bool watched)
        {
            Series s = new() { Title = "Test", IsFavorite = favorite, IsWatched = watched };
            await series.AddAsync(s, cancellationToken: TestContext.Current.CancellationToken);
            return s.Id;
        }

        [Fact]
        public async Task SetFavoriteAsync_Favorite_EnablesWatching()
        {
            (IServiceScopeFactory scopeFactory, FakeSeriesDataService series, _) = BuildScopeFactory();
            Guid seriesId = await AddSeriesAsync(series, favorite: false, watched: false);

            await SeriesFavoriteToggle.SetFavoriteAsync(scopeFactory, seriesId, isFavorite: true, CancellationToken.None);

            Series? after = await series.GetByIdAsync(seriesId, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(after);
            Assert.True(after!.IsFavorite);
            Assert.True(after.IsWatched);
        }

        [Fact]
        public async Task SetFavoriteAsync_Unfavorite_KeepsWatching()
        {
            // Das Auge bleibt Sache des Nutzers: Favorit entfernen darf die Überwachung
            // nicht stillschweigend mit abschalten.
            (IServiceScopeFactory scopeFactory, FakeSeriesDataService series, _) = BuildScopeFactory();
            Guid seriesId = await AddSeriesAsync(series, favorite: true, watched: true);

            await SeriesFavoriteToggle.SetFavoriteAsync(scopeFactory, seriesId, isFavorite: false, CancellationToken.None);

            Series? after = await series.GetByIdAsync(seriesId, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(after);
            Assert.False(after!.IsFavorite);
            Assert.True(after.IsWatched);
        }

        [Fact]
        public async Task RefreshNewReleasesAsync_KnownSeries_AsksProvider()
        {
            (IServiceScopeFactory scopeFactory, FakeSeriesDataService series, FakeOnlineEpisodeChecker checker) =
                BuildScopeFactory();
            Guid seriesId = await AddSeriesAsync(series, favorite: true, watched: true);

            await SeriesFavoriteToggle.RefreshNewReleasesAsync(scopeFactory, seriesId, CancellationToken.None);

            Assert.Equal(1, checker.CheckCallCount);
        }

        [Fact]
        public async Task RefreshNewReleasesAsync_UnknownSeries_SkipsProvider()
        {
            (IServiceScopeFactory scopeFactory, _, FakeOnlineEpisodeChecker checker) = BuildScopeFactory();

            // Feste, im Bestand nicht vergebene ID – reproduzierbar statt zufällig.
            await SeriesFavoriteToggle.RefreshNewReleasesAsync(scopeFactory, Helpers.TestIds.SeriesE, CancellationToken.None);

            Assert.Equal(0, checker.CheckCallCount);
        }

        [Fact]
        public async Task RefreshNewReleasesAsync_OfflineMode_SkipsProvider()
        {
            (IServiceScopeFactory scopeFactory, FakeSeriesDataService series, FakeOnlineEpisodeChecker checker) =
                BuildScopeFactory(offlineMode: true);
            Guid seriesId = await AddSeriesAsync(series, favorite: true, watched: true);

            await SeriesFavoriteToggle.RefreshNewReleasesAsync(scopeFactory, seriesId, CancellationToken.None);

            Assert.Equal(0, checker.CheckCallCount);
        }
    }
}

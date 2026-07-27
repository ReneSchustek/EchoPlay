using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft, dass die bereits gerenderte Startseite den Neuerscheinungen-Cache nachlädt,
    /// wenn der Hintergrund-Check nach dem Favorisieren fertig wird.
    /// </summary>
    public sealed class DashboardNewReleaseRefreshTests
    {
        private static (DashboardViewModel Vm, NewReleaseEventService Events, FakeCachedNewReleaseDataService Cache)
            BuildViewModel(FakeSeriesDataService seriesService)
        {
            FakeCachedNewReleaseDataService cache = new();
            NewReleaseEventService events = new();

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IDashboardPositionDataService>(_ => new FakeDashboardPositionDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(null));
            _ = services.AddScoped<IOnlineEpisodeChecker>(_ => new FakeOnlineEpisodeChecker());
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => cache);
            ServiceProvider provider = services.BuildServiceProvider();

            DashboardViewModel vm = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                new FakePlayerService(),
                new FakeLoggerFactory(),
                clock: new FakeClock(),
                newReleaseEventService: events);

            return (vm, events, cache);
        }

        [Fact]
        public async Task CacheChanged_ReloadsNewReleaseSection()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG", IsSubscribed = true, IsFavorite = true, IsWatched = true };
            await seriesService.AddAsync(series, cancellationToken: TestContext.Current.CancellationToken);

            (DashboardViewModel vm, NewReleaseEventService events, FakeCachedNewReleaseDataService cache) =
                BuildViewModel(seriesService);

            await vm.LoadAsync();
            Assert.Empty(vm.NewEpisodeGroups);

            // Der Hintergrund-Check füllt den Cache erst nach dem Rendern der Seite.
            await cache.UpsertRangeAsync(
                [
                    new CachedNewRelease
                    {
                        SeriesId = series.Id,
                        Series = series,
                        Title = "TKKG - Folge 231",
                        EpisodeNumber = 231,
                        ReleaseDate = new FakeClock().UtcNow.AddDays(-3),
                        CollectionId = 4711,
                        CheckedAtUtc = new FakeClock().UtcNow
                    }
                ],
                cancellationToken: TestContext.Current.CancellationToken);

            events.RaiseCacheChanged();

            // Ohne Dispatcher (Unit-Test) lädt das VM direkt – auf den Abschluss warten.
            await ChangeSignals.WaitForCollectionAsync(
                vm.NewEpisodeGroups,
                () => vm.NewEpisodeGroups.Count > 0,
                "Startseite lädt die Neuerscheinungen nach dem Cache-Ereignis nach");

            _ = Assert.Single(vm.NewEpisodeGroups);
        }

        [Fact]
        public async Task Dispose_UnsubscribesFromCacheChanged()
        {
            FakeSeriesDataService seriesService = new();
            (DashboardViewModel vm, NewReleaseEventService events, _) = BuildViewModel(seriesService);

            await vm.LoadAsync();
            vm.Dispose();

            // Nach Dispose darf das Event kein Nachladen mehr auslösen – das VM ist entsorgt,
            // sein Lifecycle-Token abgebrochen.
            events.RaiseCacheChanged();
        }
    }
}

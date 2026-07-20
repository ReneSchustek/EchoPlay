using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="StatistikViewModel"/>. Fokus: keine N+1-Queries beim
    /// Episodenzähler-Aufbau.
    /// </summary>
    public sealed class StatistikViewModelTests
    {
        [Fact]
        public async Task LoadAsync_TenSubscribedSeries_TriggersSingleEpisodeCountsCall()
        {
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakePlaybackStateDataService stateService = new();

            for (int i = 1; i <= 10; i++)
            {
                await seriesService.AddAsync(
                    new Series { Title = $"Serie {i}", IsSubscribed = true },
                    cancellationToken: TestContext.Current.CancellationToken);
            }

            StatistikViewModel vm = BuildViewModel(seriesService, episodeService, stateService);
            await vm.LoadAsync();

            // Keine N+1: GetBySeriesIdAsync darf nicht in der Schleife laufen.
            Assert.Equal(0, episodeService.GetBySeriesIdAsyncCallCount);
            // Statt dessen: ein einziger GroupBy-Server-Aufruf.
            Assert.Equal(1, episodeService.GetEpisodeCountsForSeriesAsyncCallCount);
            Assert.Equal(10, vm.SeriesCount);
        }

        [Fact]
        public async Task LoadAsync_NoSubscribedSeries_StillSingleCountsCall()
        {
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakePlaybackStateDataService stateService = new();

            StatistikViewModel vm = BuildViewModel(seriesService, episodeService, stateService);
            await vm.LoadAsync();

            // Auch bei leerer Liste: ein Aufruf, der intern auf leere Antwort kurzschliesst.
            Assert.Equal(1, episodeService.GetEpisodeCountsForSeriesAsyncCallCount);
            Assert.Equal(0, vm.SeriesCount);
            Assert.Equal(0, vm.EpisodeCount);
        }

        private static StatistikViewModel BuildViewModel(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            FakePlaybackStateDataService stateService)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<IPlaybackStateDataService>(_ => stateService);

            ServiceProvider provider = services.BuildServiceProvider();
            return new StatistikViewModel(provider.GetRequiredService<IServiceScopeFactory>());
        }
    }
}

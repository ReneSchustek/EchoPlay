using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
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

            // Auch bei leerer Liste: ein Aufruf, der intern auf leere Antwort kurzschließt.
            Assert.Equal(1, episodeService.GetEpisodeCountsForSeriesAsyncCallCount);
            Assert.Equal(0, vm.SeriesCount);
            Assert.Equal(0, vm.EpisodeCount);
        }

        [Fact]
        public async Task LoadAsync_WithEpisodes_ReportsCountsAndProgress()
        {
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakePlaybackStateDataService stateService = new();

            Series series = new() { Title = "TKKG", IsSubscribed = true };
            await seriesService.AddAsync(series, cancellationToken: TestContext.Current.CancellationToken);
            for (int i = 1; i <= 4; i++)
            {
                Episode episode = new() { Title = $"Folge {i}", SeriesId = series.Id };
                await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);
                if (i == 1)
                {
                    await stateService.AddAsync(new PlaybackState { EpisodeId = episode.Id, IsCompleted = true }, TestContext.Current.CancellationToken);
                }
            }

            StatistikViewModel vm = BuildViewModel(seriesService, episodeService, stateService);
            await vm.LoadAsync();

            Assert.Equal(4, vm.EpisodeCount);
            Assert.Equal(1, vm.CompletedCount);
            Assert.Equal(25, vm.ProgressPercent);
            // Im Testhost greift der Rückfall-Text; geprüft wird, dass die Zahlen eingesetzt sind.
            Assert.Contains("1", vm.ProgressText, System.StringComparison.Ordinal);
            Assert.Contains("4", vm.ProgressText, System.StringComparison.Ordinal);
            Assert.Contains("25", vm.ProgressText, System.StringComparison.Ordinal);
        }

        [Fact]
        public async Task LoadAsync_WithoutEpisodes_ShowsTheEmptyHint()
        {
            StatistikViewModel vm = BuildViewModel(new FakeSeriesDataService(), new FakeEpisodeDataService(), new FakePlaybackStateDataService());
            await vm.LoadAsync();

            Assert.Equal(0, vm.ProgressPercent);
            Assert.False(string.IsNullOrEmpty(vm.ProgressText));
            Assert.DoesNotContain("%", vm.ProgressText, System.StringComparison.Ordinal);
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

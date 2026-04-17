using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="StatusBarViewModel"/>.
    /// Prüft die korrekten Statistiken in der Info-Leiste des Hauptfensters.
    /// </summary>
    public sealed class StatusBarViewModelTests
    {
        private static (StatusBarViewModel Vm, FakePlaybackStateDataService StateService) BuildViewModel(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            FakePlaybackStateDataService stateService)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<IPlaybackStateDataService>(_ => stateService);
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            StatusBarViewModel vm = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeThemeService(),
                new EchoPlay.App.Services.TaskbarProgressService(),
                new FakeClock());

            return (vm, stateService);
        }

        [Fact]
        public async Task LoadAsync_CountsSubscribedSeries()
        {
            // Nur abonnierte Serien zählen für die Info-Leiste
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true });
            await seriesService.AddAsync(new Series { Title = "Bibi", IsSubscribed = true });
            await seriesService.AddAsync(new Series { Title = "Globi", IsSubscribed = false });

            (StatusBarViewModel vm, _) = BuildViewModel(
                seriesService,
                new FakeEpisodeDataService(),
                new FakePlaybackStateDataService());

            await vm.LoadAsync();

            Assert.Equal(2, vm.SubscribedSeriesCount);
        }

        [Fact]
        public async Task LoadAsync_CountsFinishedEpisodes()
        {
            // Episoden mit IsCompleted=true werden als gehört gezählt
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true });
            Guid seriesId = seriesService.All[0].Id;

            Episode ep1 = new() { Title = "Folge 1", SeriesId = seriesId };
            Episode ep2 = new() { Title = "Folge 2", SeriesId = seriesId };
            Episode ep3 = new() { Title = "Folge 3", SeriesId = seriesId };
            await episodeService.AddAsync(ep1);
            await episodeService.AddAsync(ep2);
            await episodeService.AddAsync(ep3);

            List<PlaybackState> states =
            [
                new PlaybackState { EpisodeId = ep1.Id, IsCompleted = true,  LastPosition = TimeSpan.Zero },
                new PlaybackState { EpisodeId = ep2.Id, IsCompleted = true,  LastPosition = TimeSpan.Zero },
                new PlaybackState { EpisodeId = ep3.Id, IsCompleted = false, LastPosition = TimeSpan.FromSeconds(30) },
            ];

            (StatusBarViewModel vm, _) = BuildViewModel(
                seriesService,
                episodeService,
                new FakePlaybackStateDataService(states));

            await vm.LoadAsync();

            Assert.Equal(2, vm.FinishedEpisodesCount);
            Assert.Equal(1, vm.UnfinishedEpisodesCount);
        }

        [Fact]
        public async Task LoadAsync_CountsNewEpisodes()
        {
            // "Neu" = erschienen (ReleaseDate ≤ heute), aber noch nicht gehört
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series { Title = "Die drei ???", IsSubscribed = true });
            Guid seriesId = seriesService.All[0].Id;

            // Gestern erschienen, noch nicht gehört → zählt als neu
            Episode epNeu = new()
            {
                Title = "Folge 1",
                SeriesId = seriesId,
                ReleaseDate = TestIds.ReferenceDate.AddDays(-1)
            };
            // Zukünftig → zählt nicht als neu
            Episode epKommend = new()
            {
                Title = "Folge 2",
                SeriesId = seriesId,
                ReleaseDate = TestIds.ReferenceDate.AddDays(7)
            };

            await episodeService.AddAsync(epNeu);
            await episodeService.AddAsync(epKommend);

            (StatusBarViewModel vm, _) = BuildViewModel(
                seriesService,
                episodeService,
                new FakePlaybackStateDataService());

            await vm.LoadAsync();

            Assert.Equal(1, vm.NewEpisodesCount);
        }

        [Fact]
        public async Task RefreshAsync_UpdatesAfterStatusChange()
        {
            // Nach einer Statusänderung muss RefreshAsync die Zähler aktualisieren
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakePlaybackStateDataService stateService = new();

            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true });
            Guid seriesId = seriesService.All[0].Id;

            Episode ep = new() { Title = "Folge 1", SeriesId = seriesId };
            await episodeService.AddAsync(ep);

            (StatusBarViewModel vm, _) = BuildViewModel(seriesService, episodeService, stateService);
            await vm.LoadAsync();

            // Vor dem Refresh: keine abgeschlossene Episode
            Assert.Equal(0, vm.FinishedEpisodesCount);

            // Status nachtragen und Statistik aktualisieren
            await stateService.AddAsync(new PlaybackState
            {
                EpisodeId = ep.Id,
                IsCompleted = true,
                LastPosition = TimeSpan.Zero
            });

            await vm.RefreshAsync();

            Assert.Equal(1, vm.FinishedEpisodesCount);
        }
    }
}

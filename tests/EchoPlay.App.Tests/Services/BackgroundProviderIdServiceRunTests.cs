using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für den Anreicherungslauf des <see cref="BackgroundProviderIdService"/>.
    /// Er trägt die Apple-Music-Album-ID nach, die später das Öffnen einer Folge beim
    /// Anbieter möglich macht. Die Zerlegung der URL prüft
    /// <see cref="BackgroundProviderIdServiceTests"/>.
    /// </summary>
    public sealed class BackgroundProviderIdServiceRunTests
    {
        private static (BackgroundProviderIdService Service, FakeEpisodeDataService Episodes) Build(
            ProviderType provider,
            params Episode[] episodes)
        {
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(
                _ => new FakeAppSettingsDataService(new AppSettings { ActiveProvider = provider }));
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);

            ServiceProvider provider2 = services.BuildServiceProvider();

            Series series = new() { Title = "Testserie", IsSubscribed = true };
            seriesService.AddAsync(series).GetAwaiter().GetResult();

            foreach (Episode episode in episodes)
            {
                episode.SeriesId = series.Id;
                episodeService.AddAsync(episode).GetAwaiter().GetResult();
            }

            BackgroundProviderIdService service = new(
                provider2.GetRequiredService<IServiceScopeFactory>(),
                new SemaphoreHostRateLimiter(new Dictionary<string, TimeSpan>()),
                new FakeLoggerFactory());

            return (service, episodeService);
        }

        [Fact]
        public async Task RunOnceAsync_WithoutProvider_DoesNothing()
        {
            (BackgroundProviderIdService service, FakeEpisodeDataService episodes) = Build(
                ProviderType.None,
                new Episode { Title = "Folge 1", ProviderUrl = "https://music.apple.com/de/album/x/id123" });

            await service.RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Null(episodes.All[0].AppleMusicAlbumId);
        }

        [Fact]
        public async Task RunOnceAsync_AppleMusic_FillsAlbumIdFromProviderUrl()
        {
            (BackgroundProviderIdService service, FakeEpisodeDataService episodes) = Build(
                ProviderType.AppleMusic,
                new Episode { Title = "Folge 1", ProviderUrl = "https://music.apple.com/de/album/titel/id1234567" });

            await service.RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Equal("1234567", episodes.All[0].AppleMusicAlbumId);
        }

        [Fact]
        public async Task RunOnceAsync_ExistingAlbumId_IsNotOverwritten()
        {
            (BackgroundProviderIdService service, FakeEpisodeDataService episodes) = Build(
                ProviderType.AppleMusic,
                new Episode
                {
                    Title = "Folge 1",
                    ProviderUrl = "https://music.apple.com/de/album/titel/id999",
                    AppleMusicAlbumId = "111"
                });

            await service.RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Equal("111", episodes.All[0].AppleMusicAlbumId);
        }

        [Fact]
        public async Task RunOnceAsync_WithoutProviderUrl_LeavesEpisodeUntouched()
        {
            (BackgroundProviderIdService service, FakeEpisodeDataService episodes) = Build(
                ProviderType.AppleMusic,
                new Episode { Title = "Nur lokal", ProviderUrl = null });

            await service.RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Null(episodes.All[0].AppleMusicAlbumId);
        }

        [Fact]
        public async Task RunOnceAsync_CalledTwice_IsIdempotent()
        {
            (BackgroundProviderIdService service, FakeEpisodeDataService episodes) = Build(
                ProviderType.Both,
                new Episode { Title = "Folge 1", ProviderUrl = "https://music.apple.com/de/album/titel/id42" });

            await service.RunOnceAsync(TestContext.Current.CancellationToken);
            await service.RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Equal("42", episodes.All[0].AppleMusicAlbumId);
        }

        [Fact]
        public void Dispose_WithoutStart_IsHarmless()
        {
            (BackgroundProviderIdService service, _) = Build(ProviderType.None);

            service.Dispose();
            service.Dispose();
        }
    }
}

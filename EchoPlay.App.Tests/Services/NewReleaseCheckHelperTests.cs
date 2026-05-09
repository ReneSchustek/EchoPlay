using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="NewReleaseCheckHelper"/>.
    /// </summary>
    public sealed class NewReleaseCheckHelperTests
    {
        [Fact]
        public async Task CheckAndCacheSingleSeriesAsync_OfflineMode_DoesNothing()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new AppSettings { OfflineMode = true }));
            _ = services.AddScoped<IOnlineEpisodeChecker>(_ => new FakeOnlineEpisodeChecker());
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => new FakeCachedNewReleaseDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            Series series = new() { Title = "Test" };

            // Darf nicht crashen und keine API-Calls machen
            await NewReleaseCheckHelper.CheckAndCacheSingleSeriesAsync(series, provider, cancellationToken: TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task CheckAndCacheSingleSeriesAsync_OnlineMode_ExecutesCheck()
        {
            FakeCachedNewReleaseDataService cacheService = new();

            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new AppSettings { OfflineMode = false }));
            _ = services.AddScoped<IOnlineEpisodeChecker>(_ => new FakeOnlineEpisodeChecker());
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => cacheService);
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            Series series = new() { Title = "Test" };

            // Darf nicht crashen – FakeOnlineEpisodeChecker gibt leere Ergebnisse zurück
            await NewReleaseCheckHelper.CheckAndCacheSingleSeriesAsync(series, provider, cancellationToken: TestContext.Current.CancellationToken);
        }
    }
}

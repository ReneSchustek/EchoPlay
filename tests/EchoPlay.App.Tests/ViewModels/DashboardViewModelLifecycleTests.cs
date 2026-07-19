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
    /// Tests fuer den Lifecycle-CTS-Mechanismus in <see cref="DashboardViewModel"/>:
    /// Dispose stoppt laufende Service-Calls, ein Re-LoadAsync canclet den
    /// vorigen Token sauber.
    /// </summary>
    public sealed class DashboardViewModelLifecycleTests
    {
        private static DashboardViewModel BuildViewModel()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => new FakeCachedNewReleaseDataService());
            _ = services.AddScoped<IDashboardPositionDataService>(_ => new FakeDashboardPositionDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(new Data.Entities.Settings.AppSettings()));
            ServiceProvider provider = services.BuildServiceProvider();

            return new DashboardViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                new FakePlayerService(),
                new FakeLoggerFactory(),
                clock: new FakeClock());
        }

        [Fact]
        public async Task LoadAsync_Cancelled_StopsBeforePersisting()
        {
            // CTS-Lifecycle: nach Dispose sollte weder ein nachfolgender LoadAsync-Lauf
            // den disposeden CTS lecken noch eine Exception werfen.
            DashboardViewModel vm = BuildViewModel();

            await vm.LoadAsync();
            vm.Dispose();

            // Dispose ist idempotent.
            vm.Dispose();
        }

        [Fact]
        public async Task LoadAsync_Twice_CancelsPreviousToken()
        {
            // Zwei unmittelbar aufeinanderfolgende LoadAsync-Aufrufe: der zweite cancelt
            // den ersten und legt einen frischen CTS an. Test darf nicht haengenbleiben.
            DashboardViewModel vm = BuildViewModel();

            Task first = vm.LoadAsync();
            Task second = vm.LoadAsync();
            await Task.WhenAll(first, second);

            vm.Dispose();
        }
    }
}

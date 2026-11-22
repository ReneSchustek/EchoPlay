using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft, dass die schon gezeichnete Startseite ihren Kalenderabschnitt nachlädt, sobald der
    /// Hintergrunddienst neue Einträge meldet, und dass sie sich beim Freigeben abmeldet.
    /// </summary>
    public sealed class DashboardReleaseCalendarRefreshTests
    {
        // Der Kalenderabschnitt rechnet mit der Uhr der Startseite; der Eintrag liegt im selben Monat.
        private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        /// <summary>Prüfergebnis mit einer Folge, die fünf Tage nach „jetzt“ erscheint.</summary>
        private static OnlineEpisodeCheckResult AnnouncedRelease(Series series)
            => new()
            {
                SeriesId = series.Id,
                SeriesTitle = series.Title,
                NewReleaseEpisodes =
                [
                    new NewReleaseEpisode
                    {
                        Title = "Folge 231: Der Neue",
                        EpisodeNumber = 231,
                        ReleaseDate = Now.AddDays(5),
                        CollectionId = 4711
                    }
                ]
            };

        private static (DashboardViewModel Vm, ReleaseCalendarRefreshService Service) Build(Series series)
        {
            FakeSeriesDataService seriesService = new();
            seriesService.AddAsync(series).GetAwaiter().GetResult();

            OnlineEpisodeCheckResult result = AnnouncedRelease(series);

            FakeReleaseCalendarDataService calendar = new();
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IDashboardPositionDataService>(_ => new FakeDashboardPositionDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(new AppSettings { OfflineMode = false }));
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => new FakeCachedNewReleaseDataService());
            _ = services.AddScoped<IReleaseCalendarDataService>(_ => calendar);
            _ = services.AddScoped<IOnlineEpisodeChecker>(_ => new FakeOnlineEpisodeChecker([result]));
            ServiceProvider provider = services.BuildServiceProvider();

            FakeClock clock = new() { UtcNow = Now };
            ReleaseCalendarRefreshService service = new(
                provider.GetRequiredService<IServiceScopeFactory>(), clock, new FakeLoggerFactory());

            DashboardViewModel vm = new(new DashboardViewModelContext(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                new FakePlayerService(),
                new FakeLoggerFactory(),
                Clock: clock,
                ReleaseCalendarService: service));

            return (vm, service);
        }

        [Fact]
        public async Task CalendarChanged_ReloadsTheCalendarSection()
        {
            Series series = new() { Title = "TKKG", IsSubscribed = true };
            (DashboardViewModel vm, ReleaseCalendarRefreshService service) = Build(series);

            await vm.LoadAsync();
            Assert.Empty(vm.ReleaseCalendarVM.Items);

            // Ohne Oberflächen-Thread (Unit-Test) lädt die Startseite direkt nach, sobald der
            // Dienst seinen Stapel meldet.
            _ = await service.RunOnceAsync(TestContext.Current.CancellationToken);

            DateTime giveUp = DateTime.UtcNow.AddSeconds(5);
            while (vm.ReleaseCalendarVM.Items.Count == 0 && DateTime.UtcNow < giveUp)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            // Den Seriennamen lädt der echte Datendienst per Include mit; der Fake hält nur den Eintrag.
            ReleaseCalendarItemViewModel item = Assert.Single(vm.ReleaseCalendarVM.Items);
            Assert.Equal("Folge 231: Der Neue", item.EpisodeTitle);
            vm.Dispose();
        }

        [Fact]
        public async Task Dispose_UnsubscribesFromCalendarChanged()
        {
            Series series = new() { Title = "TKKG", IsSubscribed = true };
            (DashboardViewModel vm, ReleaseCalendarRefreshService service) = Build(series);
            await vm.LoadAsync();
            vm.Dispose();

            // Nach dem Freigeben darf die Meldung nichts mehr nachladen.
            _ = await service.RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Empty(vm.ReleaseCalendarVM.Items);
        }

        [Fact]
        public async Task CalendarChanged_InOfflineMode_LeavesTheSectionAlone()
        {
            Series series = new() { Title = "TKKG", IsSubscribed = true };
            (DashboardViewModel vm, ReleaseCalendarRefreshService service) = Build(series);
            await vm.LoadAsync();
            vm.ReleaseCalendarVM.IsAvailable = false;

            _ = await service.RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.Empty(vm.ReleaseCalendarVM.Items);
            vm.Dispose();
        }
    }
}

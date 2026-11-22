using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für Start, Schleife und Stopp des <see cref="ReleaseCalendarRefreshService"/>.
    /// Die künstliche Uhr ersetzt die Minute vor dem ersten Lauf und die Stunde dazwischen.
    /// </summary>
    public sealed class ReleaseCalendarRefreshLoopTests
    {
        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        // Länger als jede Wartezeit der Schleife; die Uhr wird in diesen Schritten gedreht.
        private static readonly TimeSpan ClockStep = TimeSpan.FromMinutes(61);

        // Echte Obergrenze, falls eine Bedingung nie eintritt.
        /// <summary>Einstellungsdienst, der beim ersten Lesen scheitert und danach liefert.</summary>
        private sealed class FailingOnceSettings : IAppSettingsDataService
        {
            private int _calls;

            public int Calls => Volatile.Read(ref _calls);

            public Task<AppSettings> GetAsync(CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref _calls) == 1)
                {
                    throw new InvalidOperationException("Datenbank gesperrt");
                }

                return Task.FromResult(new AppSettings { OfflineMode = false });
            }

            public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        /// <summary>Prüfer, der hängt, bis der Test ihn freigibt, und dabei den Abbruch übergeht.</summary>
        private sealed class BlockingChecker : IOnlineEpisodeChecker
        {
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckAllAsync(
                IReadOnlyList<CheckableSeriesInfo> subscribedSeries, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public async Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckNewReleasesAsync(
                IReadOnlyList<CheckableSeriesInfo> subscribedSeries, DateTime cutoffDate, CancellationToken cancellationToken = default)
            {
                _ = Entered.TrySetResult();
                await Release.Task;
                return [];
            }
        }

        private static (ReleaseCalendarRefreshService Service, FakeTimeProvider Time, CapturingLogger Log) Build(
            IAppSettingsDataService settings, IOnlineEpisodeChecker checker, FakeReleaseCalendarDataService calendar)
        {
            FakeSeriesDataService series = new();
            series.AddAsync(new Series { Title = "TKKG" }).GetAwaiter().GetResult();

            ServiceCollection services = new();
            _ = services.AddScoped(_ => settings);
            _ = services.AddScoped<ISeriesDataService>(_ => series);
            _ = services.AddScoped<IReleaseCalendarDataService>(_ => calendar);
            _ = services.AddScoped(_ => checker);
            ServiceProvider provider = services.BuildServiceProvider();

            FakeTimeProvider time = new(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));
            CapturingLogger logger = new();
            CapturingLoggerFactory loggerFactory = new(logger);
            ReleaseCalendarRefreshService service = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeClock { UtcNow = time.GetUtcNow().UtcDateTime },
                loggerFactory,
                Berlin,
                time);
            return (service, time, logger);
        }

        /// <summary>Dreht die Uhr, bis die Bedingung eintritt, und gibt den Fortsetzungen dazwischen Zeit.</summary>
        private static Task AdvanceUntilAsync(FakeTimeProvider time, Func<bool> condition)
            => Eventually.TrueAsync(condition, () => time.Advance(ClockStep));

        [Fact]
        public async Task Start_AfterTheInitialDelay_BuildsTheCalendar()
        {
            FakeReleaseCalendarDataService calendar = new();
            FakeOnlineEpisodeChecker checker = new();
            (ReleaseCalendarRefreshService service, FakeTimeProvider time, _) =
                Build(new FakeAppSettingsDataService(new AppSettings { OfflineMode = false }), checker, calendar);

            service.Start();
            service.Start();
            await AdvanceUntilAsync(time, () => calendar.LastRefresh is not null);
            await service.StopAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // Ein zweites Start hat keine zweite Schleife angelegt: genau ein Aufbau.
            Assert.Equal(1, checker.CheckCallCount);
        }

        [Fact]
        public async Task Loop_WhenARunFails_LogsAndTriesAgainAfterTheInterval()
        {
            FakeReleaseCalendarDataService calendar = new();
            FailingOnceSettings settings = new();
            (ReleaseCalendarRefreshService service, FakeTimeProvider time, CapturingLogger log) =
                Build(settings, new FakeOnlineEpisodeChecker(), calendar);

            service.Start();
            await AdvanceUntilAsync(time, () => calendar.LastRefresh is not null);
            await service.StopAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.True(settings.Calls >= 2);
            Assert.Contains(log.Entries, e => e.Level == "Error");
        }

        [Fact]
        public async Task StopAsync_WhenTheRunIgnoresCancellation_GivesUpAfterTheTimeout()
        {
            BlockingChecker checker = new();
            (ReleaseCalendarRefreshService service, FakeTimeProvider time, CapturingLogger log) =
                Build(new FakeAppSettingsDataService(new AppSettings { OfflineMode = false }), checker, new FakeReleaseCalendarDataService());

            service.Start();
            await AdvanceUntilAsync(time, () => checker.Entered.Task.IsCompleted);

            await service.StopAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
            _ = checker.Release.TrySetResult();

            Assert.Contains(log.Entries, e => e.Level == "Warning");
        }

        [Fact]
        public async Task StopAsync_WithoutStart_DoesNothing()
        {
            (ReleaseCalendarRefreshService service, _, _) =
                Build(new FakeAppSettingsDataService(), new FakeOnlineEpisodeChecker(), new FakeReleaseCalendarDataService());

            await service.StopAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            service.Dispose();

            Assert.False(service.IsRefreshing);
        }

        [Fact]
        public void Dispose_AfterStart_EndsTheLoop()
        {
            (ReleaseCalendarRefreshService service, _, _) =
                Build(new FakeAppSettingsDataService(), new FakeOnlineEpisodeChecker(), new FakeReleaseCalendarDataService());

            service.Start();
            service.Dispose();

            // Ein zweites Dispose darf nichts mehr freigeben wollen.
            service.Dispose();
            Assert.False(service.IsRefreshing);
        }
    }
}

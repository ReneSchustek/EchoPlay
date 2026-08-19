using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="NewReleaseCheckHelper"/> — die Sofortprüfung, die läuft,
    /// sobald der Anwender eine Serie unter Beobachtung stellt.
    /// </summary>
    public sealed class NewReleaseCheckHelperTests
    {
        private static readonly DateTime LastStart = new(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc);

        [Fact]
        public async Task CheckAndCacheSingleSeriesAsync_InOfflineMode_NeverAsksTheProvider()
        {
            FakeOnlineEpisodeChecker checker = new();
            ServiceProvider provider = BuildProvider(
                new AppSettings { OfflineMode = true }, checker, new FakeCachedNewReleaseDataService());

            await NewReleaseCheckHelper.CheckAndCacheSingleSeriesAsync(
                new Series { Title = "TKKG" }, provider, TestContext.Current.CancellationToken);

            // Im Offline-Betrieb darf die Anwendung nicht nach draußen telefonieren —
            // auch nicht für eine einzelne Serie.
            Assert.Equal(0, checker.CheckCallCount);
        }

        [Fact]
        public async Task CheckAndCacheSingleSeriesAsync_InOnlineMode_AsksTheProviderForThatSeries()
        {
            FakeOnlineEpisodeChecker checker = new();
            ServiceProvider provider = BuildProvider(
                new AppSettings { OfflineMode = false, NewReleaseDays = 30, LastAppStart = LastStart },
                checker,
                new FakeCachedNewReleaseDataService());
            Series series = new() { Title = "TKKG", AppleMusicArtistId = "am_tkkg" };

            await NewReleaseCheckHelper.CheckAndCacheSingleSeriesAsync(
                series, provider, TestContext.Current.CancellationToken);

            Assert.Equal(1, checker.CheckCallCount);
            CheckableSeriesInfo asked = Assert.Single(checker.LastCheckedSeries);
            Assert.Equal("TKKG", asked.Title);
            Assert.Equal("am_tkkg", asked.AppleMusicArtistId);
        }

        [Fact]
        public async Task CheckAndCacheSingleSeriesAsync_LooksBackFromTheLastStartByTheConfiguredDays()
        {
            FakeOnlineEpisodeChecker checker = new();
            ServiceProvider provider = BuildProvider(
                new AppSettings { OfflineMode = false, NewReleaseDays = 14, LastAppStart = LastStart },
                checker,
                new FakeCachedNewReleaseDataService());

            await NewReleaseCheckHelper.CheckAndCacheSingleSeriesAsync(
                new Series { Title = "TKKG" }, provider, TestContext.Current.CancellationToken);

            // Der Stichtag entscheidet, was als „neu" gilt. Läge er auf dem heutigen Tag,
            // fände die Prüfung nie etwas.
            Assert.Equal(LastStart.AddDays(-14), checker.LastCutoffDate);
        }

        [Fact]
        public async Task CheckAndCacheSingleSeriesAsync_WithoutLastStart_LooksBackFromNow()
        {
            FakeOnlineEpisodeChecker checker = new();
            FakeClock clock = new();
            ServiceProvider provider = BuildProvider(
                new AppSettings { OfflineMode = false, NewReleaseDays = 7, LastAppStart = null },
                checker,
                new FakeCachedNewReleaseDataService(),
                clock);

            await NewReleaseCheckHelper.CheckAndCacheSingleSeriesAsync(
                new Series { Title = "TKKG" }, provider, TestContext.Current.CancellationToken);

            Assert.Equal(clock.UtcNow.AddDays(-7), checker.LastCutoffDate);
        }

        [Fact]
        public async Task CheckAndCacheSingleSeriesAsync_WithNewReleases_PutsThemIntoTheCache()
        {
            Series series = new() { Title = "TKKG" };
            FakeOnlineEpisodeChecker checker = new(
            [
                new OnlineEpisodeCheckResult
                {
                    SeriesId = series.Id,
                    SeriesTitle = series.Title,
                    NewReleaseEpisodes =
                    [
                        new NewReleaseEpisode
                        {
                            Title = "TKKG - Folge 240 - Der neue Fall",
                            EpisodeNumber = 240,
                            ReleaseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
                            CoverUrl = "https://example.invalid/cover.jpg",
                            CollectionId = 4711,
                        },
                    ],
                },
            ]);
            FakeCachedNewReleaseDataService cache = new();
            ServiceProvider provider = BuildProvider(
                new AppSettings { OfflineMode = false, NewReleaseDays = 30, LastAppStart = LastStart },
                checker, cache);

            await NewReleaseCheckHelper.CheckAndCacheSingleSeriesAsync(
                series, provider, TestContext.Current.CancellationToken);

            // Erst der Eintrag im Zwischenspeicher macht die Neuerscheinung beim nächsten
            // Besuch der Übersicht sichtbar — ohne ihn war der Abruf umsonst.
            IReadOnlyList<CachedNewRelease> cached = await cache.GetAllAsync(TestContext.Current.CancellationToken);
            CachedNewRelease entry = Assert.Single(cached);
            Assert.Equal("TKKG - Folge 240 - Der neue Fall", entry.Title);
            Assert.Equal(240, entry.EpisodeNumber);
            Assert.Equal(4711, entry.CollectionId);
        }

        [Fact]
        public async Task CheckAndCacheSingleSeriesAsync_WithoutNewReleases_LeavesTheCacheEmpty()
        {
            FakeCachedNewReleaseDataService cache = new();
            ServiceProvider provider = BuildProvider(
                new AppSettings { OfflineMode = false, NewReleaseDays = 30, LastAppStart = LastStart },
                new FakeOnlineEpisodeChecker(), cache);

            await NewReleaseCheckHelper.CheckAndCacheSingleSeriesAsync(
                new Series { Title = "TKKG" }, provider, TestContext.Current.CancellationToken);

            Assert.Empty(await cache.GetAllAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task CheckAndCacheSingleSeriesAsync_WhenTheProviderIsMissing_StaysSilent()
        {
            // Der Aufruf hängt am Umschalten der Beobachtung. Reißt er, bliebe die
            // Schaltfläche stehen, obwohl die Serie längst beobachtet wird.
            ServiceCollection services = new();
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddScoped<IAppSettingsDataService>(
                _ => new FakeAppSettingsDataService(new AppSettings { OfflineMode = false }));
            ServiceProvider provider = services.BuildServiceProvider();

            await NewReleaseCheckHelper.CheckAndCacheSingleSeriesAsync(
                new Series { Title = "TKKG" }, provider, TestContext.Current.CancellationToken);
        }

        private static ServiceProvider BuildProvider(
            AppSettings settings,
            FakeOnlineEpisodeChecker checker,
            FakeCachedNewReleaseDataService cache,
            FakeClock? clock = null)
        {
            ServiceCollection services = new();
            _ = services.AddSingleton<IClock>(clock ?? new FakeClock());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(settings));
            _ = services.AddScoped<IOnlineEpisodeChecker>(_ => checker);
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => cache);
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            return services.BuildServiceProvider();
        }
    }
}

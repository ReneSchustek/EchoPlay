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
    /// Prüft das Einschalten der Überwachung einer Serie.
    /// </summary>
    /// <remarks>
    /// Das Ausschalten deckt <see cref="WatchToggleServiceTests"/> ab. Beim Einschalten
    /// kommt ein Schritt hinzu, den der Anwender sonst nicht bemerkt: Die Neuerscheinungen
    /// werden sofort geholt. Ohne ihn bliebe die Startseite bis zum nächsten Programmstart
    /// leer — die Serie wäre überwacht, ohne dass es sich zeigt.
    /// </remarks>
    public sealed class WatchToggleEnableTests
    {
        [Fact]
        public async Task ToggleAsync_EnableWatch_PersistsTheFlag()
        {
            Fixture fixture = await BuildFixtureAsync(watchedBefore: false, releaseTitle: null);

            await fixture.Service.ToggleAsync(fixture.SeriesId, watch: true, TestContext.Current.CancellationToken);

            Series? stored = await fixture.Series.GetByIdAsync(fixture.SeriesId, TestContext.Current.CancellationToken);
            Assert.NotNull(stored);
            Assert.True(stored.IsWatched);
        }

        [Fact]
        public async Task ToggleAsync_EnableWatch_AsksTheProviderRightAway()
        {
            Fixture fixture = await BuildFixtureAsync(watchedBefore: false, releaseTitle: null);

            await fixture.Service.ToggleAsync(fixture.SeriesId, watch: true, TestContext.Current.CancellationToken);

            Assert.Equal(1, fixture.Checker.CheckCallCount);
        }

        [Fact]
        public async Task ToggleAsync_EnableWatch_PutsFoundReleasesIntoTheCache()
        {
            Fixture fixture = await BuildFixtureAsync(
                watchedBefore: false, releaseTitle: "TKKG - Folge 250 - Der neue Fall");

            await fixture.Service.ToggleAsync(fixture.SeriesId, watch: true, TestContext.Current.CancellationToken);

            IReadOnlyList<CachedNewRelease> cached =
                await fixture.Cache.GetAllAsync(TestContext.Current.CancellationToken);
            CachedNewRelease entry = Assert.Single(cached);
            Assert.Equal("TKKG - Folge 250 - Der neue Fall", entry.Title);
        }

        [Fact]
        public async Task ToggleAsync_EnableWatch_WhileOffline_AsksNobody()
        {
            Fixture fixture = await BuildFixtureAsync(
                watchedBefore: false, releaseTitle: null, offline: true);

            await fixture.Service.ToggleAsync(fixture.SeriesId, watch: true, TestContext.Current.CancellationToken);

            // Der Schalter darf im Offline-Betrieb wirken, ohne nach draußen zu telefonieren.
            Series? stored = await fixture.Series.GetByIdAsync(fixture.SeriesId, TestContext.Current.CancellationToken);
            Assert.NotNull(stored);
            Assert.True(stored.IsWatched);
            Assert.Equal(0, fixture.Checker.CheckCallCount);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private sealed class Fixture
        {
            public required WatchToggleService Service { get; init; }
            public required FakeSeriesDataService Series { get; init; }
            public required FakeCachedNewReleaseDataService Cache { get; init; }
            public required FakeOnlineEpisodeChecker Checker { get; init; }
            public required Guid SeriesId { get; init; }
        }

        private static async Task<Fixture> BuildFixtureAsync(
            bool watchedBefore, string? releaseTitle, bool offline = false)
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG", IsWatched = watchedBefore };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeOnlineEpisodeChecker checker = releaseTitle is null
                ? new FakeOnlineEpisodeChecker()
                : new FakeOnlineEpisodeChecker(
                [
                    new OnlineEpisodeCheckResult
                    {
                        SeriesId = series.Id,
                        SeriesTitle = series.Title,
                        NewReleaseEpisodes =
                        [
                            new NewReleaseEpisode
                            {
                                Title = releaseTitle,
                                EpisodeNumber = 250,
                                ReleaseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
                                CollectionId = 815,
                            },
                        ],
                    },
                ]);

            FakeCachedNewReleaseDataService cache = new();

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => cache);
            _ = services.AddScoped<IOnlineEpisodeChecker>(_ => checker);
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new AppSettings
                {
                    OfflineMode = offline,
                    NewReleaseDays = 30,
                    LastAppStart = new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc),
                }));
            _ = services.AddSingleton<IClock>(new FakeClock());
            ServiceProvider provider = services.BuildServiceProvider();

            return new Fixture
            {
                Service = new WatchToggleService(
                    provider.GetRequiredService<IServiceScopeFactory>(), new FakeLoggerFactory()),
                Series = seriesService,
                Cache = cache,
                Checker = checker,
                SeriesId = series.Id,
            };
        }
    }
}

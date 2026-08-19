using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft den Aufbau des Neuerscheinungs-Abschnitts aus dem Zwischenspeicher.
    /// </summary>
    /// <remarks>
    /// Der Zwischenspeicher überlebt das Abschalten der Beobachtung. Würde er ungefiltert
    /// angezeigt, sähe der Anwender Neuerscheinungen zu Serien, die er ausdrücklich nicht
    /// mehr verfolgt — und der Abschnitt verlöre seinen Sinn.
    /// </remarks>
    public sealed class NewReleaseGroupBuilderTests
    {
        [Fact]
        public async Task Build_WithoutCachedEntries_ReturnsNoGroups()
        {
            Fixture fixture = Build();

            IReadOnlyList<NewEpisodesGroupViewModel> groups =
                await fixture.Builder.BuildAsync([], TestContext.Current.CancellationToken);

            Assert.Empty(groups);
        }

        [Fact]
        public async Task Build_ForAnUnwatchedSeries_LeavesItOut()
        {
            Series series = new() { Title = "TKKG", IsWatched = false };
            Fixture fixture = Build(CachedRelease(series.Id, "TKKG - Folge 240"));

            IReadOnlyList<NewEpisodesGroupViewModel> groups =
                await fixture.Builder.BuildAsync([series], TestContext.Current.CancellationToken);

            Assert.Empty(groups);
        }

        [Fact]
        public async Task Build_ForASeriesThatIsNoLongerKnown_LeavesItOut()
        {
            Fixture fixture = Build(CachedRelease(
                new Guid("99999999-9999-9999-9999-999999999999"), "Fremde Folge"));

            IReadOnlyList<NewEpisodesGroupViewModel> groups =
                await fixture.Builder.BuildAsync([], TestContext.Current.CancellationToken);

            // Ein Eintrag ohne passende Serie stammt aus einem gelöschten Bestand.
            Assert.Empty(groups);
        }

        [Fact]
        public async Task Build_ForAWatchedSeries_ShowsTheReleaseInItsMonth()
        {
            Series series = new() { Title = "TKKG", IsWatched = true };
            Fixture fixture = Build(CachedRelease(series.Id, "TKKG - Folge 240"));

            IReadOnlyList<NewEpisodesGroupViewModel> groups =
                await fixture.Builder.BuildAsync([series], TestContext.Current.CancellationToken);

            NewEpisodesGroupViewModel group = Assert.Single(groups);
            _ = Assert.Single(group.Episodes);
            Assert.NotEmpty(group.GroupLabel);
        }

        [Fact]
        public async Task Build_WhenTheEpisodeIsAlreadyKnownButUnheard_StillShowsIt()
        {
            Series series = new() { Title = "TKKG", IsWatched = true };
            FakeEpisodeDataService episodes = new();
            Episode local = new() { SeriesId = series.Id, Title = "TKKG - Folge 240", EpisodeNumber = 240 };
            await episodes.AddAsync(local, TestContext.Current.CancellationToken);

            Fixture fixture = Build(episodes, new FakePlaybackStateDataService(),
                CachedRelease(series.Id, "TKKG - Folge 240"));

            IReadOnlyList<NewEpisodesGroupViewModel> groups =
                await fixture.Builder.BuildAsync([series], TestContext.Current.CancellationToken);

            // Heruntergeladen heißt nicht gehört — die Folge bleibt eine Neuerscheinung,
            // bis der Anwender sie durch hat.
            NewEpisodesGroupViewModel group = Assert.Single(groups);
            _ = Assert.Single(group.Episodes);
        }

        [Fact]
        public async Task Build_WhenTheEpisodeIsAlreadyHeard_LeavesItOut()
        {
            Series series = new() { Title = "TKKG", IsWatched = true };
            FakeEpisodeDataService episodes = new();
            Episode local = new() { SeriesId = series.Id, Title = "TKKG - Folge 240", EpisodeNumber = 240 };
            await episodes.AddAsync(local, TestContext.Current.CancellationToken);

            FakePlaybackStateDataService states = new(states:
            [
                new EchoPlay.Data.Entities.Playback.PlaybackState
                {
                    EpisodeId = local.Id,
                    IsCompleted = true,
                },
            ]);

            Fixture fixture = Build(episodes, states, CachedRelease(series.Id, "TKKG - Folge 240"));

            IReadOnlyList<NewEpisodesGroupViewModel> groups =
                await fixture.Builder.BuildAsync([series], TestContext.Current.CancellationToken);

            // Was durchgehört ist, gehört nicht mehr unter die Neuerscheinungen.
            Assert.Empty(groups);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private sealed class Fixture
        {
            public required NewReleaseGroupBuilder Builder { get; init; }
        }

        private static CachedNewRelease CachedRelease(Guid seriesId, string title)
            => new()
            {
                SeriesId = seriesId,
                Title = title,
                EpisodeNumber = 240,
                ReleaseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
                CheckedAtUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            };

        private static Fixture Build(params CachedNewRelease[] cached)
            => Build(new FakeEpisodeDataService(), new FakePlaybackStateDataService(), cached);

        private static Fixture Build(
            FakeEpisodeDataService episodes,
            FakePlaybackStateDataService states,
            params CachedNewRelease[] cached)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => episodes);
            _ = services.AddScoped<IPlaybackStateDataService>(_ => states);
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => new FakeLocalCoverLoader());
            _ = services.AddScoped<ICachedNewReleaseDataService>(
                _ => new FakeCachedNewReleaseDataService(cached));

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            DashboardCoverProvider coverProvider = new(
                scopeFactory, coverService: null, backgroundCoverService: null, dispatcherQueue: null);

            NewEpisodeCardServices cardServices = new(
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                new FakePlayerService(),
                new FakeLocalizationService());

            return new Fixture
            {
                Builder = new NewReleaseGroupBuilder(
                    scopeFactory, coverProvider, cardServices, new FakeClock(), new FakeLogger()),
            };
        }
    }
}

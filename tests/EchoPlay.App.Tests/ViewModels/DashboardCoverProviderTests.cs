using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft, wie die Startseite ihre Folgen-Cover beschafft: ein Zwischenspeicher je
    /// Durchgang, eine Vormerkung für alles, was fehlt, und ein Nachreichen im Hintergrund.
    /// </summary>
    /// <remarks>
    /// Die Startseite zeigt bis zu vierzig Kacheln. Ohne den Zwischenspeicher fragte jede
    /// einzeln nach ihrem Bild; ohne die Vormerkung bekäme keine ihr Bild jemals nachgereicht.
    /// </remarks>
    public sealed class DashboardCoverProviderTests
    {
        [Fact]
        public async Task BeginSession_ReadsTheKnownCoversInOneGo()
        {
            Fixture fixture = Build();

            await fixture.Provider.BeginSessionAsync(
                [TestIds.EpisodeA, TestIds.EpisodeB], TestContext.Current.CancellationToken);

            // Eine Abfrage für alle Kacheln statt einer je Kachel — das ist der Grund,
            // warum es diesen Durchgang überhaupt gibt.
            System.Collections.Generic.IReadOnlyList<Guid> batch =
                Assert.Single(fixture.CoverImages.BatchRequests);
            Assert.Equal(2, batch.Count);
        }

        [Fact]
        public async Task BeginSession_WithoutEpisodes_AsksNothing()
        {
            Fixture fixture = Build();

            await fixture.Provider.BeginSessionAsync([], TestContext.Current.CancellationToken);

            Assert.Empty(fixture.CoverImages.BatchRequests);
        }

        [Fact]
        public async Task TryGetCachedEpisodeCover_WithoutSession_FindsNothing()
        {
            Fixture fixture = Build();

            Assert.Null(await fixture.Provider.TryGetCachedEpisodeCoverAsync(TestIds.EpisodeA));
        }

        [Fact]
        public async Task TryGetCachedEpisodeCover_ForAnEmptyId_FindsNothing()
        {
            Fixture fixture = Build();

            Assert.Null(await fixture.Provider.TryGetCachedEpisodeCoverAsync(Guid.Empty));
        }

        [Fact]
        public async Task ResolveCardCover_WithoutEpisode_FallsBackToTheSeries()
        {
            Fixture fixture = Build();

            (BitmapImage? Cover, bool HasEpisodeCover) result =
                await fixture.Provider.ResolveCardCoverAsync(new Series { Title = "TKKG" }, Guid.Empty);

            // Ohne eigenes Folgen-Cover trägt die Kachel das Serien-Cover — und gehört
            // damit ins Nachreichen.
            Assert.False(result.HasEpisodeCover);
        }

        [Fact]
        public void TrackPending_WithAnEmptyId_RemembersNothing()
        {
            Fixture fixture = Build();

            fixture.Provider.TrackPending(Guid.Empty, BuildCard(fixture));
            fixture.Provider.FlushPendingRefresh();

            Assert.Empty(fixture.CoverImages.BatchRequests);
        }

        [Fact]
        public void FlushPendingRefresh_WithoutBackgroundService_StaysQuiet()
        {
            Fixture fixture = Build(withBackgroundService: false);

            fixture.Provider.TrackPending(TestIds.EpisodeA, BuildCard(fixture));
            fixture.Provider.FlushPendingRefresh();

            Assert.Empty(fixture.CoverImages.BatchRequests);
        }

        [Fact]
        public async Task FlushPendingRefresh_HandsTheTrackedEpisodesToTheBackgroundService()
        {
            Fixture fixture = Build();
            await fixture.CoverImages.SetCoverAsync(
                CoverEntityTypes.Episode, TestIds.EpisodeA, [7, 7, 7],
                cancellationToken: TestContext.Current.CancellationToken);
            fixture.CoverImages.SignalAfterBatches(1);

            fixture.Provider.TrackPending(TestIds.EpisodeA, BuildCard(fixture));
            fixture.Provider.FlushPendingRefresh();

            // Erst dadurch bekommt eine Kachel mit Serien-Cover später ihr eigenes Bild.
            await fixture.CoverImages.BatchesReached.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.NotEmpty(fixture.CoverImages.BatchRequests);
        }

        [Fact]
        public async Task TryGetCachedEpisodeCover_AfterASessionWithStoredBytes_UsesThem()
        {
            Fixture fixture = Build();
            await fixture.CoverImages.SetCoverAsync(
                CoverEntityTypes.Episode, TestIds.EpisodeA, [1, 2, 3],
                cancellationToken: TestContext.Current.CancellationToken);

            await fixture.Provider.BeginSessionAsync(
                [TestIds.EpisodeA], TestContext.Current.CancellationToken);

            // Das Bildobjekt entsteht am Fenster und bleibt im Testlauf leer. Geprüft ist,
            // dass die Bilddaten aus dem Durchgangs-Zwischenspeicher kommen und kein
            // zweiter Griff auf die Ablage nötig ist.
            _ = await fixture.Provider.TryGetCachedEpisodeCoverAsync(TestIds.EpisodeA);

            _ = Assert.Single(fixture.CoverImages.BatchRequests);
        }

        [Fact]
        public async Task ResolveCardCover_WithAnEpisodeId_LooksForTheEpisodeCoverFirst()
        {
            Fixture fixture = Build();
            await fixture.CoverImages.SetCoverAsync(
                CoverEntityTypes.Episode, TestIds.EpisodeA, [1, 2, 3],
                cancellationToken: TestContext.Current.CancellationToken);

            await fixture.Provider.BeginSessionAsync(
                [TestIds.EpisodeA], TestContext.Current.CancellationToken);

            (BitmapImage? Cover, bool HasEpisodeCover) result =
                await fixture.Provider.ResolveCardCoverAsync(
                    new Series { Title = "TKKG" }, TestIds.EpisodeA);

            // Das eigene Folgen-Cover schlägt das Serien-Cover — sonst zeigen alle Kacheln
            // einer Serie dasselbe Bild.
            Assert.False(result.HasEpisodeCover);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private sealed class Fixture
        {
            public required DashboardCoverProvider Provider { get; init; }
            public required FakeCoverImageDataService CoverImages { get; init; }
            public required IServiceScopeFactory ScopeFactory { get; init; }
        }

        private static NewEpisodeCardViewModel BuildCard(Fixture fixture)
            => new(
                TestIds.EpisodeA,
                TestIds.SeriesA,
                "TKKG",
                "Folge 1",
                coverImage: null,
                PlaybackStatus.NotStarted,
                progressPercent: 0,
                hasLocalTrack: false,
                isAnnounced: false,
                fixture.ScopeFactory,
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                new FakePlayerService());

        private static Fixture Build(bool withBackgroundService = true)
        {
            FakeCoverImageDataService coverImages = new();
            FakeCoverDownloader downloader = new();

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImages);
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => new FakeLocalCoverLoader());
            _ = services.AddScoped<ICoverCopyService>(_ => new FakeCoverCopyService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddSingleton<ICoverDownloader>(downloader);
            _ = services.AddSingleton<EchoPlay.App.Services.CoverService>();
            _ = services.AddSingleton<ICoverService>(
                sp => sp.GetRequiredService<EchoPlay.App.Services.CoverService>());
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddSingleton<EpisodeCoverCacheService>();

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            EchoPlay.App.Services.CoverService coverService =
                provider.GetRequiredService<EchoPlay.App.Services.CoverService>();

            BackgroundCoverService? background = withBackgroundService
                ? new BackgroundCoverService(
                    scopeFactory, coverService, downloader, new FakeSpotifyCredentialStore(),
                    new BackgroundCoverServiceOptions(), new FakeLoggerFactory(), new FakeClock())
                : null;

            return new Fixture
            {
                Provider = new DashboardCoverProvider(
                    scopeFactory, coverService, background, dispatcherQueue: null),
                CoverImages = coverImages,
                ScopeFactory = scopeFactory,
            };
        }
    }
}

using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Cover-Wege der lokalen Mediathek: Suche, Übernahme roher Bilddaten und
    /// Übernahme eines gewählten Treffers.
    /// </summary>
    /// <remarks>
    /// Die eigentliche Arbeit macht der Cover-Koordinator. Hier zählt, dass die Seite ihn
    /// überhaupt erreicht — und dass sie ohne ihn still bleibt statt zu reißen. Ohne
    /// Koordinator läuft die Anwendung im abgespeckten Betrieb; ein Klick auf „Cover suchen"
    /// darf dann nichts tun, aber auch nichts kaputt machen.
    /// </remarks>
    public sealed class LocalLibraryCoverActionsTests
    {
        private static readonly CoverSearchHit Hit = new(
            "https://example.invalid/thumb.jpg", "https://example.invalid/full.jpg",
            "TKKG Folge 1", "Cover Art Archive");

        [Fact]
        public async Task SearchCoversAsync_PassesQueryAndPageToTheCoordinator()
        {
            FakeEpisodeCoverCoordinator coordinator = new() { Hits = [Hit] };
            LocalLibraryViewModel sut = BuildViewModel(coordinator);

            IReadOnlyList<CoverSearchHit> hits = await sut.Actions.SearchCoversAsync(
                "TKKG", CoverSearchPage.First, TestContext.Current.CancellationToken);

            Assert.Equal([Hit], hits);
            (string query, CoverSearchPage page) = Assert.Single(coordinator.SearchCalls);
            Assert.Equal("TKKG", query);
            Assert.Equal(0, page.Index);
        }

        [Fact]
        public async Task SearchCoversAsync_WithoutCoordinator_FindsNothing()
        {
            LocalLibraryViewModel sut = BuildViewModel(coverCoordinator: null);

            IReadOnlyList<CoverSearchHit> hits = await sut.Actions.SearchCoversAsync(
                "TKKG", CoverSearchPage.First, TestContext.Current.CancellationToken);

            Assert.Empty(hits);
        }

        [Fact]
        public async Task ApplyEpisodeCoverFromBytesAsync_HandsTheBytesToTheCoordinator()
        {
            FakeEpisodeCoverCoordinator coordinator = new();
            LocalLibraryViewModel sut = BuildViewModel(coordinator);
            LocalEpisodeCardViewModel card = BuildEpisodeCard();

            await sut.Actions.ApplyEpisodeCoverFromBytesAsync(card, [1, 2, 3]);

            (LocalEpisodeCardViewModel handed, byte[] bytes) = Assert.Single(coordinator.EpisodeCoverBytes);
            Assert.Same(card, handed);
            Assert.Equal([1, 2, 3], bytes);
        }

        [Fact]
        public async Task ApplySeriesCoverFromBytesAsync_HandsTheBytesToTheCoordinator()
        {
            FakeEpisodeCoverCoordinator coordinator = new();
            LocalLibraryViewModel sut = BuildViewModel(coordinator);
            LocalArtistCardViewModel card = BuildArtistCard();

            await sut.Actions.ApplySeriesCoverFromBytesAsync(card, [4, 5]);

            (LocalArtistCardViewModel handed, byte[] bytes) = Assert.Single(coordinator.SeriesCoverBytes);
            Assert.Same(card, handed);
            Assert.Equal([4, 5], bytes);
        }

        [Fact]
        public async Task ApplySelectedSeriesCoverAsync_HandsTheChosenHitToTheCoordinator()
        {
            FakeEpisodeCoverCoordinator coordinator = new();
            LocalLibraryViewModel sut = BuildViewModel(coordinator);
            LocalArtistCardViewModel card = BuildArtistCard();

            await sut.Actions.ApplySelectedSeriesCoverAsync(card, Hit);

            (LocalArtistCardViewModel handed, CoverSearchHit hit) = Assert.Single(coordinator.SelectedSeriesCovers);
            Assert.Same(card, handed);
            Assert.Equal(Hit, hit);
        }

        [Fact]
        public async Task ApplySelectedEpisodeCoverAsync_HandsTheChosenHitToTheCoordinator()
        {
            FakeEpisodeCoverCoordinator coordinator = new();
            LocalLibraryViewModel sut = BuildViewModel(coordinator);
            LocalEpisodeCardViewModel card = BuildEpisodeCard();

            await sut.Actions.ApplySelectedEpisodeCoverAsync(card, Hit);

            (LocalEpisodeCardViewModel handed, CoverSearchHit hit) = Assert.Single(coordinator.SelectedEpisodeCovers);
            Assert.Same(card, handed);
            Assert.Equal(Hit, hit);
        }

        [Fact]
        public async Task CoverActions_WithoutCoordinator_StayQuiet()
        {
            LocalLibraryViewModel sut = BuildViewModel(coverCoordinator: null);

            await sut.Actions.ApplyEpisodeCoverFromBytesAsync(BuildEpisodeCard(), [1]);
            await sut.Actions.ApplySeriesCoverFromBytesAsync(BuildArtistCard(), [1]);
            await sut.Actions.ApplySelectedEpisodeCoverAsync(BuildEpisodeCard(), Hit);
            await sut.Actions.ApplySelectedSeriesCoverAsync(BuildArtistCard(), Hit);
        }

        [Fact]
        public async Task AnalyzeRestructureAsync_WithoutCoordinator_StaysQuiet()
        {
            LocalLibraryViewModel sut = BuildViewModel(coverCoordinator: null);

            await sut.Actions.AnalyzeRestructureAsync(TestIds.SeriesA);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static LocalEpisodeCardViewModel BuildEpisodeCard()
            => new(
                TestIds.EpisodeA,
                episodeNumber: 1,
                "Folge 1",
                localTrackCount: 1,
                folderPath: null,
                coverImage: null,
                isCompleted: false,
                isSpecialEpisode: false);

        private static LocalArtistCardViewModel BuildArtistCard()
        {
            ServiceCollection services = new();
            ServiceProvider provider = services.BuildServiceProvider();

            return new LocalArtistCardViewModel(
                TestIds.SeriesA,
                "Die drei Fragezeichen",
                coverImage: null,
                localFolderPath: null,
                localEpisodeCount: 1,
                totalEpisodeCount: 1,
                isFavorite: false,
                isWatched: false,
                provider.GetRequiredService<IServiceScopeFactory>());
        }

        private static LocalLibraryViewModel BuildViewModel(FakeEpisodeCoverCoordinator? coverCoordinator)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ITrackTitleResolver>(_ => new FakeTrackTitleResolver());
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            IClock clock = new FakeClock();

            StatusBarViewModel statusBar = new(
                scopeFactory, new FakeThemeService(), new TaskbarProgressService(), clock);

            LocalLibraryViewModelContext context = new(
                scopeFactory,
                new FakeSyncService(),
                new FakePlayerService(),
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                statusBar,
                new FakeLocalCoverLoader(),
                new FakeScanEventService(),
                new FakeCoverSearchService(),
                new FakeOnlineAccessGuard(),
                new FakeOnlineEpisodeChecker(),
                clock,
                CoverCoordinator: coverCoordinator);

            return new LocalLibraryViewModel(context);
        }
    }
}

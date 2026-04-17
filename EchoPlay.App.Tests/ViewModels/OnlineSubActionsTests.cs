using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für die Sub-Actions der Online-Mediathek. Pro Sub-Action mindestens zwei
    /// Tests inklusive Call-Counter-Assertion, um die Zerschneidung des früheren
    /// 741-Zeilen-Monolithen gegen Regressionen abzusichern.
    /// </summary>
    public sealed class OnlineSubActionsTests
    {
        // ── Gemeinsames Setup ────────────────────────────────────────────────────

        private static MediathekOnlineActionsContext BuildContext(
            FakeSeriesDataService? seriesService = null,
            FakeEpisodeDataService? episodeService = null,
            FakePlaybackStateDataService? stateService = null,
            ProviderType activeProvider = ProviderType.Spotify)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService ?? new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService ?? new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => stateService ?? new FakePlaybackStateDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new AppSettings { ActiveProvider = activeProvider }));
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.ISeriesImportSearch>(
                "Spotify", (_, _) => new FakeSeriesImportSearch([], "Spotify"));
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.ISeriesImportSearch>(
                "AppleMusic", (_, _) => new FakeSeriesImportSearch([], "AppleMusic"));
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "Spotify", (_, _) => new FakeEpisodeImportSource([]));
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "AppleMusic", (_, _) => new FakeEpisodeImportSource([]));
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddScoped<ICoverCopyService>(_ => new FakeCoverCopyService());
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddHttpClient();
            _ = services.AddSingleton<CoverService>();
            _ = services.AddSingleton<EpisodeCoverCacheService>();

            ServiceProvider provider = services.BuildServiceProvider();
            ImportService importService = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<EpisodeCoverCacheService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());

            return new MediathekOnlineActionsContext(
                ScopeFactory: provider.GetRequiredService<IServiceScopeFactory>(),
                ConfirmationDialogService: new FakeConfirmationDialogService(),
                ImportService: importService,
                ErrorDialogService: new FakeErrorDialogService(),
                LocalizationService: new FakeLocalizationService(),
                OnlineAccessGuard: new FakeOnlineAccessGuard(),
                CoverCacheService: null,
                CoverService: provider.GetRequiredService<CoverService>(),
                BackgroundCoverService: null,
                WatchToggleService: null,
                HttpClientFactory: provider.GetRequiredService<System.Net.Http.IHttpClientFactory>(),
                CoverBrightnessAnalyzer: null,
                RateLimiter: null);
        }

        // ── OnlineSeriesLoader ───────────────────────────────────────────────────

        [Fact]
        public async Task OnlineSeriesLoader_LoadAsync_IncrementsCallCount_AndSetsNoProviderFlag()
        {
            MediathekOnlineActionsContext ctx = BuildContext(activeProvider: ProviderType.None);
            OnlineSeriesViewModel seriesVM = new();
            OnlineActionsState state = new();

            bool? hasNoProvider = null;
            OnlineSeriesLoader sut = new(ctx, seriesVM, state,
                setIsLoading: _ => { }, setLoadingStatusText: _ => { },
                setHasNoProvider: v => hasNoProvider = v);

            await sut.LoadAsync();

            Assert.Equal(1, sut.LoadCallCount);
            Assert.True(hasNoProvider);
        }

        [Fact]
        public async Task OnlineSeriesLoader_LoadAsync_PopulatesCompletedEpisodeIds_InState()
        {
            Guid completedEpisodeId = new("11111111-1111-1111-1111-111111111111");
            FakePlaybackStateDataService stateService = new(
                states: [
                    new EchoPlay.Data.Entities.Playback.PlaybackState
                    {
                        EpisodeId   = completedEpisodeId,
                        IsCompleted = true
                    }
                ]);

            MediathekOnlineActionsContext ctx = BuildContext(stateService: stateService);
            OnlineSeriesViewModel seriesVM = new();
            OnlineActionsState state = new();

            OnlineSeriesLoader sut = new(ctx, seriesVM, state,
                setIsLoading: _ => { }, setLoadingStatusText: _ => { }, setHasNoProvider: _ => { });

            await sut.LoadAsync();

            Assert.Contains(completedEpisodeId, state.CompletedEpisodeIds);
        }

        // ── OnlineEpisodePipeline ────────────────────────────────────────────────

        [Fact]
        public async Task OnlineEpisodePipeline_SelectSeriesAsync_IncrementsCallCount()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG", SpotifyArtistId = "sp_tkkg", IsOnlineImported = true };
            await seriesService.AddAsync(series);

            MediathekOnlineActionsContext ctx = BuildContext(seriesService: seriesService);
            OnlineSeriesViewModel seriesVM = new();
            OnlineEpisodesViewModel episodesVM = new();
            OnlineActionsState state = new();

            SeriesCardViewModel card = new(
                id: series.Id, title: series.Title, coverImage: null,
                totalEpisodeCount: 0, newEpisodeCount: 0, inProgressCount: 0, finishedCount: 0,
                isSubscribed: false, isFavorite: false, isWatched: false,
                scopeFactory: ctx.ScopeFactory,
                confirmationDialogService: ctx.ConfirmationDialogService,
                localizationService: ctx.LocalizationService);
            seriesVM.SetAllSeries([card]);

            using OnlineEpisodePipeline sut = new(ctx, seriesVM, episodesVM, state);

            await sut.SelectSeriesAsync(card);

            Assert.Equal(1, sut.SelectSeriesCallCount);
            Assert.Equal(0, seriesVM.SelectedSeriesIndex);
        }

        [Fact]
        public async Task OnlineEpisodePipeline_SearchEpisodeCoversAsync_ReturnsEmpty_WhenServiceIsNull()
        {
            System.Collections.Generic.IReadOnlyList<EchoPlay.App.Models.CoverSearchHit> hits =
                await OnlineEpisodePipeline.SearchEpisodeCoversAsync(
                    coverSearchService: null,
                    query: "X",
                    ct: System.Threading.CancellationToken.None);

            Assert.Empty(hits);
        }

        // ── OnlineProviderSearchActions ──────────────────────────────────────────

        [Fact]
        public async Task OnlineProviderSearchActions_SearchProviderAsync_EmptyQuery_SkipsSearch()
        {
            MediathekOnlineActionsContext ctx = BuildContext();
            OnlineSeriesViewModel seriesVM = new();
            OnlineEpisodesViewModel episodesVM = new();
            OnlineProviderSearchViewModel searchVM = new();

            OnlineProviderSearchActions sut = new(ctx, seriesVM, episodesVM, searchVM,
                reloadAfterImportAsync: () => Task.CompletedTask);

            await sut.SearchProviderAsync(string.Empty);

            Assert.Equal(1, sut.SearchCallCount);
            Assert.False(searchVM.IsSearchingProvider);
        }

        [Fact]
        public void OnlineProviderSearchActions_AddSelected_EmptyList_IsNoOp()
        {
            MediathekOnlineActionsContext ctx = BuildContext();
            OnlineSeriesViewModel seriesVM = new();
            OnlineEpisodesViewModel episodesVM = new();
            OnlineProviderSearchViewModel searchVM = new();

            OnlineProviderSearchActions sut = new(ctx, seriesVM, episodesVM, searchVM,
                reloadAfterImportAsync: () => Task.CompletedTask);

            sut.AddSelected();

            Assert.Equal(1, sut.AddSelectedCallCount);
        }

        // ── OnlineBulkRefreshActions ─────────────────────────────────────────────

        [Fact]
        public async Task OnlineBulkRefreshActions_ToggleWatchAsync_NullService_IncrementsButSkips()
        {
            MediathekOnlineActionsContext ctx = BuildContext();
            OnlineSeriesViewModel seriesVM = new();
            OnlineEpisodesViewModel episodesVM = new();

            OnlineBulkRefreshActions sut = new(ctx, seriesVM, episodesVM,
                setIsLoading: _ => { }, setLoadingStatusText: _ => { },
                reloadAfterRefreshAsync: () => Task.CompletedTask);

            await sut.ToggleWatchAsync(Guid.NewGuid(), watch: true);

            Assert.Equal(1, sut.ToggleWatchCallCount);
        }

        [Fact]
        public async Task OnlineBulkRefreshActions_RemoveSeriesAsync_UnknownId_IsNoOp()
        {
            MediathekOnlineActionsContext ctx = BuildContext();
            OnlineSeriesViewModel seriesVM = new();
            OnlineEpisodesViewModel episodesVM = new();

            OnlineBulkRefreshActions sut = new(ctx, seriesVM, episodesVM,
                setIsLoading: _ => { }, setLoadingStatusText: _ => { },
                reloadAfterRefreshAsync: () => Task.CompletedTask);

            await sut.RemoveSeriesAsync(Guid.NewGuid());

            Assert.Equal(1, sut.RemoveSeriesCallCount);
        }
    }
}

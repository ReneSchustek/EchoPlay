using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für die Sub-Actions der Online-Mediathek. Pro Sub-Action mindestens zwei
    /// Tests inklusive Call-Counter-Assertion, um die Aufteilung der
    /// Online-Mediathek in Sub-Actions gegen Regressionen abzusichern.
    /// </summary>
    public sealed class OnlineSubActionsTests
    {
        // Stabile Test-IDs für "unbekannte" Series-IDs in Negativ-Tests.
        // Deterministisch statt Guid.NewGuid(): unabhängig vom Lauf reproduzierbar.
        private static readonly Guid UnknownSeriesId = new("00000000-0000-0000-0000-deadbeef0001");
        private static readonly Guid UnknownRemoveId = new("00000000-0000-0000-0000-deadbeef0002");

        /// <summary>Einstellungen für <see cref="BuildContext"/>; ohne Angabe gelten die Vorgaben.</summary>
        private sealed record ContextSetup
        {
            public FakeSeriesDataService? SeriesService { get; init; }
            public FakeEpisodeDataService? EpisodeService { get; init; }
            public FakePlaybackStateDataService? StateService { get; init; }
            public ProviderType ActiveProvider { get; init; } = ProviderType.Spotify;
            public FakeSpotifyClientCredentialsProvider? CredentialsProvider { get; init; }
            public EchoPlay.Core.Abstractions.Import.ISeriesImportSearch? SpotifySearch { get; init; }
            public EchoPlay.Core.Abstractions.Import.ISeriesImportSearch? AppleMusicSearch { get; init; }
            public FakeConfirmationDialogService? ConfirmationDialogService { get; init; }
            public FakeWatchToggleService? WatchToggleService { get; init; }
        }

        private static OnlineLibraryActionsContext BuildContext(ContextSetup? setup = null)
        {
            setup ??= new ContextSetup();
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => setup.SeriesService ?? new FakeSeriesDataService());
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => setup.EpisodeService ?? new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => setup.StateService ?? new FakePlaybackStateDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new AppSettings { ActiveProvider = setup.ActiveProvider }));
            ImportTestServices.AddProviderFakes(services, spotifySearch: setup.SpotifySearch, appleMusicSearch: setup.AppleMusicSearch);
            _ = services.AddSingleton<ISpotifyClientCredentialsProvider>(
                setup.CredentialsProvider ?? FakeSpotifyClientCredentialsProvider.WithCredentials());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddScoped<ICoverCopyService>(_ => new FakeCoverCopyService());
            ImportTestServices.AddCoverChain(services);

            ServiceProvider provider = services.BuildServiceProvider();
            ImportService importService = ImportTestServices.CreateImportService(provider);

            return new OnlineLibraryActionsContext
            {
                ScopeFactory = provider.GetRequiredService<IServiceScopeFactory>(),
                ConfirmationDialogService = setup.ConfirmationDialogService ?? new FakeConfirmationDialogService(),
                ImportService = importService,
                ErrorDialogService = new FakeErrorDialogService(),
                LocalizationService = new FakeLocalizationService(),
                OnlineAccessGuard = new FakeOnlineAccessGuard(),
                CoverService = provider.GetRequiredService<CoverService>(),
                WatchToggleService = setup.WatchToggleService,
                CoverDownloader = provider.GetRequiredService<ICoverDownloader>(),
            };
        }

        [Fact]
        public async Task OnlineSeriesLoader_LoadAsync_IncrementsCallCount_AndSetsNoProviderFlag()
        {
            OnlineLibraryActionsContext ctx = BuildContext(new() { ActiveProvider = ProviderType.None });
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

            OnlineLibraryActionsContext ctx = BuildContext(new() { StateService = stateService });
            OnlineSeriesViewModel seriesVM = new();
            OnlineActionsState state = new();

            OnlineSeriesLoader sut = new(ctx, seriesVM, state,
                setIsLoading: _ => { }, setLoadingStatusText: _ => { }, setHasNoProvider: _ => { });

            await sut.LoadAsync();

            Assert.Contains(completedEpisodeId, state.CompletedEpisodeIds);
        }

        [Fact]
        public async Task OnlineEpisodePipeline_SelectSeriesAsync_IncrementsCallCount()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG", SpotifyArtistId = "sp_tkkg", IsOnlineImported = true };
            await seriesService.AddAsync(series, cancellationToken: TestContext.Current.CancellationToken);

            OnlineLibraryActionsContext ctx = BuildContext(new() { SeriesService = seriesService });
            OnlineSeriesViewModel seriesVM = new();
            OnlineEpisodesViewModel episodesVM = new();
            OnlineActionsState state = new();

            SeriesCardViewModel card = new(
                new SeriesCardData
                {
                    Id = series.Id,
                    Title = series.Title,
                    TotalEpisodeCount = 0,
                    NewEpisodeCount = 0,
                    InProgressCount = 0,
                    FinishedCount = 0,
                    IsSubscribed = false,
                    IsFavorite = false,
                    IsWatched = false,
                },
                coverImage: null,
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
        public async Task OnlineEpisodePipeline_SelectSeriesAsync_SameSeriesTwice_DeselectsOnSecondCall()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG", SpotifyArtistId = "sp_tkkg", IsOnlineImported = true };
            await seriesService.AddAsync(series, cancellationToken: TestContext.Current.CancellationToken);

            OnlineLibraryActionsContext ctx = BuildContext(new() { SeriesService = seriesService });
            OnlineSeriesViewModel seriesVM = new();
            OnlineEpisodesViewModel episodesVM = new();
            OnlineActionsState state = new();

            SeriesCardViewModel card = new(
                new SeriesCardData
                {
                    Id = series.Id,
                    Title = series.Title,
                    TotalEpisodeCount = 0,
                    NewEpisodeCount = 0,
                    InProgressCount = 0,
                    FinishedCount = 0,
                    IsSubscribed = false,
                    IsFavorite = false,
                    IsWatched = false,
                },
                coverImage: null,
                scopeFactory: ctx.ScopeFactory,
                confirmationDialogService: ctx.ConfirmationDialogService,
                localizationService: ctx.LocalizationService);
            seriesVM.SetAllSeries([card]);

            using OnlineEpisodePipeline sut = new(ctx, seriesVM, episodesVM, state);

            await sut.SelectSeriesAsync(card);
            Assert.Equal(0, seriesVM.SelectedSeriesIndex);
            Assert.True(card.IsSelectedInAccordion);

            // Re-Klick auf dieselbe Kachel → Toggle: Auswahl wird aufgehoben.
            await sut.SelectSeriesAsync(card);

            Assert.Equal(-1, seriesVM.SelectedSeriesIndex);
            Assert.False(card.IsSelectedInAccordion);
            Assert.Empty(episodesVM.Episodes);
        }

        [Fact]
        public void OnlineEpisodesViewModel_SetEpisodes_KeepsChosenSortOrder()
        {
            // Die gewählte Sortierung darf beim Serienwechsel (SetEpisodes) nicht auf den Standard
            // zurückspringen, die Auswahl soll erhalten bleiben (User-Wunsch: „nicht zurückspringen").
            OnlineEpisodesViewModel vm = new();

            vm.EpisodeSortIndex = 1; // Nummer absteigend

            vm.SetEpisodes(
            [
                new(
                    new OnlineEpisodeCardData
                    {
                        EpisodeId = Helpers.TestIds.EpisodeA,
                        EpisodeNumber = 1,
                        Title = "Folge 1",
                    }),
                new(
                    new OnlineEpisodeCardData
                    {
                        EpisodeId = Helpers.TestIds.EpisodeC,
                        EpisodeNumber = 3,
                        Title = "Folge 3",
                    }),
                new(
                    new OnlineEpisodeCardData
                    {
                        EpisodeId = Helpers.TestIds.EpisodeB,
                        EpisodeNumber = 2,
                        Title = "Folge 2",
                    }),
            ]);

            // Sortier-Auswahl bleibt erhalten ...
            Assert.Equal(1, vm.EpisodeSortIndex);
            // ... und wird auf die neue Liste angewandt: absteigend nach Nummer.
            Assert.Equal(3, vm.Episodes[0].EpisodeNumber);
            Assert.Equal(2, vm.Episodes[1].EpisodeNumber);
            Assert.Equal(1, vm.Episodes[2].EpisodeNumber);
        }

        [Fact]
        public async Task OnlineEpisodePipeline_SearchEpisodeCoversAsync_ReturnsEmpty_WhenServiceIsNull()
        {
            System.Collections.Generic.IReadOnlyList<EchoPlay.App.Models.CoverSearchHit> hits =
                await OnlineEpisodePipeline.SearchEpisodeCoversAsync(
                    coverSearchService: null,
                    query: "X",
                    page: EchoPlay.LocalLibrary.Cover.CoverSearchPage.First,
                    ct: System.Threading.CancellationToken.None);

            Assert.Empty(hits);
        }

        [Fact]
        public async Task OnlineProviderSearchActions_SearchProviderAsync_EmptyQuery_SkipsSearch()
        {
            OnlineLibraryActionsContext ctx = BuildContext();
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
            OnlineLibraryActionsContext ctx = BuildContext();
            OnlineSeriesViewModel seriesVM = new();
            OnlineEpisodesViewModel episodesVM = new();
            OnlineProviderSearchViewModel searchVM = new();

            OnlineProviderSearchActions sut = new(ctx, seriesVM, episodesVM, searchVM,
                reloadAfterImportAsync: () => Task.CompletedTask);

            sut.AddSelected();

            Assert.Equal(1, sut.AddSelectedCallCount);
        }

        [Fact]
        public async Task OnlineProviderSearchActions_SearchProviderAsync_SetsFallbackHint_WhenSpotifyCredentialsMissing()
        {
            // Spotify aktiv ohne Credentials → Fallback auf Apple Music mit sichtbarem Hinweis.
            FakeSeriesImportSearch appleMusicSearch = new(
                [new ImportSeries { Title = "Apple", Source = "AppleMusic", SourceSeriesId = "am1" }],
                "AppleMusic");

            OnlineLibraryActionsContext ctx = BuildContext(new()
            {
                CredentialsProvider = FakeSpotifyClientCredentialsProvider.Missing(),
                AppleMusicSearch = appleMusicSearch,
            });
            OnlineSeriesViewModel seriesVM = new();
            OnlineEpisodesViewModel episodesVM = new();
            OnlineProviderSearchViewModel searchVM = new();

            OnlineProviderSearchActions sut = new(ctx, seriesVM, episodesVM, searchVM,
                reloadAfterImportAsync: () => Task.CompletedTask);

            await sut.SearchProviderAsync("query");

            Assert.True(searchVM.IsSpotifyFallbackHintVisible);
            Assert.False(searchVM.IsSearchingProvider);
        }

        [Fact]
        public async Task OnlineProviderSearchActions_SearchProviderAsync_BackToBack_DiscardsOlderResults()
        {
            // Back-to-Back-Suche in der Online-Mediathek: die zweite Suche muss die erste
            // verdrängen, die spät eintreffenden Stale-Treffer dürfen die UI nicht mehr füllen.
            GatedSeriesImportSearch gated = new();
            OnlineLibraryActionsContext ctx = BuildContext(new() { SpotifySearch = gated });
            OnlineSeriesViewModel seriesVM = new();
            OnlineEpisodesViewModel episodesVM = new();
            OnlineProviderSearchViewModel searchVM = new()
            {
                SearchTypeIndex = 1 // nur Serien, die Album-Suche wird so umgangen
            };

            OnlineProviderSearchActions sut = new(ctx, seriesVM, episodesVM, searchVM,
                reloadAfterImportAsync: () => Task.CompletedTask);

            Task firstSearch = sut.SearchProviderAsync("first");
            Task secondSearch = sut.SearchProviderAsync("second");

            gated.CompleteCall(0,
                [new ImportSeries { Title = "stale", Source = "Spotify", SourceSeriesId = "s1" }]);
            gated.CompleteCall(1,
                [new ImportSeries { Title = "fresh", Source = "Spotify", SourceSeriesId = "f1" }]);

            await Task.WhenAll(firstSearch, secondSearch);

            _ = Assert.Single(searchVM.ProviderSearchResults);
            Assert.Equal("fresh", searchVM.ProviderSearchResults[0].Title);
            Assert.False(searchVM.IsSearchingProvider);
        }

        [Fact]
        public void OnlineProviderSearchViewModel_ClearResults_ResetsFallbackHint()
        {
            // Direkt-Test auf dem Sub-VM: ClearResults muss den Fallback-Hinweis löschen.
            OnlineProviderSearchViewModel searchVM = new()
            {
                IsSpotifyFallbackHintVisible = true
            };

            searchVM.ClearResults();

            Assert.False(searchVM.IsSpotifyFallbackHintVisible);
        }

        [Fact]
        public async Task OnlineBulkRefreshActions_ToggleWatchAsync_NullService_IncrementsButSkips()
        {
            OnlineLibraryActionsContext ctx = BuildContext();
            OnlineSeriesViewModel seriesVM = new();
            OnlineEpisodesViewModel episodesVM = new();

            OnlineBulkRefreshActions sut = new(ctx, seriesVM, episodesVM,
                setIsLoading: _ => { }, setLoadingStatusText: _ => { },
                reloadAfterRefreshAsync: () => Task.CompletedTask);

            await sut.ToggleWatchAsync(UnknownSeriesId, watch: true);

            Assert.Equal(1, sut.ToggleWatchCallCount);
        }

        [Fact]
        public async Task OnlineBulkRefreshActions_RemoveSeriesAsync_UnknownId_IsNoOp()
        {
            OnlineLibraryActionsContext ctx = BuildContext();
            OnlineSeriesViewModel seriesVM = new();
            OnlineEpisodesViewModel episodesVM = new();

            OnlineBulkRefreshActions sut = new(ctx, seriesVM, episodesVM,
                setIsLoading: _ => { }, setLoadingStatusText: _ => { },
                reloadAfterRefreshAsync: () => Task.CompletedTask);

            await sut.RemoveSeriesAsync(UnknownRemoveId);

            Assert.Equal(1, sut.RemoveSeriesCallCount);
        }

        [Fact]
        public async Task OnlineBulkRefreshActions_RemoveSeriesAsync_WhenUserDeclines_KeepsTheSeries()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Die drei Fragezeichen", IsOnlineImported = true };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeConfirmationDialogService dialog = new(result: false);
            OnlineLibraryActionsContext ctx = BuildContext(new()
            {
                SeriesService = seriesService,
                ConfirmationDialogService = dialog,
            });

            OnlineSeriesViewModel seriesVM = new();
            seriesVM.SetAllSeries([CardFor(series, ctx.ScopeFactory)]);
            OnlineEpisodesViewModel episodesVM = new();

            OnlineBulkRefreshActions sut = new(ctx, seriesVM, episodesVM,
                setIsLoading: _ => { }, setLoadingStatusText: _ => { },
                reloadAfterRefreshAsync: () => Task.CompletedTask);

            await sut.RemoveSeriesAsync(series.Id);

            // Der Dialog ist die letzte Gelegenheit umzukehren. Wird dort abgelehnt, muss
            // die Serie samt Folgen unangetastet bleiben.
            Assert.Equal(1, dialog.CallCount);
            _ = Assert.Single(seriesService.All);
            _ = Assert.Single(seriesVM.AllSeries);
        }

        [Fact]
        public async Task OnlineBulkRefreshActions_RemoveSeriesAsync_WhenConfirmed_RemovesItEverywhere()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Die drei Fragezeichen", IsOnlineImported = true };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            OnlineLibraryActionsContext ctx = BuildContext(new()
            {
                SeriesService = seriesService,
                ConfirmationDialogService = new FakeConfirmationDialogService(result: true),
            });

            OnlineSeriesViewModel seriesVM = new();
            seriesVM.SetAllSeries([CardFor(series, ctx.ScopeFactory)]);
            OnlineEpisodesViewModel episodesVM = new();

            OnlineBulkRefreshActions sut = new(ctx, seriesVM, episodesVM,
                setIsLoading: _ => { }, setLoadingStatusText: _ => { },
                reloadAfterRefreshAsync: () => Task.CompletedTask);

            await sut.RemoveSeriesAsync(series.Id);

            // Aus der Ablage und aus der Anzeige. Bliebe die Kachel stehen, führte der
            // nächste Klick auf eine Serie, die es nicht mehr gibt.
            Assert.Empty(seriesService.All);
            Assert.Empty(seriesVM.AllSeries);
        }

        [Fact]
        public async Task OnlineBulkRefreshActions_ToggleWatchAsync_WithService_UpdatesTheCard()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Die drei Fragezeichen", IsOnlineImported = true };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeWatchToggleService watchToggle = new();
            OnlineLibraryActionsContext ctx = BuildContext(new()
            {
                SeriesService = seriesService,
                WatchToggleService = watchToggle,
            });

            OnlineSeriesViewModel seriesVM = new();
            seriesVM.SetAllSeries([CardFor(series, ctx.ScopeFactory)]);
            OnlineEpisodesViewModel episodesVM = new();

            OnlineBulkRefreshActions sut = new(ctx, seriesVM, episodesVM,
                setIsLoading: _ => { }, setLoadingStatusText: _ => { },
                reloadAfterRefreshAsync: () => Task.CompletedTask);

            await sut.ToggleWatchAsync(series.Id, watch: true);

            // Der Dienst merkt sich den Stand, die Kachel zeigt ihn. Fehlt das zweite,
            // springt das Symbol beim nächsten Aufbau der Liste zurück.
            Assert.Equal([(series.Id, true)], watchToggle.Calls);
            Assert.True(seriesVM.AllSeries[0].IsWatched);
        }

        /// <summary>
        /// Baut die Kachel zu einer Serie. Die Zählwerte spielen für diese Tests keine Rolle;
        /// geprüft wird, ob die Kachel verschwindet beziehungsweise ihren Überwachungsstand
        /// übernimmt.
        /// </summary>
        private static SeriesCardViewModel CardFor(Series series, IServiceScopeFactory scopeFactory)
        {
            return new SeriesCardViewModel(
                new SeriesCardData
                {
                    Id = series.Id,
                    Title = series.Title,
                    TotalEpisodeCount = 0,
                    NewEpisodeCount = 0,
                    InProgressCount = 0,
                    FinishedCount = 0,
                    IsSubscribed = true,
                    IsFavorite = false,
                    IsWatched = false,
                },
                coverImage: null,
                scopeFactory: scopeFactory,
                confirmationDialogService: new FakeConfirmationDialogService(),
                localizationService: new FakeLocalizationService());
        }
    }
}

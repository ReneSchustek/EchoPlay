using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Durchreiche-Schicht der Online-Mediathek: Filter, Sortierung und
    /// Suchfeld liegen in Unter-Ansichtsmodellen, die Seite bindet aber an das obere.
    /// </summary>
    /// <remarks>
    /// Jede dieser Eigenschaften ist eine Weiche. Zeigt eine davon auf das falsche
    /// Unter-Ansichtsmodell, wirkt eine Bedienung ins Leere — und das fällt in der
    /// Oberfläche erst auf, wenn jemand genau diesen Regler benutzt.
    /// </remarks>
    public sealed class OnlineLibraryPassThroughTests
    {
        [Fact]
        public void StatusFilter_IsHandedToTheSeriesSection()
        {
            OnlineLibraryViewModel sut = BuildViewModel();

            sut.StatusFilter = SeriesStatusFilter.AmHoeren;

            Assert.Equal(SeriesStatusFilter.AmHoeren, sut.SeriesVM.StatusFilter);
            Assert.Equal(SeriesStatusFilter.AmHoeren, sut.StatusFilter);
        }

        [Fact]
        public void SeriesSortIndex_IsHandedToTheSeriesSection()
        {
            OnlineLibraryViewModel sut = BuildViewModel();

            sut.SeriesSortIndex = 2;

            Assert.Equal(2, sut.SeriesVM.SeriesSortIndex);
            Assert.Equal(2, sut.SeriesSortIndex);
        }

        [Fact]
        public void SearchTypeIndex_IsHandedToTheProviderSearch()
        {
            OnlineLibraryViewModel sut = BuildViewModel();

            sut.SearchTypeIndex = 1;

            Assert.Equal(1, sut.ProviderSearchVM.SearchTypeIndex);
            Assert.Equal(1, sut.SearchTypeIndex);
        }

        [Fact]
        public void ProviderSearchState_MirrorsTheProviderSearchSection()
        {
            OnlineLibraryViewModel sut = BuildViewModel();

            Assert.Equal(sut.ProviderSearchVM.ProviderSearchResultsVisibility, sut.ProviderSearchResultsVisibility);
            Assert.Equal(sut.ProviderSearchVM.IsSpotifyFallbackHintVisible, sut.IsSpotifyFallbackHintVisible);
        }

        [Fact]
        public void SelectedSeriesIndex_MirrorsTheSeriesSection()
        {
            OnlineLibraryViewModel sut = BuildViewModel();

            // Ohne Auswahl steht der Zeiger auf „nichts gewählt"; die Seite blendet daran
            // den ganzen Folgenbereich aus.
            Assert.Equal(-1, sut.SelectedSeriesIndex);
        }

        [Fact]
        public void DeselectSeries_ClosesTheEpisodeArea()
        {
            OnlineLibraryViewModel sut = BuildViewModel();
            sut.EpisodesVM.SetEpisodes([new OnlineEpisodeCardViewModel(TestIds.EpisodeA, 1, "Folge 1")]);

            sut.DeselectSeries();

            Assert.Empty(sut.EpisodesVM.Episodes);
            Assert.Equal(-1, sut.SelectedSeriesIndex);
        }

        [Fact]
        public async Task SelectSeriesAsync_WithoutCard_ThrowsArgumentNullException()
        {
            OnlineLibraryViewModel sut = BuildViewModel();

            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SelectSeriesAsync(null!));
        }

        [Fact]
        public async Task ApplySelectedEpisodeCoverAsync_WithoutCard_ThrowsArgumentNullException()
        {
            OnlineLibraryViewModel sut = BuildViewModel();
            CoverSearchHit hit = new("t", "f", "r", "s");

            _ = await Assert.ThrowsAsync<ArgumentNullException>(
                () => sut.ApplySelectedEpisodeCoverAsync(null!, hit));
        }

        [Fact]
        public async Task ApplySelectedEpisodeCoverAsync_WithoutHit_ThrowsArgumentNullException()
        {
            OnlineLibraryViewModel sut = BuildViewModel();
            OnlineEpisodeCardViewModel card = new(TestIds.EpisodeA, 1, "Folge 1");

            _ = await Assert.ThrowsAsync<ArgumentNullException>(
                () => sut.ApplySelectedEpisodeCoverAsync(card, null!));
        }

        [Fact]
        public async Task SearchEpisodeCoversAsync_WithoutSearchService_FindsNothing()
        {
            OnlineLibraryViewModel sut = BuildViewModel();

            Assert.Empty(await sut.SearchEpisodeCoversAsync(
                "TKKG", EchoPlay.LocalLibrary.Cover.CoverSearchPage.First,
                TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ReloadAfterImportAsync_ClearsTheSearchResults()
        {
            OnlineLibraryViewModel sut = BuildViewModel();

            await sut.ReloadAfterImportAsync();

            // Nach dem Import gehört die Serie in die Mediathek, nicht mehr in die
            // Trefferliste — sonst lädt der Anwender sie ein zweites Mal ein.
            Assert.Empty(sut.ProviderSearchVM.ProviderSearchResults);
        }

        [Fact]
        public void GoToSettingsCommand_AndFocusSearch_AreAvailable()
        {
            OnlineLibraryViewModel sut = BuildViewModel();

            Assert.True(sut.GoToSettingsCommand.CanExecute(null));
            Assert.True(sut.RefreshCommand.CanExecute(null));
            Assert.True(sut.AddSelectedCommand.CanExecute(null));
        }

        [Fact]
        public void StartSearchFromEmptyState_WithoutSearchText_OnlyAsksForTheFocus()
        {
            OnlineLibraryViewModel sut = BuildViewModel();
            int focusRequests = 0;
            sut.FocusSearchRequested += () => focusRequests++;

            sut.FocusSearchCommand.Execute(null);

            // Ohne Suchbegriff gibt es nichts zu suchen — der Zeiger springt trotzdem ins
            // Feld, damit der Anwender sofort tippen kann.
            Assert.Equal(1, focusRequests);
        }

        [Fact]
        public async Task RemoveSeriesAsync_TakesTheSeriesOutOfTheLibrary()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG", SpotifyArtistId = "sp_tkkg", IsOnlineImported = true };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);
            OnlineLibraryViewModel sut = BuildViewModel(seriesService);
            await sut.LoadAsync();

            await sut.RemoveSeriesAsync(series.Id);

            Assert.Null(await seriesService.GetByIdAsync(series.Id, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ToggleWatchAsync_WritesTheWatchFlag()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG", SpotifyArtistId = "sp_tkkg", IsOnlineImported = true };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);
            FakeWatchToggleService watchToggle = new();
            OnlineLibraryViewModel sut = BuildViewModel(seriesService, watchToggle);
            await sut.LoadAsync();

            await sut.ToggleWatchAsync(series.Id, watch: true);

            Assert.Equal([(series.Id, true)], watchToggle.Calls);
        }

        [Fact]
        public void Dispose_CanBeCalledWithoutALoadedLibrary()
        {
            OnlineLibraryViewModel sut = BuildViewModel();

            sut.Dispose();
        }

        [Fact]
        public async Task EpisodeSortIndex_WhenChanged_IsRemembered()
        {
            FakeAppSettingsDataService settings = new();
            OnlineLibraryViewModel sut = BuildViewModel(new FakeSeriesDataService(), settings: settings);

            sut.EpisodeSortIndex = 2;
            await ChangeSignals.WaitForAsync(
                sut, () => settings.SaveCallCount > 0, "Die Sortierung ist gespeichert");

            // Die gewählte Sortierung soll den Programmstart überleben — sonst springt die
            // Folgenliste bei jedem Öffnen auf die Vorgabe zurück.
            AppSettings stored = await settings.GetAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, stored.OnlineEpisodeSortIndex);
        }

        [Fact]
        public async Task EpisodeSortIndex_SetToTheStoredValue_WritesNothingAgain()
        {
            FakeAppSettingsDataService settings = new(
                new AppSettings { ActiveProvider = ProviderType.Spotify, OnlineEpisodeSortIndex = 2 });
            OnlineLibraryViewModel sut = BuildViewModel(new FakeSeriesDataService(), settings: settings);

            await sut.LoadAsync();

            // Beim Seitenaufbau wird die gemerkte Sortierung gesetzt; ein erneuter
            // Schreibvorgang daraus wäre überflüssige Last auf der Ablage.
            Assert.Equal(2, sut.EpisodeSortIndex);
        }

        [Fact]
        public async Task LoadAsync_RestoresTheStoredEpisodeSortOrder()
        {
            FakeAppSettingsDataService settings = new(
                new AppSettings { ActiveProvider = ProviderType.Spotify, OnlineEpisodeSortIndex = 1 });
            OnlineLibraryViewModel sut = BuildViewModel(new FakeSeriesDataService(), settings: settings);

            await sut.LoadAsync();

            Assert.Equal(1, sut.EpisodesVM.EpisodeSortIndex);
        }

        [Fact]
        public void FocusSearch_WithASearchText_StartsTheProviderSearch()
        {
            OnlineLibraryViewModel sut = BuildViewModel();
            sut.SearchText = "TKKG";
            int focusRequests = 0;
            sut.FocusSearchRequested += () => focusRequests++;

            sut.FocusSearchCommand.Execute(null);

            // Aus dem Leer-Zustand heraus soll ein Klick reichen: Zeiger ins Feld und
            // gleich suchen, wenn schon etwas darin steht.
            Assert.Equal(1, focusRequests);
            Assert.Equal("TKKG", sut.SearchText);
        }

        private static OnlineLibraryViewModel BuildViewModel() => BuildViewModel(new FakeSeriesDataService());

        private static OnlineLibraryViewModel BuildViewModel(
            FakeSeriesDataService seriesService,
            FakeWatchToggleService? watchToggle = null,
            FakeAppSettingsDataService? settings = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => settings ?? new FakeAppSettingsDataService(
                new AppSettings { ActiveProvider = ProviderType.Spotify }));
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
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddHttpClient();
            _ = services.AddSingleton<CoverService>();
            _ = services.AddSingleton<ICoverService>(sp => sp.GetRequiredService<CoverService>());
            _ = services.AddSingleton<ICoverDownloader>(new FakeCoverDownloader());
            _ = services.AddSingleton<EpisodeCoverCacheService>();

            ServiceProvider provider = services.BuildServiceProvider();
            ImportService importService = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<EpisodeCoverCacheService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());

            return new OnlineLibraryViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeConfirmationDialogService(),
                importService,
                new FakeErrorDialogService(),
                new FakeLocalizationService(),
                new FakeOnlineAccessGuard(),
                provider.GetRequiredService<ICoverDownloader>(),
                watchToggleService: watchToggle);
        }
    }
}

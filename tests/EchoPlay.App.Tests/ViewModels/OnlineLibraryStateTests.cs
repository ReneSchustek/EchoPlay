using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für den Zustand der Online-Mediathek: Leer-Zustände, durchgereichte
    /// Eigenschaften der drei Unter-Ansichtsmodelle und die gemerkte Folgen-Sortierung.
    /// Das Laden der Serienliste steht in <see cref="OnlineLibraryViewModelTests"/>.
    /// </summary>
    public sealed class OnlineLibraryStateTests
    {
        private static (OnlineLibraryViewModel ViewModel, FakeAppSettingsDataService Settings) Build(
            AppSettings? settings = null,
            FakeSeriesDataService? seriesService = null,
            FakePageModeGuard? pageModeGuard = null,
            FakeNavigationService? navigationService = null,
            FakeEpisodeDataService? episodeService = null,
            FakeEpisodeImportSource? episodeSource = null)
        {
            FakeAppSettingsDataService settingsService = new(
                settings ?? new AppSettings { ActiveProvider = ProviderType.Spotify });

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService ?? new FakeSeriesDataService());
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService ?? new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => settingsService);
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.ISeriesImportSearch>(
                "Spotify", (_, _) => new FakeSeriesImportSearch([], "Spotify"));
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.ISeriesImportSearch>(
                "AppleMusic", (_, _) => new FakeSeriesImportSearch([], "AppleMusic"));
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "Spotify", (_, _) => episodeSource ?? new FakeEpisodeImportSource([]));
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "AppleMusic", (_, _) => episodeSource ?? new FakeEpisodeImportSource([]));
            _ = services.AddSingleton<EchoPlay.Spotify.Auth.ISpotifyClientCredentialsProvider>(
                FakeSpotifyClientCredentialsProvider.WithCredentials());
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddHttpClient();
            _ = services.AddSingleton<CoverService>();
            _ = services.AddSingleton<ICoverService>(sp => sp.GetRequiredService<CoverService>());
            _ = services.AddSingleton<ICoverDownloader>(new FakeCoverDownloader());
            _ = services.AddSingleton<EpisodeCoverCacheService>();

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            ImportService importService = new(
                scopeFactory,
                provider.GetRequiredService<EpisodeCoverCacheService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());

            OnlineLibraryViewModel viewModel = new(
                scopeFactory,
                new FakeConfirmationDialogService(),
                importService,
                new FakeErrorDialogService(),
                new FakeLocalizationService(),
                new FakeOnlineAccessGuard(),
                provider.GetRequiredService<ICoverDownloader>(),
                pageModeGuard: pageModeGuard,
                navigationService: navigationService);

            return (viewModel, settingsService);
        }

        [Fact]
        public async Task Seitenstart_OhneWaechterLaedtWeiter()
        {
            (OnlineLibraryViewModel viewModel, _) = Build();

            Assert.True(await viewModel.InitializeAsync());
        }

        [Fact]
        public async Task Seitenstart_HaeltDieSeiteImNurLokalBetriebAn()
        {
            // Sagt der Wächter Nein, darf die Seite nicht weiterladen — sonst laufen
            // Anbieter-Abfragen im ausdrücklich gewählten Offline-Betrieb.
            FakePageModeGuard guard = new(allow: false);
            (OnlineLibraryViewModel viewModel, _) = Build(pageModeGuard: guard);

            Assert.False(await viewModel.InitializeAsync());
            Assert.Equal(1, guard.CallCount);
        }

        [Fact]
        public async Task Folgensortierung_WirdGemerkt()
        {
            (OnlineLibraryViewModel viewModel, FakeAppSettingsDataService settings) = Build();

            viewModel.EpisodeSortIndex = 2;
            await WaitForSaveAsync(settings);

            AppSettings stored = await settings.GetAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, stored.OnlineEpisodeSortIndex);
        }

        [Fact]
        public void Folgensortierung_DerselbeWertSchreibtNichts()
        {
            (OnlineLibraryViewModel viewModel, FakeAppSettingsDataService settings) = Build();

            viewModel.EpisodeSortIndex = viewModel.EpisodeSortIndex;

            Assert.Equal(0, settings.SaveCallCount);
        }

        [Fact]
        public async Task Folgensortierung_KehrtNachDemLadenZurueck()
        {
            // Ohne Wiederherstellung stünde nach jedem Start wieder die Standardreihenfolge —
            // die bewusste Wahl des Nutzers wäre nach einem Serienwechsel verloren.
            (OnlineLibraryViewModel viewModel, _) = Build(new AppSettings
            {
                ActiveProvider = ProviderType.Spotify,
                OnlineEpisodeSortIndex = 3
            });

            await viewModel.LoadAsync();

            Assert.Equal(3, viewModel.EpisodeSortIndex);
        }

        [Fact]
        public void Suchfeld_LeerenRaeumtAuchDieTrefferAusDerAnbietersuche()
        {
            (OnlineLibraryViewModel viewModel, _) = Build();
            viewModel.ProviderSearchVM.IsSpotifyFallbackHintVisible = true;

            viewModel.SearchText = string.Empty;

            Assert.False(viewModel.ProviderSearchVM.HasResults);
            Assert.False(viewModel.IsSpotifyFallbackHintVisible);
        }

        [Fact]
        public void Suchfeld_ReichtDenTextAnDieSerienlisteWeiter()
        {
            (OnlineLibraryViewModel viewModel, _) = Build();

            viewModel.SearchText = "TKKG";

            Assert.Equal("TKKG", viewModel.SeriesVM.SearchText);
        }

        [Fact]
        public void LeerZustand_VerschwindetSobaldSerienDaSind()
        {
            (OnlineLibraryViewModel viewModel, _) = Build();

            Assert.Equal(Visibility.Visible, viewModel.EmptyStateVisibility);

            viewModel.SeriesVM.SetAllSeries([BuildCard()]);

            Assert.Equal(Visibility.Collapsed, viewModel.EmptyStateVisibility);
        }

        [Fact]
        public void LeerZustand_NenntDenFehlendenAnbieterOderDieFehlendenSerien()
        {
            // Beide Hinweise teilen sich denselben Platz. Steht kein Anbieter fest,
            // hilft der Hinweis auf die Serien nicht weiter.
            (OnlineLibraryViewModel viewModel, _) = Build();

            Assert.Equal(Visibility.Collapsed, viewModel.NoProviderVisibility);
            Assert.Equal(Visibility.Visible, viewModel.NoSeriesVisibility);
        }

        [Fact]
        public async Task LeerZustand_OhneAnbieterZeigtDenAnbieterHinweis()
        {
            (OnlineLibraryViewModel viewModel, _) = Build(new AppSettings { ActiveProvider = ProviderType.None });

            await viewModel.LoadAsync();

            Assert.True(viewModel.HasNoProvider);
            Assert.Equal(Visibility.Visible, viewModel.NoProviderVisibility);
            Assert.Equal(Visibility.Collapsed, viewModel.NoSeriesVisibility);
        }

        [Fact]
        public void Anbietersuche_TauschtBibliothekUndFilterAus()
        {
            (OnlineLibraryViewModel viewModel, _) = Build();

            Assert.Equal(Visibility.Visible, viewModel.LibraryVisibility);
            Assert.Equal(Visibility.Visible, viewModel.StatusFilterVisibility);
            Assert.Equal(Visibility.Collapsed, viewModel.SearchTypeFilterVisibility);

            viewModel.ProviderSearchVM.IsSearchingProvider = true;

            Assert.Equal(Visibility.Collapsed, viewModel.LibraryVisibility);
            Assert.Equal(Visibility.Collapsed, viewModel.StatusFilterVisibility);
            Assert.Equal(Visibility.Visible, viewModel.SearchTypeFilterVisibility);
        }

        [Fact]
        public void Durchreichen_MeldetAenderungenDerUnterAnsichten()
        {
            // Die Seite bindet auf das obere Ansichtsmodell. Ohne Weiterreichen bliebe
            // die Anzeige stehen, obwohl sich die Unter-Ansicht geändert hat.
            (OnlineLibraryViewModel viewModel, _) = Build();
            List<string?> gemeldet = [];
            viewModel.PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName);

            viewModel.ProviderSearchVM.IsSpotifyFallbackHintVisible = true;

            Assert.Contains(nameof(OnlineLibraryViewModel.IsSpotifyFallbackHintVisible), gemeldet);
            Assert.Contains(nameof(OnlineLibraryViewModel.SpotifyFallbackHintVisibility), gemeldet);
            Assert.True(viewModel.IsSpotifyFallbackHintVisible);
            Assert.Equal(Visibility.Visible, viewModel.SpotifyFallbackHintVisibility);
        }

        [Fact]
        public void Durchreichen_ZeigtDenZustandDerFolgenliste()
        {
            (OnlineLibraryViewModel viewModel, _) = Build();

            viewModel.EpisodesVM.IsLoadingEpisodes = true;

            Assert.True(viewModel.IsLoadingEpisodes);
            Assert.Equal(Visibility.Visible, viewModel.LoadingEpisodesVisibility);
            Assert.Empty(viewModel.Episodes);
            Assert.Empty(viewModel.ProviderSearchResults);
        }

        [Fact]
        public void Durchreichen_SetztFilterUndSortierungDerSerienliste()
        {
            (OnlineLibraryViewModel viewModel, _) = Build();

            viewModel.StatusFilter = EchoPlay.App.Models.SeriesStatusFilter.AmHoeren;
            viewModel.SeriesSortIndex = 1;
            viewModel.SearchTypeIndex = 1;

            Assert.Equal(EchoPlay.App.Models.SeriesStatusFilter.AmHoeren, viewModel.SeriesVM.StatusFilter);
            Assert.Equal(1, viewModel.SeriesVM.SeriesSortIndex);
            Assert.Equal(1, viewModel.ProviderSearchVM.SearchTypeIndex);
            Assert.Equal(-1, viewModel.SelectedSeriesIndex);
            Assert.Equal(Visibility.Collapsed, viewModel.EpisodesAccordionVisibility);
        }

        [Fact]
        public void AuswahlAufheben_SchliesstDasAkkordeonUndLeertDieFolgen()
        {
            (OnlineLibraryViewModel viewModel, _) = Build();
            SeriesCardViewModel card = BuildCard();
            viewModel.SeriesVM.SetAllSeries([card]);
            viewModel.SeriesVM.SelectSeries(card);

            viewModel.DeselectSeries();

            Assert.Equal(-1, viewModel.SelectedSeriesIndex);
            Assert.Empty(viewModel.Episodes);
        }

        [Fact]
        public void EinstellungenAufrufen_FuehrtZurEinstellungsseite()
        {
            FakeNavigationService navigation = new();
            (OnlineLibraryViewModel viewModel, _) = Build(navigationService: navigation);

            viewModel.GoToSettingsCommand.Execute(null);

            Assert.Equal(NavigationTarget.Settings, Assert.Single(navigation.Navigations).Target);
        }

        [Fact]
        public void SucheAusDemLeerZustand_MeldetDenFokusWunsch()
        {
            (OnlineLibraryViewModel viewModel, _) = Build();
            bool fokusAngefordert = false;
            viewModel.FocusSearchRequested += () => fokusAngefordert = true;

            viewModel.FocusSearchCommand.Execute(null);

            Assert.True(fokusAngefordert);
        }

        [Fact]
        public async Task Coversuche_OhneDienstLiefertKeineTreffer()
        {
            (OnlineLibraryViewModel viewModel, _) = Build();

            IReadOnlyList<EchoPlay.App.Models.CoverSearchHit> hits = await viewModel.SearchEpisodeCoversAsync(
                "TKKG",
                EchoPlay.LocalLibrary.Cover.CoverSearchPage.First,
                TestContext.Current.CancellationToken);

            Assert.Empty(hits);
        }

        [Fact]
        public void Aufraeumen_LaeuftAuchOhneVorherigesLaden()
        {
            (OnlineLibraryViewModel viewModel, _) = Build();

            viewModel.Dispose();

            Assert.Equal(Visibility.Visible, viewModel.EmptyStateVisibility);
        }

        [Fact]
        public async Task LeereSerie_WirdBeimLadenNachimportiert()
        {
            // Eine Migration hat die Folgen einer Serie entfernt. Ohne den Nachimport steht
            // die Serie leer in der Mediathek, und der Anwender muss von Hand neu einlesen.
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "TKKG", SpotifyArtistId = "sp_tkkg", IsOnlineImported = true },
                cancellationToken: TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodes = new();
            FakeEpisodeImportSource source = new(
            [
                new EchoPlay.Core.Models.Import.ImportEpisode
                {
                    SourceEpisodeId = "ep-1",
                    Title = "Der Superhund",
                    EpisodeNumber = 1
                }
            ]);

            (OnlineLibraryViewModel viewModel, _) = Build(
                seriesService: seriesService,
                episodeService: episodes,
                episodeSource: source);

            await viewModel.LoadAsync();

            Assert.Equal(["sp_tkkg"], source.RequestedSeriesIds);
            IReadOnlyList<EchoPlay.Data.Entities.Library.Episode> gespeichert =
                await episodes.GetBySeriesIdAsync(seriesService.All[0].Id, TestContext.Current.CancellationToken);
            Assert.Equal("Der Superhund", Assert.Single(gespeichert).Title);
        }

        [Fact]
        public async Task SerieMitFolgen_WirdNichtNachimportiert()
        {
            // Der Nachimport ist eine Reparatur, keine Routine — wo Folgen liegen, wird
            // der Anbieter beim Laden nicht gefragt.
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG", SpotifyArtistId = "sp_tkkg", IsOnlineImported = true };
            await seriesService.AddAsync(series, cancellationToken: TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodes = new();
            await episodes.AddAsync(
                new EchoPlay.Data.Entities.Library.Episode { SeriesId = series.Id, Title = "Folge 1" },
                TestContext.Current.CancellationToken);

            FakeEpisodeImportSource source = new([]);

            (OnlineLibraryViewModel viewModel, _) = Build(
                seriesService: seriesService,
                episodeService: episodes,
                episodeSource: source);

            await viewModel.LoadAsync();

            Assert.Empty(source.RequestedSeriesIds);
        }

        private static SeriesCardViewModel BuildCard()
        {
            // Ohne Cover: Ein Bildobjekt braucht ein laufendes Fenster.
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            ServiceProvider provider = services.BuildServiceProvider();

            return new SeriesCardViewModel(
                Guid.NewGuid(),
                "TKKG",
                coverImage: null,
                totalEpisodeCount: 3,
                newEpisodeCount: 0,
                inProgressCount: 0,
                finishedCount: 0,
                isSubscribed: true,
                isFavorite: false,
                isWatched: false,
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeConfirmationDialogService(),
                new FakeLocalizationService());
        }

        /// <summary>
        /// Die Sortierung wird nebenher gespeichert; gewartet wird auf den Schreibvorgang,
        /// nicht auf eine geratene Zeitspanne.
        /// </summary>
        private static async Task WaitForSaveAsync(FakeAppSettingsDataService settings)
        {
            for (int versuch = 0; versuch < 200 && settings.SaveCallCount == 0; versuch++)
            {
                await Task.Yield();
            }
        }
    }
}

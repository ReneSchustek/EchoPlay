using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft das Übernehmen mehrerer Suchtreffer auf einmal: Was ticked ist, wird
    /// eingelesen; was schon in der Mediathek steht, bleibt außen vor.
    /// </summary>
    /// <remarks>
    /// Der Sammel-Import ist der Weg für jemanden, der eine ganze Reihe auf einmal
    /// aufnimmt. Übergeht er die Auswahl, holt er entweder zu viel oder nichts, und
    /// beides fällt erst auf, wenn der Anwender die Mediathek durchsieht.
    /// </remarks>
    public sealed class OnlineProviderSelectionTests
    {
        [Fact]
        public async Task AddSelected_WithoutAnySelection_ImportsNothing()
        {
            OnlineLibraryViewModel sut = await SearchAsync(
                new ImportSeries { Title = "TKKG", Source = "Spotify", SourceSeriesId = "s1", Score = 90 });

            sut.AddSelectedCommand.Execute(null);

            // Ohne Haken gibt es nichts zu übernehmen; ein Klick daneben darf nicht die
            // ganze Trefferliste einlesen.
            foreach (SearchResultViewModel result in sut.ProviderSearchResults)
            {
                Assert.False(result.IsImported);
            }
        }

        [Fact]
        public async Task AddSelected_ImportsTheTickedResults()
        {
            OnlineLibraryViewModel sut = await SearchAsync(
                new ImportSeries { Title = "TKKG", Source = "Spotify", SourceSeriesId = "s1", Score = 90 },
                new ImportSeries { Title = "Bibi Blocksberg", Source = "Spotify", SourceSeriesId = "s2", Score = 80 });

            SearchResultViewModel ticked = sut.ProviderSearchResults[0];
            SearchResultViewModel skipped = sut.ProviderSearchResults[1];
            ticked.IsSelected = true;

            sut.AddSelectedCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                ticked,
                () => ticked.IsImported,
                "Der tickede Treffer ist übernommen");

            Assert.True(ticked.IsImported);
            Assert.False(skipped.IsImported);
        }

        [Fact]
        public async Task AddSelected_SkipsResultsThatAreAlreadyInTheLibrary()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "TKKG", SpotifyArtistId = "s1", IsOnlineImported = true },
                TestContext.Current.CancellationToken);

            OnlineLibraryViewModel sut = await SearchWithStoreAsync(
                seriesService,
                [new ImportSeries { Title = "TKKG", Source = "Spotify", SourceSeriesId = "s1", Score = 90 }]);

            SearchResultViewModel alreadyThere = sut.ProviderSearchResults[0];
            alreadyThere.IsSelected = true;
            sut.AddSelectedCommand.Execute(null);

            // Eine bereits alreadyTheree Serie ein zweites Mal einzulesen erzeugt Doppel in
            // der Mediathek, daher trägt der Treffer schon vor dem Klick den Vermerk.
            Assert.True(alreadyThere.IsImported);
            _ = Assert.Single(seriesService.All);
        }

        private static Task<OnlineLibraryViewModel> SearchAsync(params ImportSeries[] results)
            => SearchWithStoreAsync(new FakeSeriesDataService(), results);

        private static async Task<OnlineLibraryViewModel> SearchWithStoreAsync(
            FakeSeriesDataService seriesService, ImportSeries[] results)
        {
            OnlineLibraryViewModel viewModel = Build(results, seriesService);
            viewModel.SearchText = "TKKG";

            viewModel.ProviderSearchCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel,
                () => viewModel.ProviderSearchResults.Count == results.Length,
                "Trefferliste der Anbieter-Suche steht");

            return viewModel;
        }

        private static OnlineLibraryViewModel Build(
            IReadOnlyList<ImportSeries> searchResults, FakeSeriesDataService seriesService)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddScoped<IAppSettingsDataService>(
                _ => new FakeAppSettingsDataService(new AppSettings { ActiveProvider = ProviderType.Spotify }));
            _ = services.AddSingleton<EchoPlay.Spotify.Auth.ISpotifyClientCredentialsProvider>(
                FakeSpotifyClientCredentialsProvider.WithCredentials());
            ImportTestServices.AddProviderFakes(services, spotifyResults: searchResults);
            ImportTestServices.AddCoverChain(services);

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            ImportService importService = ImportTestServices.CreateImportService(provider);

            return new OnlineLibraryViewModel(
                new OnlineLibraryActionsContext
                {
                    ScopeFactory = scopeFactory,
                    ConfirmationDialogService = new FakeConfirmationDialogService(),
                    ImportService = importService,
                    ErrorDialogService = new FakeErrorDialogService(),
                    LocalizationService = new FakeLocalizationService(),
                    OnlineAccessGuard = new FakeOnlineAccessGuard(),
                    CoverDownloader = provider.GetRequiredService<ICoverDownloader>(),
                });
        }
    }
}

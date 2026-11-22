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
    /// Prüft die Anbieter-Suche aus der Online-Mediathek heraus: Reihenfolge der Treffer,
    /// Kennzeichnung bereits vorhandener Serien und das Übernehmen der Auswahl.
    /// </summary>
    /// <remarks>
    /// Die Reihenfolge ist die eigentliche Leistung der Suche: Treffer, die den Suchbegriff
    /// im Namen tragen, stehen vorn, erst danach entscheidet die Bewertung des Anbieters.
    /// Ohne diese Regel verschwindet die gesuchte Serie zwischen gleichnamigen Alben.
    /// </remarks>
    public sealed class OnlineProviderSearchTests
    {
        [Fact]
        public async Task Search_PutsHitsWithTheQueryFirst()
        {
            List<ImportSeries> hit =
            [
                new() { Title = "Hörspiel-Sammlung", Source = "Spotify", SourceSeriesId = "s1", Score = 90 },
                new() { Title = "TKKG", Source = "Spotify", SourceSeriesId = "s2", Score = 10 }
            ];

            OnlineLibraryViewModel viewModel = Build(hit);
            viewModel.SearchText = "TKKG";

            viewModel.ProviderSearchCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel,
                () => viewModel.ProviderSearchResults.Count == 2,
                "Trefferliste der Anbieter-Suche steht");

            Assert.Equal(["TKKG", "Hörspiel-Sammlung"], viewModel.ProviderSearchResults.Select(r => r.Title));
        }

        [Fact]
        public async Task Search_SortsByScoreWhenEquallyRelevant()
        {
            List<ImportSeries> hit =
            [
                new() { Title = "TKKG Junior", Source = "Spotify", SourceSeriesId = "s1", Score = 30 },
                new() { Title = "TKKG", Source = "Spotify", SourceSeriesId = "s2", Score = 80 }
            ];

            OnlineLibraryViewModel viewModel = Build(hit);
            viewModel.SearchText = "TKKG";

            viewModel.ProviderSearchCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel,
                () => viewModel.ProviderSearchResults.Count == 2,
                "Trefferliste der Anbieter-Suche steht");

            Assert.Equal(["TKKG", "TKKG Junior"], viewModel.ProviderSearchResults.Select(r => r.Title));
        }

        [Fact]
        public async Task Search_MarksExistingSeries()
        {
            // Ohne die Kennzeichnung böte die Kachel ein zweites Mal „hinzufügen" an,
            // und der Anwender fragte sich, warum nichts passiert.
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "TKKG", SpotifyArtistId = "s2", IsOnlineImported = true },
                cancellationToken: TestContext.Current.CancellationToken);

            List<ImportSeries> hit =
            [
                new() { Title = "TKKG", Source = "Spotify", SourceSeriesId = "s2", Score = 80 }
            ];

            OnlineLibraryViewModel viewModel = Build(hit, seriesService);
            viewModel.SearchText = "TKKG";

            viewModel.ProviderSearchCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel,
                () => viewModel.ProviderSearchResults.Count == 1,
                "Trefferliste der Anbieter-Suche steht");

            Assert.True(Assert.Single(viewModel.ProviderSearchResults).IsImported);
        }

        [Fact]
        public async Task Search_DoesNotLeaveTheLoadingState()
        {
            OnlineLibraryViewModel viewModel = Build(
            [
                new() { Title = "TKKG", Source = "Spotify", SourceSeriesId = "s1", Score = 80 }
            ]);
            viewModel.SearchText = "TKKG";

            viewModel.ProviderSearchCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel,
                () => viewModel.ProviderSearchResults.Count == 1,
                "Trefferliste der Anbieter-Suche steht");

            Assert.False(viewModel.IsSearchingProvider);
        }

        [Fact]
        public async Task SelectionTakeover_SkipsHitsAlreadyPresent()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "TKKG", SpotifyArtistId = "s1", IsOnlineImported = true },
                cancellationToken: TestContext.Current.CancellationToken);

            OnlineLibraryViewModel viewModel = Build(
                [
                    new() { Title = "TKKG", Source = "Spotify", SourceSeriesId = "s1", Score = 80 }
                ],
                seriesService);
            viewModel.SearchText = "TKKG";

            viewModel.ProviderSearchCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel,
                () => viewModel.ProviderSearchResults.Count == 1,
                "Trefferliste der Anbieter-Suche steht");

            SearchResultViewModel hit = viewModel.ProviderSearchResults[0];
            hit.IsSelected = true;

            viewModel.AddSelectedCommand.Execute(null);

            // Die Serie liegt schon in der Ablage, ein zweiter Import würde sie doppeln.
            _ = Assert.Single(seriesService.All);
        }

        private static OnlineLibraryViewModel Build(
            IReadOnlyList<ImportSeries> searchResults,
            FakeSeriesDataService? seriesService = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService ?? new FakeSeriesDataService());
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

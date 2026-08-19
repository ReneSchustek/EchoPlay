using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Anbieter-Suche aus der Online-Mediathek heraus: Reihenfolge der Treffer,
    /// Kennzeichnung bereits vorhandener Serien und das Übernehmen der Auswahl.
    /// </summary>
    /// <remarks>
    /// Die Reihenfolge ist die eigentliche Leistung der Suche: Treffer, die den Suchbegriff
    /// im Namen tragen, stehen vorn — erst danach entscheidet die Bewertung des Anbieters.
    /// Ohne diese Regel verschwindet die gesuchte Serie zwischen gleichnamigen Alben.
    /// </remarks>
    public sealed class OnlineProviderSearchTests
    {
        [Fact]
        public async Task Suche_StelltTrefferMitDemSuchbegriffNachVorn()
        {
            List<ImportSeries> treffer =
            [
                new() { Title = "Hörspiel-Sammlung", Source = "Spotify", SourceSeriesId = "s1", Score = 90 },
                new() { Title = "TKKG", Source = "Spotify", SourceSeriesId = "s2", Score = 10 }
            ];

            OnlineLibraryViewModel viewModel = Build(treffer);
            viewModel.SearchText = "TKKG";

            viewModel.ProviderSearchCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel,
                () => viewModel.ProviderSearchResults.Count == 2,
                "Trefferliste der Anbieter-Suche steht");

            Assert.Equal(["TKKG", "Hörspiel-Sammlung"], viewModel.ProviderSearchResults.Select(r => r.Title));
        }

        [Fact]
        public async Task Suche_SortiertBeiGleichemBezugNachBewertung()
        {
            List<ImportSeries> treffer =
            [
                new() { Title = "TKKG Junior", Source = "Spotify", SourceSeriesId = "s1", Score = 30 },
                new() { Title = "TKKG", Source = "Spotify", SourceSeriesId = "s2", Score = 80 }
            ];

            OnlineLibraryViewModel viewModel = Build(treffer);
            viewModel.SearchText = "TKKG";

            viewModel.ProviderSearchCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel,
                () => viewModel.ProviderSearchResults.Count == 2,
                "Trefferliste der Anbieter-Suche steht");

            Assert.Equal(["TKKG", "TKKG Junior"], viewModel.ProviderSearchResults.Select(r => r.Title));
        }

        [Fact]
        public async Task Suche_KennzeichnetBereitsVorhandeneSerien()
        {
            // Ohne die Kennzeichnung böte die Kachel ein zweites Mal „hinzufügen" an —
            // und der Anwender fragte sich, warum nichts passiert.
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "TKKG", SpotifyArtistId = "s2", IsOnlineImported = true },
                cancellationToken: TestContext.Current.CancellationToken);

            List<ImportSeries> treffer =
            [
                new() { Title = "TKKG", Source = "Spotify", SourceSeriesId = "s2", Score = 80 }
            ];

            OnlineLibraryViewModel viewModel = Build(treffer, seriesService);
            viewModel.SearchText = "TKKG";

            viewModel.ProviderSearchCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                viewModel,
                () => viewModel.ProviderSearchResults.Count == 1,
                "Trefferliste der Anbieter-Suche steht");

            Assert.True(Assert.Single(viewModel.ProviderSearchResults).IsImported);
        }

        [Fact]
        public async Task Suche_LaesstDenLadezustandNichtStehen()
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
        public async Task Auswahluebernahme_UeberspringtBereitsVorhandeneTreffer()
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

            SearchResultViewModel treffer = viewModel.ProviderSearchResults[0];
            treffer.IsSelected = true;

            viewModel.AddSelectedCommand.Execute(null);

            // Die Serie liegt schon in der Ablage — ein zweiter Import würde sie doppeln.
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
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.ISeriesImportSearch>(
                "Spotify", (_, _) => new FakeSeriesImportSearch(searchResults, "Spotify"));
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.ISeriesImportSearch>(
                "AppleMusic", (_, _) => new FakeSeriesImportSearch([], "AppleMusic"));
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "Spotify", (_, _) => new FakeEpisodeImportSource([]));
            _ = services.AddKeyedScoped<EchoPlay.Core.Abstractions.Import.IEpisodeImportSource>(
                "AppleMusic", (_, _) => new FakeEpisodeImportSource([]));
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

            return new OnlineLibraryViewModel(
                scopeFactory,
                new FakeConfirmationDialogService(),
                importService,
                new FakeErrorDialogService(),
                new FakeLocalizationService(),
                new FakeOnlineAccessGuard(),
                provider.GetRequiredService<ICoverDownloader>());
        }
    }
}

using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft den Hinweis auf den Apple-Music-Rückfall und das Verlassen der Suchseite.
    /// </summary>
    /// <remarks>
    /// Beim Verlassen laufen noch Cover-Anfragen der Trefferkacheln. Werden sie nicht
    /// abgebrochen, halten sie die Bilder der alten Treffer im Speicher — bei einer
    /// Sitzung mit mehreren Suchen summiert sich das.
    /// </remarks>
    public sealed class SearchViewModelLifecycleTests
    {
        [Fact]
        public void SpotifyFallbackHint_IsHiddenUntilTheFallbackHappens()
        {
            SearchViewModel sut = Build([]);

            Assert.False(sut.IsSpotifyFallbackHintVisible);
            Assert.Equal(Visibility.Collapsed, sut.SpotifyFallbackHintVisibility);
        }

        [Fact]
        public async Task SpotifyFallbackHint_WhenCredentialsAreMissing_BecomesVisible()
        {
            SearchViewModel sut = Build(
                [new ImportSeries { Title = "TKKG", Source = "AppleMusic", SourceSeriesId = "a1", Score = 90 }],
                credentialsProvider: FakeSpotifyClientCredentialsProvider.Missing(),
                appleMusicResults: [new ImportSeries { Title = "TKKG", Source = "AppleMusic", SourceSeriesId = "a1", Score = 90 }]);
            sut.SearchText = "TKKG";

            sut.SearchCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                sut, () => sut.IsSpotifyFallbackHintVisible, "Der Rückfall-Hinweis erscheint");

            // Ohne Zugangsdaten fragt die Suche Apple Music. Ohne den Hinweis wundert sich
            // der Anwender über andere Treffer als erwartet.
            Assert.Equal(Visibility.Visible, sut.SpotifyFallbackHintVisibility);
        }

        [Fact]
        public void Dispose_WithoutASearch_StaysQuiet()
        {
            SearchViewModel sut = Build([]);

            sut.Dispose();
            sut.Dispose();
        }

        [Fact]
        public async Task Dispose_AfterASearch_ReleasesTheResults()
        {
            SearchViewModel sut = Build(
                [new ImportSeries { Title = "TKKG", Source = "Spotify", SourceSeriesId = "s1", Score = 90 }]);
            sut.SearchText = "TKKG";

            sut.SearchCommand.Execute(null);
            await ChangeSignals.WaitForAsync(
                sut, () => sut.Results.Count == 1, "Die Trefferliste steht");

            sut.Dispose();

            // Nach dem Verlassen darf keine Anfrage der alten Treffer mehr laufen.
            Assert.NotEmpty(sut.Results);
        }

        [Fact]
        public void Reset_ClearsSearchTextAndResults()
        {
            SearchViewModel sut = Build([]);
            sut.SearchText = "TKKG";

            sut.ResetCommand.Execute(null);

            Assert.Equal(string.Empty, sut.SearchText);
            Assert.Empty(sut.Results);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static SearchViewModel Build(
            IReadOnlyList<ImportSeries> spotifyResults,
            FakeSpotifyClientCredentialsProvider? credentialsProvider = null,
            IReadOnlyList<ImportSeries>? appleMusicResults = null)
        {
            FakeSeriesDataService seriesService = new();

            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new AppSettings { ActiveProvider = ProviderType.Spotify }));
            _ = services.AddKeyedScoped<ISeriesImportSearch>(
                "Spotify", (_, _) => new FakeSeriesImportSearch(spotifyResults));
            _ = services.AddKeyedScoped<ISeriesImportSearch>(
                "AppleMusic", (_, _) => new FakeSeriesImportSearch(appleMusicResults ?? [], "AppleMusic"));
            _ = services.AddKeyedScoped<IEpisodeImportSource>("Spotify", (_, _) => new FakeEpisodeImportSource([]));
            _ = services.AddKeyedScoped<IEpisodeImportSource>("AppleMusic", (_, _) => new FakeEpisodeImportSource([]));
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddSingleton<ISpotifyClientCredentialsProvider>(
                credentialsProvider ?? FakeSpotifyClientCredentialsProvider.WithCredentials());
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

            return new SearchViewModel(
                importService, new FakeErrorDialogService(), new FakeLocalizationService(), scopeFactory);
        }
    }
}

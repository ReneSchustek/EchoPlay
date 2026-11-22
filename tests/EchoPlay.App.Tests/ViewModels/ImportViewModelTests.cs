using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="ImportViewModel"/>.
    /// Prüft Suche, Import-Ablauf, Statustext und Guard-Logik.
    /// </summary>
    public sealed class ImportViewModelTests
    {
        /// <summary>Einstellungen für <see cref="BuildViewModel"/>: die Nachbauten, aus denen der Prüfling gebaut wird.</summary>
        private sealed record ImportViewSetup
        {
            public required FakeAppSettingsDataService SettingsService { get; init; }
            public required FakeSeriesDataService SeriesService { get; init; }
            public required FakeEpisodeDataService EpisodeService { get; init; }
            public required FakeSeriesImportSearch SpotifySearch { get; init; }
            public required FakeSeriesImportSearch AppleMusicSearch { get; init; }
            public required FakeEpisodeImportSource SpotifyEpisodeSource { get; init; }
            public required FakeEpisodeImportSource AppleMusicEpisodeSource { get; init; }
            public FakeErrorDialogService? ErrorDialogService { get; init; }
            public FakeOnlineAccessGuard? OnlineGuard { get; init; }
        }

        /// <summary>
        /// Baut eine vollständige <see cref="ImportViewModel"/>-Instanz mit Fakes.
        /// </summary>
        private static ImportViewModel BuildViewModel(ImportViewSetup setup)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => setup.SettingsService);
            _ = services.AddScoped<ISeriesDataService>(_ => setup.SeriesService);
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => setup.EpisodeService);
            _ = services.AddKeyedScoped<ISeriesImportSearch>("Spotify", (_, _) => setup.SpotifySearch);
            _ = services.AddKeyedScoped<ISeriesImportSearch>("AppleMusic", (_, _) => setup.AppleMusicSearch);
            _ = services.AddKeyedScoped<IEpisodeImportSource>("Spotify", (_, _) => setup.SpotifyEpisodeSource);
            _ = services.AddKeyedScoped<IEpisodeImportSource>("AppleMusic", (_, _) => setup.AppleMusicEpisodeSource);
            _ = services.AddSingleton<ISpotifyClientCredentialsProvider>(
                FakeSpotifyClientCredentialsProvider.WithCredentials());

            _ = services.AddSingleton<ILoggerFactory>(new FakeLoggerFactory());
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
                provider.GetRequiredService<ILoggerFactory>());
            IErrorDialogService errorSvc = setup.ErrorDialogService ?? new FakeErrorDialogService();
            EchoPlay.App.Services.IOnlineAccessGuard guard = setup.OnlineGuard ?? new FakeOnlineAccessGuard();

            return new ImportViewModel(importService, errorSvc, guard, new FakeLocalizationService());
        }

        [Fact]
        public async Task SearchAsync_DoesNothing_WhenQueryIsEmpty()
        {
            // Leerer Suchbegriff darf keine Suche auslösen
            FakeSeriesImportSearch search = new([], "Spotify");
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });

            ImportViewModel vm = BuildViewModel(new()
            {
                SettingsService = settings,
                SeriesService = new FakeSeriesDataService(),
                EpisodeService = new FakeEpisodeDataService(),
                SpotifySearch = search,
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            vm.SearchQuery = string.Empty;
            await vm.SearchAsync();

            Assert.Empty(vm.Results);
        }

        [Fact]
        public async Task SearchAsync_SetsResults_WithCorrectCount()
        {
            // Suchergebnisse müssen als ImportResultViewModel-Liste zurückgegeben werden
            IReadOnlyList<ImportSeries> spotifyResults =
            [
                new() { SourceSeriesId = "sp1", Source = "Spotify", Title = "TKKG", IsHoerspiel = true, Score = 80 },
                new() { SourceSeriesId = "sp2", Source = "Spotify", Title = "TKKG Junior", IsHoerspiel = true, Score = 70 }
            ];

            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });

            ImportViewModel vm = BuildViewModel(new()
            {
                SettingsService = settings,
                SeriesService = new FakeSeriesDataService(),
                EpisodeService = new FakeEpisodeDataService(),
                SpotifySearch = new FakeSeriesImportSearch(spotifyResults, "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            vm.SearchQuery = "TKKG";
            await vm.SearchAsync();

            Assert.Equal(2, vm.Results.Count);
        }

        [Fact]
        public async Task SearchAsync_SetsStatusText_WhenNoResults()
        {
            // StatusText muss "Keine Ergebnisse" signalisieren
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });

            ImportViewModel vm = BuildViewModel(new()
            {
                SettingsService = settings,
                SeriesService = new FakeSeriesDataService(),
                EpisodeService = new FakeEpisodeDataService(),
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            vm.SearchQuery = "unbekannt";
            await vm.SearchAsync();

            Assert.False(string.IsNullOrEmpty(vm.StatusText));
        }

        [Fact]
        public async Task SearchAsync_ResetsIsSearching_AfterCompletion()
        {
            // IsSearching muss nach dem Abschluss wieder false sein
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });

            ImportViewModel vm = BuildViewModel(new()
            {
                SettingsService = settings,
                SeriesService = new FakeSeriesDataService(),
                EpisodeService = new FakeEpisodeDataService(),
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            vm.SearchQuery = "test";
            await vm.SearchAsync();

            Assert.False(vm.IsSearching);
        }

        [Fact]
        public async Task ImportAsync_FiresImportSucceeded_OnSuccess()
        {
            // ImportSucceeded muss nach einem erfolgreichen Import gefeuert werden
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });

            ImportViewModel vm = BuildViewModel(new()
            {
                SettingsService = settings,
                SeriesService = new FakeSeriesDataService(),
                EpisodeService = new FakeEpisodeDataService(),
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            bool succeeded = false;
            vm.ImportSucceeded += (_, _) => succeeded = true;

            ImportSeries series = new()
            {
                SourceSeriesId = "sp1",
                Source = "Spotify",
                Title = "TKKG",
                IsHoerspiel = true,
                Score = 80
            };

            await vm.ImportAsync(series);

            Assert.True(succeeded);
        }

        [Fact]
        public async Task ImportAsync_SetsStatusText_AfterSuccess()
        {
            // StatusText nach Import muss gesetzt sein (lokalisierter Erfolgstext)
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });

            ImportViewModel vm = BuildViewModel(new()
            {
                SettingsService = settings,
                SeriesService = new FakeSeriesDataService(),
                EpisodeService = new FakeEpisodeDataService(),
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            ImportSeries series = new()
            {
                SourceSeriesId = "sp1",
                Source = "Spotify",
                Title = "Die drei Fragezeichen",
                IsHoerspiel = true,
                Score = 90
            };

            await vm.ImportAsync(series);

            Assert.False(string.IsNullOrEmpty(vm.StatusText));
        }

        [Fact]
        public async Task ImportAsync_ResetsIsImporting_AfterCompletion()
        {
            // IsImporting muss nach dem Abschluss wieder false sein
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });

            ImportViewModel vm = BuildViewModel(new()
            {
                SettingsService = settings,
                SeriesService = new FakeSeriesDataService(),
                EpisodeService = new FakeEpisodeDataService(),
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            await vm.ImportAsync(new ImportSeries
            {
                SourceSeriesId = "x1",
                Source = "Spotify",
                Title = "Test",
                IsHoerspiel = true,
                Score = 50
            });

            Assert.False(vm.IsImporting);
        }

        [Fact]
        public async Task IsSearchEnabled_IsTrue_WhenIdle()
        {
            // Im Ruhezustand muss der Suchen-Button aktiviert sein
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });

            ImportViewModel vm = BuildViewModel(new()
            {
                SettingsService = settings,
                SeriesService = new FakeSeriesDataService(),
                EpisodeService = new FakeEpisodeDataService(),
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            Assert.True(vm.IsSearchEnabled);
        }
    }
}

using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="StartupValidator"/>.
    /// Prüft die Startup-Validierungslogik mit Fakes – ohne HTTP-Calls oder Dateisystem.
    /// </summary>
    public sealed class StartupValidatorTests
    {
        /// <summary>
        /// Baut einen StartupValidator mit konfigurierbaren Fakes.
        /// </summary>
        private static StartupValidator BuildValidator(
            FakeAppSettingsDataService? settingsService = null,
            FakeSeriesDataService? seriesService = null,
            FakeCachedNewReleaseDataService? cacheService = null,
            FakeCoverImageDataService? coverImageService = null,
            BackgroundCoverService? coverServiceOverride = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => settingsService ?? new FakeAppSettingsDataService());
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService ?? new FakeSeriesDataService());
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => cacheService ?? new FakeCachedNewReleaseDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImageService ?? new FakeCoverImageDataService());
            // Microsoft.Extensions.Http registriert den Default-IHttpClientFactory.
            // Die Tests lösen keinen echten Online-Check aus (OfflineMode = true), sodass
            // die Factory nur konstruktor-seitig gebraucht wird und keine Netzwerkzugriffe entstehen.
            _ = services.AddHttpClient();

            ServiceProvider provider = services.BuildServiceProvider();

            // BackgroundCoverService braucht echte DI-Infrastruktur – wir nutzen einen minimalen Stub.
            // Im Default-Fall zählt FakeBackgroundCoverService nur die Aufrufe der Splash-/Hintergrund-Phasen
            // (keine DB-/Dateisystem-Arbeit). Tests, die den echten Pfad prüfen wollen, übergeben coverServiceOverride.
            BackgroundCoverService coverService = coverServiceOverride ?? new FakeBackgroundCoverService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IHttpClientFactory>());

            return new StartupValidator(
                provider.GetRequiredService<IServiceScopeFactory>(),
                coverService,
                provider.GetRequiredService<IHttpClientFactory>(),
                new FakeLoggerFactory(),
                new FakeClock());
        }

        [Fact]
        public async Task ValidateAsync_ReturnsResult_WithDefaults()
        {
            StartupValidator validator = BuildValidator();

            StartupResult result = await validator.ValidateAsync();

            Assert.NotNull(result);
            Assert.NotNull(result.Settings);
            Assert.NotNull(result.SubscribedSeries);
        }

        [Fact]
        public async Task ValidateAsync_OfflineMode_SetsOnlineUnavailable()
        {
            FakeAppSettingsDataService settings = new(new AppSettings { OfflineMode = true });
            StartupValidator validator = BuildValidator(settingsService: settings);

            StartupResult result = await validator.ValidateAsync();

            Assert.False(result.IsOnlineAvailable);
        }

        [Fact]
        public async Task ValidateAsync_CacheCleared_RemovesEntries()
        {
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                ClearCacheOnNextStart = true,
                OfflineMode = true
            });

            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "Test", IsSubscribed = true });
            Series series = seriesService.All[0];

            FakeCachedNewReleaseDataService cacheService = new(
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Series = series,
                    Title = "Alte Folge",
                    CollectionId = 1,
                    ReleaseDate = TestIds.ReferenceDate.AddDays(-5),
                    CheckedAtUtc = TestIds.ReferenceDate
                }
            ]);

            StartupValidator validator = BuildValidator(
                settingsService: settings,
                seriesService: seriesService,
                cacheService: cacheService);

            StartupResult result = await validator.ValidateAsync();

            // Cache muss nach Clear leer sein
            Assert.Empty(result.CachedReleases);
        }

        [Fact]
        public async Task ValidateAsync_CacheClear_ResetsFlag()
        {
            AppSettings appSettings = new() { ClearCacheOnNextStart = true, OfflineMode = true };
            FakeAppSettingsDataService settings = new(appSettings);

            StartupValidator validator = BuildValidator(settingsService: settings);
            _ = await validator.ValidateAsync();

            // Flag muss nach dem Durchlauf zurückgesetzt sein
            AppSettings reloaded = await settings.GetAsync();
            Assert.False(reloaded.ClearCacheOnNextStart);
        }

        [Fact]
        public async Task ValidateAsync_RemovesCacheForUnwatchedSeries()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series
            {
                Title = "Nicht überwacht",
                IsSubscribed = true,
                IsWatched = false
            });
            Series series = seriesService.All[0];

            FakeCachedNewReleaseDataService cacheService = new(
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Series = series,
                    Title = "Sollte verschwinden",
                    CollectionId = 99,
                    ReleaseDate = TestIds.ReferenceDate.AddDays(-1),
                    CheckedAtUtc = TestIds.ReferenceDate
                }
            ]);

            FakeAppSettingsDataService settings = new(new AppSettings { OfflineMode = true });

            StartupValidator validator = BuildValidator(
                settingsService: settings,
                seriesService: seriesService,
                cacheService: cacheService);

            StartupResult result = await validator.ValidateAsync();

            // Cache-Einträge für nicht-überwachte Serien müssen entfernt sein
            Assert.Empty(result.CachedReleases);
        }

        [Fact]
        public async Task ValidateAsync_CallsStatusCallback()
        {
            StartupValidator validator = BuildValidator(
                settingsService: new FakeAppSettingsDataService(new AppSettings { OfflineMode = true }));

            List<string> statusMessages = [];
            _ = await validator.ValidateAsync(status => statusMessages.Add(status));

            Assert.NotEmpty(statusMessages);
            Assert.Contains(statusMessages, s => s.Contains("Einstellungen", StringComparison.Ordinal));
            Assert.Contains(statusMessages, s => s.Contains("Dashboard", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ValidateAsync_RunsOnlySeriesCoverPhase_OnSplashPath()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(new AppSettings { OfflineMode = true }));
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => new FakeCachedNewReleaseDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddHttpClient();
            ServiceProvider provider = services.BuildServiceProvider();

            FakeBackgroundCoverService fakeCover = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IHttpClientFactory>());

            StartupValidator validator = BuildValidator(
                settingsService: new FakeAppSettingsDataService(new AppSettings { OfflineMode = true }),
                coverServiceOverride: fakeCover);

            _ = await validator.ValidateAsync();

            // Splash darf ausschließlich die Serien-Phase anstossen.
            Assert.Equal(1, fakeCover.RunSeriesCoversCallCount);
            Assert.Equal(0, fakeCover.RunOnceCallCount);
        }

        [Fact]
        public async Task ValidateAsync_OfflineMode_PassesIsOnlineFalseToSeriesCoverPhase()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(new AppSettings { OfflineMode = true }));
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => new FakeCachedNewReleaseDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddHttpClient();
            ServiceProvider provider = services.BuildServiceProvider();

            FakeBackgroundCoverService fakeCover = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IHttpClientFactory>());

            StartupValidator validator = BuildValidator(
                settingsService: new FakeAppSettingsDataService(new AppSettings { OfflineMode = true }),
                coverServiceOverride: fakeCover);

            _ = await validator.ValidateAsync();

            // Offline-Modus muss den Provider-URL-Download in der Serien-Phase sperren.
            Assert.False(fakeCover.LastIsOnlineAvailable);
        }

        [Fact]
        public async Task RunSeriesCoversOnceAsync_DoesNotLoadEpisodeCovers()
        {
            // Arrange: eine Serie + eine Episode, jeweils mit LocalFolderPath und ohne Cover.
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series
            {
                Title = "Testserie",
                LocalFolderPath = @"C:\Serien\Testserie"
            });
            Series series = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode
            {
                SeriesId = series.Id,
                Title = "Folge 1",
                LocalFolderPath = @"C:\Serien\Testserie\01"
            });

            CallCountingLocalCoverLoader coverLoader = new();

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddScoped<EchoPlay.LocalLibrary.Cover.ILocalCoverLoader>(_ => coverLoader);
            _ = services.AddScoped<ICoverCopyService>(_ => new FakeCoverCopyService());
            _ = services.AddHttpClient();
            ServiceProvider provider = services.BuildServiceProvider();

            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            FakeLoggerFactory loggerFactory = new();

            BackgroundCoverService realService = new(
                scopeFactory,
                new CoverService(scopeFactory, loggerFactory),
                provider.GetRequiredService<IHttpClientFactory>(),
                new FakeSpotifyCredentialStore(),
                new BackgroundCoverServiceOptions(),
                loggerFactory);

            // Act: Splash-Phase aufrufen — isOnlineAvailable=false verhindert Provider-Calls.
            _ = await realService.RunSeriesCoversOnceAsync(isOnlineAvailable: false, CancellationToken.None);

            // Assert: Der Cover-Loader wurde genau einmal für den Serien-Ordner aufgerufen,
            // niemals für den Episoden-Ordner.
            (string? FolderPath, string? TrackPath) onlyCall = Assert.Single(coverLoader.LoadCalls);
            Assert.Equal(@"C:\Serien\Testserie", onlyCall.FolderPath);
            Assert.DoesNotContain(coverLoader.LoadCalls, call => call.FolderPath == @"C:\Serien\Testserie\01");
        }

        /// <summary>
        /// Cover-Loader-Fake, der alle Aufrufe mit Ordner- und Track-Pfad protokolliert,
        /// um zu prüfen, dass die Splash-Phase nur Serien-Ordner ansteuert und Episoden überspringt.
        /// </summary>
        private sealed class CallCountingLocalCoverLoader : EchoPlay.LocalLibrary.Cover.ILocalCoverLoader
        {
            public List<(string? FolderPath, string? TrackPath)> LoadCalls { get; } = [];

            public Task<byte[]?> LoadAsync(string? episodeFolderPath, string? firstTrackPath)
            {
                LoadCalls.Add((episodeFolderPath, firstTrackPath));
                return Task.FromResult<byte[]?>(null);
            }
        }
    }
}

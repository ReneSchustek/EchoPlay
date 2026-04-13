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
            FakeCoverImageDataService? coverImageService = null)
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
            // RunOnceAsync wird nur bei Cache-Clear aufgerufen, und ohne echtes Dateisystem gibt es
            // keine Cover zu laden → der Test prüft nur die Ablauflogik.
            BackgroundCoverService coverService = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new CoverService(provider.GetRequiredService<IServiceScopeFactory>(), new FakeLoggerFactory()),
                provider.GetRequiredService<IHttpClientFactory>(),
                new FakeSpotifyCredentialStore(),
                new BackgroundCoverServiceOptions(),
                new FakeLoggerFactory());

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
    }
}

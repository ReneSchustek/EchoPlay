using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
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
            BackgroundCoverService? coverServiceOverride = null,
            FakeOnlineEpisodeChecker? onlineChecker = null,
            FakeClock? clock = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => settingsService ?? new FakeAppSettingsDataService());
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService ?? new FakeSeriesDataService());
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => cacheService ?? new FakeCachedNewReleaseDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImageService ?? new FakeCoverImageDataService());

            if (onlineChecker is not null)
            {
                _ = services.AddScoped<IOnlineEpisodeChecker>(_ => onlineChecker);
            }

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
                clock ?? new FakeClock());
        }

        /// <summary>
        /// Einstellungen ohne Netzzugriff: Ohne aktiven Anbieter überspringt der Validator
        /// den HTTP-Konnektivitätscheck und gilt trotzdem als online — damit lassen sich die
        /// Folgeschritte prüfen, ohne dass ein Test ins Netz geht.
        /// </summary>
        private static AppSettings OnlineOhneAnbieter() => new()
        {
            OfflineMode = false,
            ActiveProvider = ProviderType.None
        };

        [Fact]
        public async Task ValidateAsync_ReturnsResult_WithDefaults()
        {
            StartupValidator validator = BuildValidator();

            StartupResult result = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.NotNull(result.Settings);
            Assert.NotNull(result.SubscribedSeries);
        }

        [Fact]
        public async Task ValidateAsync_OfflineMode_SetsOnlineUnavailable()
        {
            FakeAppSettingsDataService settings = new(new AppSettings { OfflineMode = true });
            StartupValidator validator = BuildValidator(settingsService: settings);

            StartupResult result = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

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
            await seriesService.AddAsync(new Series { Title = "Test", IsSubscribed = true }, cancellationToken: TestContext.Current.CancellationToken);
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

            StartupResult result = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

            // Cache muss nach Clear leer sein
            Assert.Empty(result.CachedReleases);
        }

        [Fact]
        public async Task ValidateAsync_CacheClear_ResetsFlag()
        {
            AppSettings appSettings = new() { ClearCacheOnNextStart = true, OfflineMode = true };
            FakeAppSettingsDataService settings = new(appSettings);

            StartupValidator validator = BuildValidator(settingsService: settings);
            _ = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

            // Flag muss nach dem Durchlauf zurückgesetzt sein
            AppSettings reloaded = await settings.GetAsync(cancellationToken: TestContext.Current.CancellationToken);
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
            }, cancellationToken: TestContext.Current.CancellationToken);
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

            StartupResult result = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

            // Cache-Einträge für nicht-überwachte Serien müssen entfernt sein
            Assert.Empty(result.CachedReleases);
        }

        [Fact]
        public async Task ValidateAsync_CallsStatusCallback()
        {
            StartupValidator validator = BuildValidator(
                settingsService: new FakeAppSettingsDataService(new AppSettings { OfflineMode = true }));

            List<string> statusMessages = [];
            _ = await validator.ValidateAsync(status => statusMessages.Add(status), cancellationToken: TestContext.Current.CancellationToken);

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
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
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

            _ = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

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
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
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

            _ = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

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
            }, cancellationToken: TestContext.Current.CancellationToken);
            Series series = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode
            {
                SeriesId = series.Id,
                Title = "Folge 1",
                LocalFolderPath = @"C:\Serien\Testserie\01"
            }, cancellationToken: TestContext.Current.CancellationToken);

            CallCountingLocalCoverLoader coverLoader = new();

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
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
                loggerFactory,
                new FakeClock());

            // Act: Splash-Phase aufrufen — isOnlineAvailable=false verhindert Provider-Calls.
            _ = await realService.RunSeriesCoversOnceAsync(isOnlineAvailable: false, CancellationToken.None);

            // Assert: Der Cover-Loader wurde genau einmal für den Serien-Ordner aufgerufen,
            // niemals für den Episoden-Ordner.
            (string? FolderPath, string? TrackPath) onlyCall = Assert.Single(coverLoader.LoadCalls);
            Assert.Equal(@"C:\Serien\Testserie", onlyCall.FolderPath);
            Assert.DoesNotContain(coverLoader.LoadCalls, call => call.FolderPath == @"C:\Serien\Testserie\01");
        }

        // ── Lokales Verzeichnis ──────────────────────────────────────────────────

        [Fact]
        public async Task ValidateAsync_LocalFolderMissing_ReportsUnavailableWithHint()
        {
            // Ein Netzlaufwerk ohne Verbindung sieht genauso aus wie ein gelöschter Ordner:
            // Die Oberfläche muss die lokalen Funktionen sperren statt ins Leere zu laufen.
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                OfflineMode = true,
                LocalLibraryEnabled = true,
                LocalLibraryRootPath = @"Z:\gibt-es-nicht\EchoPlay"
            });

            StartupValidator validator = BuildValidator(settingsService: settings);

            StartupResult result = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result.IsLocalLibraryAvailable);
            Assert.Equal("StartupLocalLibraryUnavailableHint", result.LocalLibraryHintText);
        }

        [Fact]
        public async Task ValidateAsync_LocalFolderReadable_ReportsAvailableWithoutHint()
        {
            string ordner = Directory.CreateTempSubdirectory("echoplay-start").FullName;

            try
            {
                FakeAppSettingsDataService settings = new(new AppSettings
                {
                    OfflineMode = true,
                    LocalLibraryEnabled = true,
                    LocalLibraryRootPath = ordner
                });

                StartupValidator validator = BuildValidator(settingsService: settings);

                StartupResult result = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

                Assert.True(result.IsLocalLibraryAvailable);
                Assert.Null(result.LocalLibraryHintText);
            }
            finally
            {
                Directory.Delete(ordner, recursive: true);
            }
        }

        [Fact]
        public async Task ValidateAsync_LocalLibraryDisabled_SkipsFolderCheck()
        {
            // Ohne aktivierte lokale Bibliothek darf ein ungültiger Pfad kein Thema sein.
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                OfflineMode = true,
                LocalLibraryEnabled = false,
                LocalLibraryRootPath = @"Z:\gibt-es-nicht\EchoPlay"
            });

            StartupValidator validator = BuildValidator(settingsService: settings);

            StartupResult result = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.IsLocalLibraryAvailable);
            Assert.Null(result.LocalLibraryHintText);
        }

        // ── Neuerscheinungen-Cache ───────────────────────────────────────────────

        [Fact]
        public async Task ValidateAsync_WatchedSeries_FillsCacheFromChecker()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series
            {
                Title = "Überwacht",
                IsSubscribed = true,
                IsWatched = true
            }, cancellationToken: TestContext.Current.CancellationToken);
            Series series = seriesService.All[0];

            FakeOnlineEpisodeChecker checker = new(
            [
                new OnlineEpisodeCheckResult
                {
                    SeriesId = series.Id,
                    SeriesTitle = series.Title,
                    NewReleaseEpisodes =
                    [
                        new NewReleaseEpisode
                        {
                            Title = "Neue Folge",
                            EpisodeNumber = 42,
                            ReleaseDate = TestIds.ReferenceDate.AddDays(-1),
                            CollectionId = 4711
                        }
                    ]
                }
            ]);

            FakeCachedNewReleaseDataService cacheService = new();

            StartupValidator validator = BuildValidator(
                settingsService: new FakeAppSettingsDataService(OnlineOhneAnbieter()),
                seriesService: seriesService,
                cacheService: cacheService,
                onlineChecker: checker);

            StartupResult result = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, checker.CheckCallCount);
            CachedNewRelease eintrag = Assert.Single(result.CachedReleases);
            Assert.Equal("Neue Folge", eintrag.Title);
            Assert.Equal(series.Id, eintrag.SeriesId);
        }

        [Fact]
        public async Task ValidateAsync_NoWatchedSeries_SkipsProviderCheck()
        {
            // Abonniert, aber nicht überwacht: Für solche Serien gibt es keinen Grund,
            // den Anbieter zu befragen.
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series
            {
                Title = "Nur abonniert",
                IsSubscribed = true,
                IsWatched = false
            }, cancellationToken: TestContext.Current.CancellationToken);

            FakeOnlineEpisodeChecker checker = new();

            StartupValidator validator = BuildValidator(
                settingsService: new FakeAppSettingsDataService(OnlineOhneAnbieter()),
                seriesService: seriesService,
                onlineChecker: checker);

            _ = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, checker.CheckCallCount);
        }

        [Fact]
        public async Task ValidateAsync_RecentCheck_SkipsProviderCheck()
        {
            // Der Cache wurde vor weniger als 24 Stunden geprüft – ein erneuter Abruf
            // beim Anbieter wäre reine Last ohne neuen Erkenntniswert.
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series
            {
                Title = "Überwacht",
                IsSubscribed = true,
                IsWatched = true
            }, cancellationToken: TestContext.Current.CancellationToken);
            Series series = seriesService.All[0];

            FakeCachedNewReleaseDataService cacheService = new(
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Series = series,
                    Title = "Frisch geprüft",
                    CollectionId = 1,
                    ReleaseDate = TestIds.ReferenceDate,
                    CheckedAtUtc = TestIds.ReferenceDate.AddHours(-1)
                }
            ]);

            FakeOnlineEpisodeChecker checker = new();

            StartupValidator validator = BuildValidator(
                settingsService: new FakeAppSettingsDataService(OnlineOhneAnbieter()),
                seriesService: seriesService,
                cacheService: cacheService,
                onlineChecker: checker);

            _ = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, checker.CheckCallCount);
        }

        [Fact]
        public async Task ValidateAsync_ExpiredEntries_AreRemoved()
        {
            // Einträge älter als das Neuerscheinungs-Fenster gehören nicht mehr auf die Startseite.
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series
            {
                Title = "Überwacht",
                IsSubscribed = true,
                IsWatched = true
            }, cancellationToken: TestContext.Current.CancellationToken);
            Series series = seriesService.All[0];

            FakeCachedNewReleaseDataService cacheService = new(
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Series = series,
                    Title = "Uralt",
                    CollectionId = 7,
                    ReleaseDate = TestIds.ReferenceDate.AddYears(-1),
                    CheckedAtUtc = TestIds.ReferenceDate.AddHours(-1)
                }
            ]);

            StartupValidator validator = BuildValidator(
                settingsService: new FakeAppSettingsDataService(new AppSettings { OfflineMode = true }),
                seriesService: seriesService,
                cacheService: cacheService);

            StartupResult result = await validator.ValidateAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result.CachedReleases);
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

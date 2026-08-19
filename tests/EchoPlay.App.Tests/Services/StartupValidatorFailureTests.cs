using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft, was der Startlauf tut, wenn einzelne Schritte scheitern.
    /// </summary>
    /// <remarks>
    /// Der Startlauf steht zwischen dem Anwender und der Anwendung: Reißt er ab, sieht der
    /// Anwender nur das Startbild. Keiner der Schritte hier ist unverzichtbar — weder die
    /// Erreichbarkeitsprüfung noch das Nachladen der Cover noch der Abgleich der
    /// Neuerscheinungen. Sie dürfen scheitern, aber sie dürfen den Start nicht mitnehmen.
    ///
    /// Die Erreichbarkeitsprüfung läuft über einen eingesetzten Antwortgeber; kein Test
    /// dieser Datei geht ins Netz.
    /// </remarks>
    public sealed class StartupValidatorFailureTests
    {
        [Fact]
        public async Task Validate_WhenTheConnectivityCheckFails_ReportsOfflineWithAHint()
        {
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                OfflineMode = false,
                ActiveProvider = ProviderType.AppleMusic,
            });

            StartupValidator sut = Build(settings, new StubHandler(new HttpRequestException("kein Netz")));

            StartupResult result = await sut.ValidateAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            // Die Anwendung startet trotzdem — nur eben mit dem Hinweis, dass online
            // gerade nichts geht.
            Assert.False(result.IsOnlineAvailable);
            Assert.Equal("StartupOnlineUnavailableHint", result.OnlineHintText);
        }

        [Fact]
        public async Task Validate_WhenTheConnectivityCheckAnswersWithAnError_ReportsOffline()
        {
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                OfflineMode = false,
                ActiveProvider = ProviderType.AppleMusic,
            });

            StartupValidator sut = Build(settings, new StubHandler(HttpStatusCode.ServiceUnavailable));

            StartupResult result = await sut.ValidateAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            // Erreichbar heißt nicht antwortbereit: Eine Fehlerantwort zählt genauso wenig
            // wie gar keine Antwort.
            Assert.False(result.IsOnlineAvailable);
            Assert.Equal("StartupOnlineUnavailableHint", result.OnlineHintText);
        }

        [Fact]
        public async Task Validate_WhenTheConnectivityCheckAnswers_ReportsOnlineWithoutAHint()
        {
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                OfflineMode = false,
                ActiveProvider = ProviderType.AppleMusic,
            });

            StartupValidator sut = Build(settings, new StubHandler(HttpStatusCode.OK));

            StartupResult result = await sut.ValidateAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.IsOnlineAvailable);
            Assert.Null(result.OnlineHintText);
        }

        [Fact]
        public async Task Validate_WhenLoadingCoversFails_StillFinishesTheStart()
        {
            FakeBackgroundCoverService covers = new(
                BuildScopeFactory(),
                new FakeCoverDownloader())
            {
                SeriesCoversFailure = new InvalidOperationException("Cover-Speicher belegt"),
            };

            StartupValidator sut = Build(
                new FakeAppSettingsDataService(new AppSettings { OfflineMode = true }),
                new StubHandler(HttpStatusCode.OK),
                coverService: covers);

            StartupResult result = await sut.ValidateAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            // Ein Fehler beim Nachladen der Bilder darf die Anwendung nicht am Start hindern.
            Assert.NotNull(result.Settings);
            Assert.Equal(1, covers.RunSeriesCoversCallCount);
        }

        [Fact]
        public async Task Validate_WhenCoversWereLoaded_FinishesTheStart()
        {
            FakeBackgroundCoverService covers = new(
                BuildScopeFactory(),
                new FakeCoverDownloader())
            {
                SeriesCoversResult = 7,
            };

            StartupValidator sut = Build(
                new FakeAppSettingsDataService(new AppSettings { OfflineMode = true }),
                new StubHandler(HttpStatusCode.OK),
                coverService: covers);

            StartupResult result = await sut.ValidateAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(result.Settings);
            Assert.Equal(1, covers.RunSeriesCoversCallCount);
        }

        [Fact]
        public async Task Validate_WhenTheProviderFailsDuringTheReleaseCheck_KeepsTheOldCache()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "TKKG", IsSubscribed = true, IsWatched = true, AppleMusicArtistId = "am_tkkg" },
                TestContext.Current.CancellationToken);

            FakeAppSettingsDataService settings = new(new AppSettings
            {
                OfflineMode = false,
                ActiveProvider = ProviderType.None,
            });

            StartupValidator sut = Build(
                settings,
                new StubHandler(HttpStatusCode.OK),
                seriesService: seriesService,
                episodeChecker: new ThrowingEpisodeChecker());

            StartupResult result = await sut.ValidateAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            // Der Abgleich der Neuerscheinungen ist Beiwerk. Scheitert er, bleibt der
            // Startlauf grün und der Zwischenspeicher unverändert.
            Assert.True(result.IsOnlineAvailable);
            Assert.Empty(result.CachedReleases);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static IServiceScopeFactory BuildScopeFactory()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        }

        private static StartupValidator Build(
            FakeAppSettingsDataService settings,
            StubHandler handler,
            FakeSeriesDataService? seriesService = null,
            FakeBackgroundCoverService? coverService = null,
            IOnlineEpisodeChecker? episodeChecker = null)
        {
            FakeSeriesDataService series = seriesService ?? new FakeSeriesDataService();

            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => settings);
            _ = services.AddScoped<ISeriesDataService>(_ => series);
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => new FakeCachedNewReleaseDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());

            if (episodeChecker is not null)
            {
                _ = services.AddScoped<IOnlineEpisodeChecker>(_ => episodeChecker);
            }

            // Der eingesetzte Antwortgeber ersetzt die Gegenstelle vollständig —
            // die Erreichbarkeitsprüfung verlässt den Testlauf nicht.
            _ = services.AddHttpClient("OnlineCheck")
                .ConfigurePrimaryHttpMessageHandler(() => handler);

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            return new StartupValidator(
                scopeFactory,
                coverService ?? new FakeBackgroundCoverService(scopeFactory, new FakeCoverDownloader()),
                provider.GetRequiredService<IHttpClientFactory>(),
                new FakeLoggerFactory(),
                new FakeClock());
        }

        /// <summary>Antwortgeber, der entweder mit einem Zustand oder mit einem Fehler antwortet.</summary>
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly Exception? _failure;

            public StubHandler(HttpStatusCode status) => _status = status;

            public StubHandler(Exception failure)
            {
                _failure = failure;
                _status = HttpStatusCode.OK;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => _failure is not null
                    ? Task.FromException<HttpResponseMessage>(_failure)
                    : Task.FromResult(new HttpResponseMessage(_status));
        }

        /// <summary>Anbieter, der jede Anfrage mit einem Netzfehler beantwortet.</summary>
        private sealed class ThrowingEpisodeChecker : IOnlineEpisodeChecker
        {
            public Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckAllAsync(
                IReadOnlyList<CheckableSeriesInfo> subscribedSeries, CancellationToken cancellationToken = default)
                => Task.FromException<IReadOnlyList<OnlineEpisodeCheckResult>>(
                    new HttpRequestException("Anbieter nicht erreichbar"));

            public Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckNewReleasesAsync(
                IReadOnlyList<CheckableSeriesInfo> subscribedSeries, DateTime cutoffDate, CancellationToken cancellationToken = default)
                => Task.FromException<IReadOnlyList<OnlineEpisodeCheckResult>>(
                    new HttpRequestException("Anbieter nicht erreichbar"));
        }
    }
}

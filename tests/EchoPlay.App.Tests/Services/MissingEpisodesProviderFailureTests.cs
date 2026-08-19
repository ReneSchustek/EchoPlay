using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft, was der Bericht über fehlende Folgen tut, wenn der Anbieter nicht antwortet.
    /// </summary>
    /// <remarks>
    /// Der Bericht läuft über den ganzen Bestand. Reißt er beim ersten Anbieter-Fehler ab,
    /// bekommt der Anwender für keine einzige Serie ein Ergebnis — obwohl der lokale Teil
    /// ohne Netz auskommt und für sich schon nützlich ist.
    ///
    /// Die Tests arbeiten auf echten Ordnern im Temp-Verzeichnis.
    /// </remarks>
    public sealed class MissingEpisodesProviderFailureTests
    {
        [Fact]
        public async Task CheckSingleSeries_WhenTheProviderThrows_KeepsTheLocalPart()
        {
            string folder = CreateSeriesFolder("001 - Eins", "003 - Drei");
            try
            {
                Fixture fixture = await BuildAsync(folder);

                IReadOnlyList<string> result = await fixture.Coordinator.CheckSingleSeriesAsync(
                    fixture.SeriesId, folder, MissingEpisodesMode.WithOnline,
                    cancellationToken: TestContext.Current.CancellationToken);

                // Die Lücke bei Folge 2 steht auch dann im Bericht, wenn der Anbieter
                // nicht erreichbar ist.
                Assert.Contains(result, line => line.Contains("002", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckSingleSeries_WhenTheProviderThrows_ReturnsTheStatusBarToOffline()
        {
            string folder = CreateSeriesFolder("001 - Eins");
            try
            {
                Fixture fixture = await BuildAsync(folder);

                _ = await fixture.Coordinator.CheckSingleSeriesAsync(
                    fixture.SeriesId, folder, MissingEpisodesMode.WithOnline,
                    cancellationToken: TestContext.Current.CancellationToken);

                // Auch nach einem Fehlschlag darf die Anzeige nicht auf „vorübergehend
                // online" stehen bleiben.
                Assert.False(fixture.StatusBar.IsTemporarilyOnline);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckAllSeries_WhenTheProviderThrows_NamesTheSeriesWithItsReason()
        {
            string folder = CreateSeriesFolder("001 - Eins", "003 - Drei");
            try
            {
                Fixture fixture = await BuildAsync(folder);

                MissingEpisodesReport report = await fixture.Coordinator.CheckAllSeriesAsync(
                    MissingEpisodesMode.WithOnline,
                    cancellationToken: TestContext.Current.CancellationToken);

                // Die Serie fällt nicht aus dem Bericht heraus — sie trägt den Grund,
                // warum für sie nichts ermittelt werden konnte.
                SeriesMissingEpisodesResult entry = Assert.Single(report.Results);
                Assert.Equal("TKKG", entry.SeriesTitle);
                Assert.NotNull(entry.ErrorMessage);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private sealed class Fixture
        {
            public required MissingEpisodesCoordinator Coordinator { get; init; }
            public required Guid SeriesId { get; init; }
            public required StatusBarViewModel StatusBar { get; init; }
        }

        private static async Task<Fixture> BuildAsync(string seriesFolderPath)
        {
            FakeSeriesDataService seriesService = new();
            Series series = new()
            {
                Title = "TKKG",
                LocalFolderPath = seriesFolderPath,
                AppleMusicArtistId = "am_tkkg",
                IsSubscribed = true,
            };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IOnlineEpisodeChecker>(_ => new ThrowingEpisodeChecker());
            ServiceProvider provider = services.BuildServiceProvider();

            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            StatusBarViewModel statusBar = new(
                scopeFactory, new FakeThemeService(), new TaskbarProgressService(), new FakeClock());

            return new Fixture
            {
                Coordinator = new MissingEpisodesCoordinator(
                    scopeFactory, statusBar, new FakeClock(), new FakeLoggerFactory()),
                SeriesId = series.Id,
                StatusBar = statusBar,
            };
        }

        private static string CreateSeriesFolder(params string[] episodeFolderNames)
        {
            string root = Path.Combine(
                Path.GetTempPath(), $"echoplay-missing-fail-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(root);

            foreach (string name in episodeFolderNames)
            {
                string episodeFolder = Path.Combine(root, name);
                _ = Directory.CreateDirectory(episodeFolder);
                File.WriteAllBytes(Path.Combine(episodeFolder, "track01.mp3"), []);
            }

            return root;
        }

        /// <summary>Anbieter, der jede Anfrage mit einem Netzfehler beantwortet.</summary>
        private sealed class ThrowingEpisodeChecker : IOnlineEpisodeChecker
        {
            public Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckAllAsync(
                IReadOnlyList<CheckableSeriesInfo> subscribedSeries, CancellationToken cancellationToken = default)
                => Task.FromException<IReadOnlyList<OnlineEpisodeCheckResult>>(
                    new System.Net.Http.HttpRequestException("Anbieter nicht erreichbar"));

            public Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckNewReleasesAsync(
                IReadOnlyList<CheckableSeriesInfo> subscribedSeries, DateTime cutoffDate, CancellationToken cancellationToken = default)
                => Task.FromException<IReadOnlyList<OnlineEpisodeCheckResult>>(
                    new System.Net.Http.HttpRequestException("Anbieter nicht erreichbar"));
        }
    }
}

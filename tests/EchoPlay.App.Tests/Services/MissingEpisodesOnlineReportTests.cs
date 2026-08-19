using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft den Teil des Berichts über fehlende Folgen, der vom Anbieter kommt: die
    /// Überschrift, die Auflistung der online verfügbaren Folgen und das Verhalten, wenn
    /// der Anbieter nichts meldet.
    /// </summary>
    /// <remarks>
    /// Die Analyse liest echte Ordner. Sie liegen im Temp-Verzeichnis, tragen einen
    /// eindeutigen Namen und werden am Ende jedes Tests wieder entfernt — genauso wie in
    /// <see cref="MissingEpisodesCoordinatorTests"/>.
    /// </remarks>
    public sealed class MissingEpisodesOnlineReportTests
    {
        [Fact]
        public async Task CheckSingleSeriesAsync_WithMissingOnlineEpisodes_AppendsThemBelowTheLocalPart()
        {
            string folder = CreateSeriesFolder("001 - Eins", "002 - Zwei");
            try
            {
                Fixture fixture = await BuildFixtureAsync(
                    folder,
                    missing: [new MissingOnlineEpisode { EpisodeNumber = 3, AlbumTitle = "Der dritte Fall" }],
                    localHighest: 2);

                IReadOnlyList<string> result = await fixture.Coordinator.CheckSingleSeriesAsync(
                    fixture.SeriesId, folder, MissingEpisodesMode.WithOnline,
                    cancellationToken: TestContext.Current.CancellationToken);

                // Ohne diesen Anhang sähe der Anwender nur seine eigenen Lücken und nie,
                // dass beim Anbieter längst eine neue Folge liegt.
                Assert.Contains(result, line => line.Contains("Online verfügbar", StringComparison.Ordinal));
                Assert.Contains(result, line => line.Contains("Der dritte Fall", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_WithMissingOnlineEpisodes_WritesTheNumberWithThreeDigits()
        {
            string folder = CreateSeriesFolder("001 - Eins");
            try
            {
                Fixture fixture = await BuildFixtureAsync(
                    folder,
                    missing: [new MissingOnlineEpisode { EpisodeNumber = 7, AlbumTitle = "Die siebte Folge" }],
                    localHighest: 1);

                IReadOnlyList<string> result = await fixture.Coordinator.CheckSingleSeriesAsync(
                    fixture.SeriesId, folder, MissingEpisodesMode.WithOnline,
                    cancellationToken: TestContext.Current.CancellationToken);

                // Dreistellig, damit die Liste untereinander bündig steht.
                Assert.Contains(result, line => line.Contains("007", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_WhenProviderReportsNothingMissing_KeepsTheLocalPartAlone()
        {
            string folder = CreateSeriesFolder("001 - Eins", "002 - Zwei");
            try
            {
                Fixture fixture = await BuildFixtureAsync(folder, missing: [], localHighest: 2);

                IReadOnlyList<string> result = await fixture.Coordinator.CheckSingleSeriesAsync(
                    fixture.SeriesId, folder, MissingEpisodesMode.WithOnline,
                    cancellationToken: TestContext.Current.CancellationToken);

                Assert.DoesNotContain(result, line => line.Contains("Online verfügbar", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_WhenProviderAnswersWithNoResult_KeepsTheLocalPartAlone()
        {
            string folder = CreateSeriesFolder("001 - Eins");
            try
            {
                Fixture fixture = await BuildFixtureAsync(folder, missing: null, localHighest: 1);

                IReadOnlyList<string> result = await fixture.Coordinator.CheckSingleSeriesAsync(
                    fixture.SeriesId, folder, MissingEpisodesMode.WithOnline,
                    cancellationToken: TestContext.Current.CancellationToken);

                Assert.DoesNotContain(result, line => line.Contains("Online verfügbar", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_WithOnline_ReturnsTheStatusBarToOfflineAfterwards()
        {
            string folder = CreateSeriesFolder("001 - Eins");
            try
            {
                Fixture fixture = await BuildFixtureAsync(
                    folder,
                    missing: [new MissingOnlineEpisode { EpisodeNumber = 2, AlbumTitle = "Zweiter Fall" }],
                    localHighest: 1);

                _ = await fixture.Coordinator.CheckSingleSeriesAsync(
                    fixture.SeriesId, folder, MissingEpisodesMode.WithOnline,
                    cancellationToken: TestContext.Current.CancellationToken);

                // Bleibt die Anzeige auf „vorübergehend online" stehen, glaubt der Anwender,
                // die Anwendung telefoniere weiter nach draußen.
                Assert.False(fixture.StatusBar.IsTemporarilyOnline);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_WithFoldersWithoutNumbering_ReportsHowManyWereFound()
        {
            string folder = CreateSeriesFolder("Anfang", "Ende");
            try
            {
                Fixture fixture = await BuildFixtureAsync(folder, missing: [], localHighest: 0);

                IReadOnlyList<string> result = await fixture.Coordinator.CheckSingleSeriesAsync(
                    fixture.SeriesId, folder, MissingEpisodesMode.OfflineOnly,
                    cancellationToken: TestContext.Current.CancellationToken);

                // Ohne Nummern gibt es keine Lücken zu melden — wohl aber die Zahl der
                // gefundenen Ordner, damit der Anwender die Benennung nachbessern kann.
                string only = Assert.Single(result);
                Assert.Contains("2", only, StringComparison.Ordinal);
                Assert.Contains("Nummerierung", only, StringComparison.Ordinal);
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

        private static async Task<Fixture> BuildFixtureAsync(
            string seriesFolderPath,
            IReadOnlyList<MissingOnlineEpisode>? missing,
            int localHighest)
        {
            FakeSeriesDataService seriesService = new();
            Series series = new()
            {
                Title = "Die drei Fragezeichen",
                LocalFolderPath = seriesFolderPath,
                AppleMusicArtistId = "am_ddf",
                IsSubscribed = true,
            };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            IReadOnlyList<OnlineEpisodeCheckResult> results = missing is null
                ? []
                :
                [
                    new OnlineEpisodeCheckResult
                    {
                        SeriesId = series.Id,
                        SeriesTitle = series.Title,
                        LocalHighestNumber = localHighest,
                        MissingOnlineEpisodes = missing,
                    }
                ];

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<EchoPlay.Core.Abstractions.IOnlineEpisodeChecker>(
                _ => new FakeOnlineEpisodeChecker(results));
            ServiceProvider provider = services.BuildServiceProvider();

            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            StatusBarViewModel statusBar = new(
                scopeFactory,
                new FakeThemeService(),
                new TaskbarProgressService(),
                new FakeClock());

            return new Fixture
            {
                Coordinator = new MissingEpisodesCoordinator(
                    scopeFactory, statusBar, new FakeClock(), new FakeLoggerFactory()),
                SeriesId = series.Id,
                StatusBar = statusBar,
            };
        }

        /// <summary>
        /// Legt einen Serienordner mit den genannten Folgenordnern an. Jeder bekommt eine
        /// leere MP3-Datei, denn nur Ordner mit Audiodatei gelten als echte Folge.
        /// </summary>
        private static string CreateSeriesFolder(params string[] episodeFolderNames)
        {
            string root = Path.Combine(
                Path.GetTempPath(), $"echoplay-missing-online-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(root);

            foreach (string name in episodeFolderNames)
            {
                string episodeFolder = Path.Combine(root, name);
                _ = Directory.CreateDirectory(episodeFolder);
                File.WriteAllBytes(Path.Combine(episodeFolder, "track01.mp3"), []);
            }

            return root;
        }
    }
}

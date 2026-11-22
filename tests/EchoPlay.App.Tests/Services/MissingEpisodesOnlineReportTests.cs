using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft den Teil des Berichts über fehlende Folgen, der vom Anbieter kommt: die
    /// Überschrift, die Auflistung der online verfügbaren Folgen und das Verhalten, wenn
    /// der Anbieter nichts meldet.
    /// </summary>
    /// <remarks>
    /// Die Analyse liest echte Ordner. Sie liegen im Temp-Verzeichnis, tragen einen
    /// eindeutigen Namen und werden am Ende jedes Tests wieder entfernt, genauso wie in
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

                // Ohne Nummern gibt es keine Lücken zu melden, wohl aber die Zahl der
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

        [Fact]
        public async Task CheckSingleSeriesAsync_OfflineOnly_ListsNewEpisodesTheProviderAlreadyDelivered()
        {
            string folder = CreateSeriesFolder("001 - Eins", "002 - Zwei");
            try
            {
                Fixture fixture = await BuildFixtureAsync(
                    folder, missing: [], localHighest: 2,
                    knownProviderEpisodes: [(2, "Folge 2: Zwei"), (3, "Folge 3: Der dritte Fall")]);

                IReadOnlyList<string> result = await fixture.Coordinator.CheckSingleSeriesAsync(
                    fixture.SeriesId, folder, MissingEpisodesMode.OfflineOnly,
                    cancellationToken: TestContext.Current.CancellationToken);

                // Ohne Netz, aus der Datenbank: Die Folge nach der letzten lokalen ist bekannt.
                Assert.Contains(result, line => line.Contains("Der dritte Fall", StringComparison.Ordinal));
                Assert.DoesNotContain(result, line => line.Contains("Folge 2: Zwei", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckAllSeriesAsync_OfflineOnly_ReportsNewEpisodesFromTheProviderTwin()
        {
            string folder = CreateSeriesFolder("001 - Eins", "002 - Zwei");
            try
            {
                Fixture fixture = await BuildFixtureAsync(
                    folder, missing: [], localHighest: 2,
                    knownProviderEpisodes: [(3, "Folge 3: Drei"), (4, "Folge 4: Vier")]);

                MissingEpisodesReport report = await fixture.Coordinator.CheckAllSeriesAsync(
                    MissingEpisodesMode.OfflineOnly, TestContext.Current.CancellationToken);

                SeriesMissingEpisodesResult result = Assert.Single(report.Results);
                Assert.Equal([3, 4], result.OnlineEpisodes.Select(episode => episode.EpisodeNumber));
                Assert.Equal(4, result.OnlineHighestNumber);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckAllSeriesAsync_WithOnline_MergesLiveAndKnownEpisodes_KnownTitleWins()
        {
            string folder = CreateSeriesFolder("001 - Eins");
            try
            {
                Fixture fixture = await BuildFixtureAsync(
                    folder,
                    missing:
                    [
                        new MissingOnlineEpisode { EpisodeNumber = 2, AlbumTitle = "Die drei ??? - Folge 2 - Zwei" },
                        new MissingOnlineEpisode { EpisodeNumber = 3, AlbumTitle = "Die drei ??? - Folge 3 - Drei" }
                    ],
                    localHighest: 1,
                    knownProviderEpisodes: [(2, "Folge 2: Zwei")]);

                MissingEpisodesReport report = await fixture.Coordinator.CheckAllSeriesAsync(
                    MissingEpisodesMode.WithOnline, TestContext.Current.CancellationToken);

                SeriesMissingEpisodesResult result = Assert.Single(report.Results);
                Assert.Equal([2, 3], result.OnlineEpisodes.Select(episode => episode.EpisodeNumber));
                Assert.Equal("Folge 2: Zwei", result.OnlineEpisodes[0].Title);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        /// <summary>
        /// Koordinator, Serie und Statusleiste eines Testfalls.
        /// </summary>
        private sealed class Fixture
        {
            public required MissingEpisodesCoordinator Coordinator { get; init; }
            public required Guid SeriesId { get; init; }
            public required StatusBarViewModel StatusBar { get; init; }
        }

        /// <summary>Prüfergebnis der Serie; ohne fehlende Folgen meldet die Prüfung nichts.</summary>
        private static IReadOnlyList<OnlineEpisodeCheckResult> CheckResults(
            Series series, IReadOnlyList<MissingOnlineEpisode>? missing, int localHighest)
            => missing is null
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

        /// <summary>
        /// Registriert Daten-Nachbauten, die echte Suche nach bekannten neuen Folgen und die
        /// Online-Prüfung mit den vorgegebenen Ergebnissen.
        /// </summary>
        private static IServiceScopeFactory CreateScopeFactory(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            IReadOnlyList<OnlineEpisodeCheckResult> results)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<KnownNewEpisodeLookup>();
            _ = services.AddScoped<EchoPlay.Core.Abstractions.IOnlineEpisodeChecker>(
                _ => new FakeOnlineEpisodeChecker(results));
            return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        }

        private static async Task<Fixture> BuildFixtureAsync(
            string seriesFolderPath,
            IReadOnlyList<MissingOnlineEpisode>? missing,
            int localHighest,
            IReadOnlyList<(int Number, string Title)>? knownProviderEpisodes = null)
        {
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            Series series = new()
            {
                Title = "Die drei Fragezeichen",
                LocalFolderPath = seriesFolderPath,
                AppleMusicArtistId = "am_ddf",
                IsSubscribed = true,
            };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            if (knownProviderEpisodes is not null)
            {
                await AddProviderTwinAsync(seriesService, episodeService, knownProviderEpisodes);
            }

            IServiceScopeFactory scopeFactory = CreateScopeFactory(
                seriesService, episodeService, CheckResults(series, missing, localHighest));
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

        /// <summary>
        /// Legt das Gegenstück beim Anbieter an: eigener Titel wie bei den „!!!", dieselbe
        /// Künstlerkennung, die Folgen ohne lokale Dateien.
        /// </summary>
        private static async Task AddProviderTwinAsync(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            IReadOnlyList<(int Number, string Title)> episodes)
        {
            Series twin = new()
            {
                Title = "Die drei ???",
                AppleMusicArtistId = "am_ddf",
                IsOnlineImported = true,
            };
            await seriesService.AddAsync(twin, TestContext.Current.CancellationToken);

            foreach ((int number, string title) in episodes)
            {
                await episodeService.AddAsync(
                    new Episode { SeriesId = twin.Id, EpisodeNumber = number, Title = title },
                    TestContext.Current.CancellationToken);
            }
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

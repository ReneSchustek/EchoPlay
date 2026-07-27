using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
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
    /// Tests für <see cref="MissingEpisodesCoordinator"/>. Deckt die Cancel-Pfade, die
    /// Pfad-Guards, die Lückenanalyse auf echten Ordnerstrukturen im Temp-Verzeichnis und
    /// den Online-Abgleich über den Fake-Checker ab.
    /// </summary>
    public sealed class MissingEpisodesCoordinatorTests
    {
        private static MissingEpisodesCoordinator BuildCoordinator(
            FakeSeriesDataService? seriesService = null,
            FakeOnlineEpisodeChecker? checker = null)
        {
            FakeSeriesDataService series = seriesService ?? new FakeSeriesDataService();

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => series);
            _ = services.AddScoped<EchoPlay.Core.Abstractions.IOnlineEpisodeChecker>(
                _ => checker ?? new FakeOnlineEpisodeChecker());
            ServiceProvider provider = services.BuildServiceProvider();

            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            // StatusBarViewModel: braucht ScopeFactory + Theme + TaskbarProgress.
            // Die COM-basierte Taskleisten-Integration läuft im Test ins Leere (kein HWND vorhanden).
            StatusBarViewModel statusBar = new(
                scopeFactory,
                new FakeThemeService(),
                new TaskbarProgressService(),
                new FakeClock());

            return new MissingEpisodesCoordinator(
                scopeFactory,
                statusBar,
                new FakeClock(),
                new FakeLoggerFactory());
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_ReturnsEmpty_WhenModeIsCancel()
        {
            MissingEpisodesCoordinator coordinator = BuildCoordinator();

            IReadOnlyList<string> result = await coordinator.CheckSingleSeriesAsync(
                TestIds.SeriesA,
                Path.GetTempPath(),
                MissingEpisodesMode.Cancel, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_ReportsMissingFolder_WhenPathIsNull()
        {
            MissingEpisodesCoordinator coordinator = BuildCoordinator();

            IReadOnlyList<string> result = await coordinator.CheckSingleSeriesAsync(
                TestIds.SeriesB,
                seriesFolderPath: null,
                MissingEpisodesMode.OfflineOnly, cancellationToken: TestContext.Current.CancellationToken);

            _ = Assert.Single(result);
            Assert.Contains("Kein lokaler Ordner", result[0], StringComparison.Ordinal);
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_ReportsMissingFolder_WhenPathDoesNotExist()
        {
            MissingEpisodesCoordinator coordinator = BuildCoordinator();
            string nonExistentPath = Path.Combine(
                Path.GetTempPath(),
                $"echoplay-missing-episodes-{TestIds.SeriesC:N}");

            IReadOnlyList<string> result = await coordinator.CheckSingleSeriesAsync(
                TestIds.SeriesC,
                nonExistentPath,
                MissingEpisodesMode.OfflineOnly, cancellationToken: TestContext.Current.CancellationToken);

            _ = Assert.Single(result);
            Assert.Contains("Kein lokaler Ordner", result[0], StringComparison.Ordinal);
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_ReportsNoEpisodeFolders_WhenFolderIsEmpty()
        {
            MissingEpisodesCoordinator coordinator = BuildCoordinator();
            string tempFolder = CreateTempFolder();
            try
            {
                IReadOnlyList<string> result = await coordinator.CheckSingleSeriesAsync(
                    TestIds.SeriesD,
                    tempFolder,
                    MissingEpisodesMode.OfflineOnly, cancellationToken: TestContext.Current.CancellationToken);

                _ = Assert.Single(result);
                Assert.Contains("Keine Folgenordner", result[0], StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(tempFolder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckAllSeriesAsync_ReturnsEmptyReport_WhenModeIsCancel()
        {
            MissingEpisodesCoordinator coordinator = BuildCoordinator();

            MissingEpisodesReport report = await coordinator.CheckAllSeriesAsync(MissingEpisodesMode.Cancel, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(report.Results);
            Assert.Equal(0, report.TotalLocalGaps);
            Assert.Equal(0, report.TotalOnlineNew);
        }

        [Fact]
        public async Task CheckAllSeriesAsync_ReturnsEmptyReport_WhenNoSubscribedSeries()
        {
            MissingEpisodesCoordinator coordinator = BuildCoordinator();

            MissingEpisodesReport report = await coordinator.CheckAllSeriesAsync(MissingEpisodesMode.OfflineOnly, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(report.Results);
            Assert.NotEqual(default, report.CheckedAtUtc);
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_WithGapInNumbering_NamesMissingEpisode()
        {
            // Ordner 1, 2 und 4 vorhanden → Folge 3 fehlt.
            MissingEpisodesCoordinator coordinator = BuildCoordinator();
            string folder = CreateSeriesFolder("001 - Der Anfang", "002 - Die Fortsetzung", "004 - Das Ende");

            try
            {
                IReadOnlyList<string> result = await coordinator.CheckSingleSeriesAsync(
                    TestIds.SeriesA,
                    folder,
                    MissingEpisodesMode.OfflineOnly,
                    cancellationToken: TestContext.Current.CancellationToken);

                Assert.Contains(result, line => line.Contains('3', StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_WithoutGaps_ReportsComplete()
        {
            MissingEpisodesCoordinator coordinator = BuildCoordinator();
            string folder = CreateSeriesFolder("001 - Eins", "002 - Zwei", "003 - Drei");

            try
            {
                IReadOnlyList<string> result = await coordinator.CheckSingleSeriesAsync(
                    TestIds.SeriesB,
                    folder,
                    MissingEpisodesMode.OfflineOnly,
                    cancellationToken: TestContext.Current.CancellationToken);

                Assert.DoesNotContain(result, line => line.Contains("fehlen", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_SammlungBeginntSpaet_MeldetKeineLuecken()
        {
            // Kernpunkt der Lückensuche: Wer erst ab Folge 50 sammelt, hat keine 49 Lücken.
            MissingEpisodesCoordinator coordinator = BuildCoordinator();
            string folder = CreateSeriesFolder("050 - Fünfzig", "051 - Einundfünfzig", "052 - Zweiundfünfzig");

            try
            {
                IReadOnlyList<string> result = await coordinator.CheckSingleSeriesAsync(
                    TestIds.SeriesC,
                    folder,
                    MissingEpisodesMode.OfflineOnly,
                    cancellationToken: TestContext.Current.CancellationToken);

                Assert.DoesNotContain(result, line => line.Contains("49", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_WithOnline_QueriesTheChecker()
        {
            FakeOnlineEpisodeChecker checker = new();
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Mit Online-Abgleich", IsSubscribed = true };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            MissingEpisodesCoordinator coordinator = BuildCoordinator(seriesService, checker);
            string folder = CreateSeriesFolder("001 - Eins");

            try
            {
                _ = await coordinator.CheckSingleSeriesAsync(
                    series.Id,
                    folder,
                    MissingEpisodesMode.WithOnline,
                    cancellationToken: TestContext.Current.CancellationToken);

                Assert.Equal(1, checker.CheckCallCount);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_UnknownSeriesId_SkipsOnlineStep()
        {
            // Ohne passenden Datensatz gibt es nichts abzugleichen – der Checker
            // darf dann gar nicht erst gerufen werden.
            FakeOnlineEpisodeChecker checker = new();
            MissingEpisodesCoordinator coordinator = BuildCoordinator(checker: checker);
            string folder = CreateSeriesFolder("001 - Eins");

            try
            {
                _ = await coordinator.CheckSingleSeriesAsync(
                    TestIds.SeriesE,
                    folder,
                    MissingEpisodesMode.WithOnline,
                    cancellationToken: TestContext.Current.CancellationToken);

                Assert.Equal(0, checker.CheckCallCount);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckAllSeriesAsync_SkipsSeriesWithoutLocalFolder()
        {
            // Ohne lokalen Ordner gibt es nichts zu vergleichen – solche Serien
            // gehören nicht in den Bericht, sonst steht dort eine Zeile ohne Aussage.
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "Ohne Ordner", IsSubscribed = true, LocalFolderPath = null },
                TestContext.Current.CancellationToken);

            MissingEpisodesCoordinator coordinator = BuildCoordinator(seriesService);

            MissingEpisodesReport report = await coordinator.CheckAllSeriesAsync(
                MissingEpisodesMode.OfflineOnly,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(report.Results);
        }

        [Fact]
        public async Task CheckAllSeriesAsync_WithLocalFolder_ReportsSeriesAndGaps()
        {
            string folder = CreateSeriesFolder("001 - Eins", "003 - Drei");

            try
            {
                FakeSeriesDataService seriesService = new();
                await seriesService.AddAsync(
                    new Series { Title = "Mit Lücke", IsSubscribed = true, LocalFolderPath = folder },
                    TestContext.Current.CancellationToken);

                MissingEpisodesCoordinator coordinator = BuildCoordinator(seriesService);

                MissingEpisodesReport report = await coordinator.CheckAllSeriesAsync(
                    MissingEpisodesMode.OfflineOnly,
                    cancellationToken: TestContext.Current.CancellationToken);

                SeriesMissingEpisodesResult result = Assert.Single(report.Results);
                Assert.Equal("Mit Lücke", result.SeriesTitle);
                Assert.Equal(3, result.LocalHighestNumber);
                Assert.Contains(2, result.LocalGaps);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        private static string CreateTempFolder()
        {
            string path = Path.Combine(Path.GetTempPath(), $"echoplay-missing-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Legt einen Serienordner mit den genannten Folgenordnern an. Jeder bekommt eine
        /// leere MP3-Datei, denn nur Ordner mit Audiodatei gelten als echte Folge —
        /// ohne die zählt die Analyse den Ordner nicht mit.
        /// Zufälliger Name, weil xUnit die Testklassen parallel ausführt.
        /// </summary>
        private static string CreateSeriesFolder(params string[] episodeFolderNames)
        {
            string root = CreateTempFolder();

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

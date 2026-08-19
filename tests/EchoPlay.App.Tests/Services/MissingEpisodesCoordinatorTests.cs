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
            FakeOnlineEpisodeChecker? checker = null,
            Action<StatusBarViewModel>? captureStatusBar = null)
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

            // Die Statusleiste gehört zum geprüften Verhalten: Wer sie sehen will, bekommt
            // sie hier durchgereicht, ohne dass die anderen Aufrufe etwas ändern müssen.
            captureStatusBar?.Invoke(statusBar);

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

        [Fact]
        public async Task CheckAllSeriesAsync_WithOnline_PutsProviderEpisodesIntoTheReport()
        {
            string folder = CreateSeriesFolder("001 - Eins", "002 - Zwei");

            try
            {
                FakeSeriesDataService seriesService = new();
                Series series = new()
                {
                    Title = "Mit Online-Abgleich",
                    IsSubscribed = true,
                    LocalFolderPath = folder,
                    AppleMusicArtistId = "201306317",
                };
                await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

                FakeOnlineEpisodeChecker checker = new(
                [
                    new OnlineEpisodeCheckResult
                    {
                        SeriesId = series.Id,
                        SeriesTitle = series.Title,
                        OnlineHighestNumber = 4,
                        LocalHighestNumber = 2,
                        NewEpisodesCount = 2,
                        MissingOnlineEpisodes =
                        [
                            new MissingOnlineEpisode { EpisodeNumber = 3, AlbumTitle = "Folge 3 - Der dritte Fall" },
                            new MissingOnlineEpisode { EpisodeNumber = 4, AlbumTitle = "Folge 4 - Der vierte Fall" },
                        ],
                    },
                ]);

                MissingEpisodesCoordinator coordinator = BuildCoordinator(seriesService, checker);

                MissingEpisodesReport report = await coordinator.CheckAllSeriesAsync(
                    MissingEpisodesMode.WithOnline, cancellationToken: TestContext.Current.CancellationToken);

                // Der eigentliche Zweck des Online-Abgleichs: sichtbar machen, welche Folgen es
                // beim Anbieter gibt und in der Sammlung fehlen. Bleibt die Liste leer, ist der
                // Weg über das Netz umsonst gegangen.
                SeriesMissingEpisodesResult result = Assert.Single(report.Results);
                Assert.Equal(4, result.OnlineHighestNumber);
                Assert.Equal(2, result.OnlineEpisodes.Count);
                Assert.Equal(1, checker.CheckCallCount);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckAllSeriesAsync_OfflineOnly_NeverAsksTheProvider()
        {
            string folder = CreateSeriesFolder("001 - Eins");

            try
            {
                FakeSeriesDataService seriesService = new();
                await seriesService.AddAsync(
                    new Series { Title = "Nur lokal geprüft", IsSubscribed = true, LocalFolderPath = folder },
                    TestContext.Current.CancellationToken);

                FakeOnlineEpisodeChecker checker = new();
                MissingEpisodesCoordinator coordinator = BuildCoordinator(seriesService, checker);

                _ = await coordinator.CheckAllSeriesAsync(
                    MissingEpisodesMode.OfflineOnly, cancellationToken: TestContext.Current.CancellationToken);

                // Wer im Dialog „nur offline" wählt, hat dem Netzzugriff ausdrücklich
                // widersprochen. Eine Abfrage trotzdem abzusetzen, wäre ein Vertrauensbruch.
                Assert.Equal(0, checker.CheckCallCount);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckAllSeriesAsync_WithOnline_ReturnsTheStatusBarToOfflineAfterwards()
        {
            StatusBarViewModel? statusBar = null;
            MissingEpisodesCoordinator coordinator = BuildCoordinator(
                captureStatusBar: bar => statusBar = bar);

            _ = await coordinator.CheckAllSeriesAsync(
                MissingEpisodesMode.WithOnline, TestContext.Current.CancellationToken);

            // Für die Prüfung geht die Anwendung vorübergehend online — der Anwender hat dem
            // im Dialog zugestimmt. Bliebe der Stand danach stehen, zeigte die Statusleiste
            // dauerhaft „online", obwohl niemand mehr zugestimmt hat.
            Assert.NotNull(statusBar);
            Assert.False(statusBar.IsTemporarilyOnline);
        }

        [Fact]
        public async Task CheckAllSeriesAsync_WithoutOnline_LeavesTheStatusBarUntouched()
        {
            StatusBarViewModel? statusBar = null;
            MissingEpisodesCoordinator coordinator = BuildCoordinator(
                captureStatusBar: bar => statusBar = bar);

            _ = await coordinator.CheckAllSeriesAsync(
                MissingEpisodesMode.OfflineOnly, TestContext.Current.CancellationToken);

            Assert.NotNull(statusBar);
            Assert.False(statusBar.IsTemporarilyOnline);
        }

        [Fact]
        public async Task CheckAllSeriesAsync_ClearsTheProgressTextWhenDone()
        {
            StatusBarViewModel? statusBar = null;
            MissingEpisodesCoordinator coordinator = BuildCoordinator(
                captureStatusBar: bar => statusBar = bar);

            _ = await coordinator.CheckAllSeriesAsync(
                MissingEpisodesMode.OfflineOnly, TestContext.Current.CancellationToken);

            // Bleibt „Prüfe Serie 3/12 …" stehen, hält der Anwender einen längst beendeten
            // Vorgang für laufend und wartet auf etwas, das nicht mehr kommt.
            Assert.NotNull(statusBar);
            Assert.True(string.IsNullOrEmpty(statusBar.ScanProgress.Text));
        }
    }
}

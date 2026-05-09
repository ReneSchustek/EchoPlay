using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Models;
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
    /// Tests für <see cref="MissingEpisodesCoordinator"/>. Deckt die Cancel-Pfade,
    /// die Pfad-Guards der Einzelserien-Prüfung und die leere Gesamtprüfung ab.
    /// Die Online- und Dateisystem-Analyse wird nicht getestet, weil sie reale
    /// iTunes-Aufrufe und tief verschachtelte Verzeichnisstrukturen voraussetzen würde.
    /// </summary>
    public sealed class MissingEpisodesCoordinatorTests
    {
        private static MissingEpisodesCoordinator BuildCoordinator(FakeSeriesDataService? seriesService = null)
        {
            FakeSeriesDataService series = seriesService ?? new FakeSeriesDataService();

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => series);
            _ = services.AddScoped<EchoPlay.Core.Abstractions.IOnlineEpisodeChecker>(
                _ => new FakeOnlineEpisodeChecker());
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

        private static string CreateTempFolder()
        {
            string path = Path.Combine(Path.GetTempPath(), $"echoplay-missing-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            return path;
        }
    }
}

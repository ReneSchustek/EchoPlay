using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
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
            services.AddScoped<ISeriesDataService>(_ => series);
            ServiceProvider provider = services.BuildServiceProvider();

            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            // StatusBarViewModel: braucht ScopeFactory + Theme + TaskbarProgress.
            // Die COM-basierte Taskleisten-Integration läuft im Test ins Leere (kein HWND vorhanden).
            StatusBarViewModel statusBar = new(
                scopeFactory,
                new FakeThemeService(),
                new TaskbarProgressService());

            return new MissingEpisodesCoordinator(
                scopeFactory,
                new FakeOnlineEpisodeChecker(),
                statusBar);
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_ReturnsEmpty_WhenModeIsCancel()
        {
            MissingEpisodesCoordinator coordinator = BuildCoordinator();

            IReadOnlyList<string> result = await coordinator.CheckSingleSeriesAsync(
                Guid.NewGuid(),
                Path.GetTempPath(),
                MissingEpisodesMode.Cancel);

            Assert.Empty(result);
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_ReportsMissingFolder_WhenPathIsNull()
        {
            MissingEpisodesCoordinator coordinator = BuildCoordinator();

            IReadOnlyList<string> result = await coordinator.CheckSingleSeriesAsync(
                Guid.NewGuid(),
                seriesFolderPath: null,
                MissingEpisodesMode.OfflineOnly);

            Assert.Single(result);
            Assert.Contains("Kein lokaler Ordner", result[0]);
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_ReportsMissingFolder_WhenPathDoesNotExist()
        {
            MissingEpisodesCoordinator coordinator = BuildCoordinator();
            string nonExistentPath = Path.Combine(
                Path.GetTempPath(),
                $"echoplay-missing-episodes-{Guid.NewGuid():N}");

            IReadOnlyList<string> result = await coordinator.CheckSingleSeriesAsync(
                Guid.NewGuid(),
                nonExistentPath,
                MissingEpisodesMode.OfflineOnly);

            Assert.Single(result);
            Assert.Contains("Kein lokaler Ordner", result[0]);
        }

        [Fact]
        public async Task CheckSingleSeriesAsync_ReportsNoEpisodeFolders_WhenFolderIsEmpty()
        {
            MissingEpisodesCoordinator coordinator = BuildCoordinator();
            string tempFolder = CreateTempFolder();
            try
            {
                IReadOnlyList<string> result = await coordinator.CheckSingleSeriesAsync(
                    Guid.NewGuid(),
                    tempFolder,
                    MissingEpisodesMode.OfflineOnly);

                Assert.Single(result);
                Assert.Contains("Keine Folgenordner", result[0]);
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

            MissingEpisodesReport report = await coordinator.CheckAllSeriesAsync(MissingEpisodesMode.Cancel);

            Assert.Empty(report.Results);
            Assert.Equal(0, report.TotalLocalGaps);
            Assert.Equal(0, report.TotalOnlineNew);
        }

        [Fact]
        public async Task CheckAllSeriesAsync_ReturnsEmptyReport_WhenNoSubscribedSeries()
        {
            MissingEpisodesCoordinator coordinator = BuildCoordinator();

            MissingEpisodesReport report = await coordinator.CheckAllSeriesAsync(MissingEpisodesMode.OfflineOnly);

            Assert.Empty(report.Results);
            Assert.NotEqual(default, report.CheckedAtUtc);
        }

        private static string CreateTempFolder()
        {
            string path = Path.Combine(Path.GetTempPath(), $"echoplay-missing-{Path.GetRandomFileName()}");
            Directory.CreateDirectory(path);
            return path;
        }
    }
}

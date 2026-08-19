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
    /// Prüft, wie der Bericht über fehlende Folgen mit Ordnern umgeht, die zwar
    /// Unterordner haben, aber keine Audiodateien.
    /// </summary>
    /// <remarks>
    /// Ein Serienordner enthält oft Beiwerk — Bilder, Textdateien, ein Verzeichnis für
    /// Booklets. Zählte die Analyse solche Ordner als Folgen mit, meldete sie Lücken, die
    /// es nicht gibt, und der Anwender suchte nach Folgen, die er längst hat.
    ///
    /// Die Tests legen echte Ordner im Temp-Verzeichnis an und räumen sie wieder ab.
    /// </remarks>
    public sealed class MissingEpisodesFolderShapeTests
    {
        [Fact]
        public async Task CheckSingleSeries_WithSubfoldersButNoAudio_ReportsNoEpisodeFolders()
        {
            string folder = CreateFolderWithEmptySubfolders("Booklet", "Bilder");
            try
            {
                Fixture fixture = await BuildAsync(folder);

                IReadOnlyList<string> result = await fixture.Coordinator.CheckSingleSeriesAsync(
                    fixture.SeriesId, folder, MissingEpisodesMode.OfflineOnly,
                    cancellationToken: TestContext.Current.CancellationToken);

                string only = Assert.Single(result);
                Assert.Contains("Folgenordner", only, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckAllSeries_WithSubfoldersButNoAudio_ReportsNoGaps()
        {
            string folder = CreateFolderWithEmptySubfolders("Booklet");
            try
            {
                Fixture fixture = await BuildAsync(folder);

                MissingEpisodesReport report =
                    await fixture.Coordinator.CheckAllSeriesAsync(
                        MissingEpisodesMode.OfflineOnly,
                        cancellationToken: TestContext.Current.CancellationToken);

                SeriesMissingEpisodesResult entry = Assert.Single(report.Results);
                Assert.Empty(entry.LocalGaps);
                Assert.Equal(0, entry.LocalHighestNumber);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckAllSeries_WithNumberedEpisodeFolders_ReportsTheHighestNumber()
        {
            string folder = CreateFolderWithEpisodes("001 - Eins", "003 - Drei");
            try
            {
                Fixture fixture = await BuildAsync(folder);

                MissingEpisodesReport report =
                    await fixture.Coordinator.CheckAllSeriesAsync(
                        MissingEpisodesMode.OfflineOnly,
                        cancellationToken: TestContext.Current.CancellationToken);

                SeriesMissingEpisodesResult entry = Assert.Single(report.Results);
                Assert.Equal(3, entry.LocalHighestNumber);
                Assert.Equal([2], entry.LocalGaps);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task CheckAllSeries_WithAFolderThatIsGone_ReportsTheSeriesWithoutGaps()
        {
            string missing = Path.Combine(Path.GetTempPath(), "echoplay-ordner-gibt-es-nicht");
            Fixture fixture = await BuildAsync(missing);

            MissingEpisodesReport report = await fixture.Coordinator.CheckAllSeriesAsync(
                MissingEpisodesMode.OfflineOnly,
                cancellationToken: TestContext.Current.CancellationToken);

            // Ein abgezogenes Laufwerk oder ein umbenannter Ordner darf den ganzen Bericht
            // nicht kippen — die Serie erscheint, nur eben ohne Angaben.
            SeriesMissingEpisodesResult entry = Assert.Single(report.Results);
            Assert.Empty(entry.LocalGaps);
            Assert.Equal(0, entry.LocalHighestNumber);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private sealed class Fixture
        {
            public required MissingEpisodesCoordinator Coordinator { get; init; }
            public required Guid SeriesId { get; init; }
        }

        private static async Task<Fixture> BuildAsync(string seriesFolderPath)
        {
            FakeSeriesDataService seriesService = new();
            Series series = new()
            {
                Title = "TKKG",
                LocalFolderPath = seriesFolderPath,
                IsSubscribed = true,
            };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<EchoPlay.Core.Abstractions.IOnlineEpisodeChecker>(
                _ => new FakeOnlineEpisodeChecker());
            ServiceProvider provider = services.BuildServiceProvider();

            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            StatusBarViewModel statusBar = new(
                scopeFactory, new FakeThemeService(), new TaskbarProgressService(), new FakeClock());

            return new Fixture
            {
                Coordinator = new MissingEpisodesCoordinator(
                    scopeFactory, statusBar, new FakeClock(), new FakeLoggerFactory()),
                SeriesId = series.Id,
            };
        }

        private static string CreateFolderWithEmptySubfolders(params string[] names)
        {
            string root = CreateRoot();
            foreach (string name in names)
            {
                _ = Directory.CreateDirectory(Path.Combine(root, name));
            }
            return root;
        }

        private static string CreateFolderWithEpisodes(params string[] names)
        {
            string root = CreateRoot();
            foreach (string name in names)
            {
                string folder = Path.Combine(root, name);
                _ = Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "track01.mp3"), []);
            }
            return root;
        }

        private static string CreateRoot()
        {
            string path = Path.Combine(
                Path.GetTempPath(), $"echoplay-missing-shape-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            return path;
        }
    }
}

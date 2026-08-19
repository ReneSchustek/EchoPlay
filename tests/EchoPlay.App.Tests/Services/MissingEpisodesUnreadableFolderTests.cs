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
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft den Bericht über fehlende Folgen, wenn der Serienordner zwar da ist,
    /// sich aber nicht lesen lässt.
    /// </summary>
    /// <remarks>
    /// Das ist der Alltagsfall eines Netzlaufwerks ohne Verbindung oder eines Ordners,
    /// den ein anderes Konto angelegt hat: <see cref="Directory.Exists(string)"/> sagt ja,
    /// das Auflisten scheitert trotzdem. Ein nicht vorhandener Ordner wird vorher
    /// abgefangen und kommt hier gar nicht an — nur der lesbare-aber-nicht-lesbare Fall
    /// erreicht diese Zweige.
    ///
    /// Der Test entzieht sich dafür selbst die Leserechte auf einen eigenen Ordner im
    /// Temp-Verzeichnis und gibt sie im <c>finally</c> wieder frei.
    /// </remarks>
    public sealed class MissingEpisodesUnreadableFolderTests
    {
        [Fact]
        public async Task CheckSingleSeries_WithAFolderThatCannotBeRead_SaysSoInsteadOfFailing()
        {
            string folder = CreateUnreadableFolder();
            try
            {
                Fixture fixture = await BuildAsync(folder);

                IReadOnlyList<string> lines = await fixture.Coordinator.CheckSingleSeriesAsync(
                    fixture.SeriesId, folder, MissingEpisodesMode.OfflineOnly,
                    cancellationToken: TestContext.Current.CancellationToken);

                // Der Anwender bekommt einen Satz, der ihm sagt, woran es liegt — statt
                // einer leeren Liste, die wie „keine Lücken" aussieht.
                string line = Assert.Single(lines);
                Assert.NotEmpty(line);
            }
            finally
            {
                RestoreAndDelete(folder);
            }
        }

        [Fact]
        public async Task CheckAllSeries_WithAFolderThatCannotBeRead_KeepsTheSeriesInTheReport()
        {
            string folder = CreateUnreadableFolder();
            try
            {
                Fixture fixture = await BuildAsync(folder);

                MissingEpisodesReport report = await fixture.Coordinator.CheckAllSeriesAsync(
                    MissingEpisodesMode.OfflineOnly,
                    cancellationToken: TestContext.Current.CancellationToken);

                // Im Gesamtbericht fällt die Serie nicht heraus — sie steht ohne Angaben da.
                SeriesMissingEpisodesResult entry = Assert.Single(report.Results);
                Assert.Empty(entry.LocalGaps);
                Assert.Equal(0, entry.LocalHighestNumber);
            }
            finally
            {
                RestoreAndDelete(folder);
            }
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
            _ = services.AddScoped<IOnlineEpisodeChecker>(_ => new FakeOnlineEpisodeChecker());
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

        /// <summary>
        /// Legt einen Ordner an und entzieht dem laufenden Konto das Auflisten seines
        /// Inhalts. Der Ordner bleibt vorhanden — nur sein Inhalt ist nicht mehr zu sehen.
        /// </summary>
        private static string CreateUnreadableFolder()
        {
            string path = Path.Combine(
                Path.GetTempPath(), $"echoplay-gesperrt-{Path.GetRandomFileName()}");
            DirectoryInfo folder = Directory.CreateDirectory(path);

            DirectorySecurity security = folder.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User!,
                FileSystemRights.ListDirectory | FileSystemRights.ReadData,
                AccessControlType.Deny));
            folder.SetAccessControl(security);

            return path;
        }

        /// <summary>
        /// Nimmt die Sperre zurück und räumt den Ordner weg. Läuft auch dann, wenn der
        /// Test scheitert — ein gesperrter Ordner darf nicht liegen bleiben.
        /// </summary>
        private static void RestoreAndDelete(string path)
        {
            DirectoryInfo folder = new(path);
            if (!folder.Exists)
            {
                return;
            }

            DirectorySecurity security = folder.GetAccessControl();
            security.RemoveAccessRuleAll(new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User!,
                FileSystemRights.ListDirectory | FileSystemRights.ReadData,
                AccessControlType.Deny));
            folder.SetAccessControl(security);

            folder.Delete(recursive: true);
        }
    }
}

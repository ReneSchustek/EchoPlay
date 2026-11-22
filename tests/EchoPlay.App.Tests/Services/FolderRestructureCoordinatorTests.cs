using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="FolderRestructureCoordinator"/>. Prüft den Pfad-Guard
    /// für nicht existierende Ordner, die Weiterleitung des AppSettings-Musters an den
    /// Restrukturierungs-Service und die Übergabe des Original-Previews beim Ausführen.
    /// </summary>
    public sealed class FolderRestructureCoordinatorTests
    {
        private static (FolderRestructureCoordinator coordinator,
                        FakeFolderRestructureService restructure,
                        FakeAppSettingsDataService settings) BuildCoordinator(string folderPattern = "{number:000} - {title}")
        {
            FakeFolderRestructureService restructure = new();
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                EpisodeFolderPattern = folderPattern
            });

            ServiceCollection services = new();
            _ = services.AddScoped<IFolderRestructureService>(_ => restructure);
            _ = services.AddScoped<IAppSettingsDataService>(_ => settings);
            ServiceProvider provider = services.BuildServiceProvider();

            FolderRestructureCoordinator coordinator = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeLoggerFactory());

            return (coordinator, restructure, settings);
        }

        [Fact]
        public async Task AnalyzeAsync_ReturnsNull_WhenPathIsEmpty()
        {
            (FolderRestructureCoordinator coordinator, _, _) = BuildCoordinator();

            RestructurePreviewDisplay? result = await coordinator.AnalyzeAsync(string.Empty, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task AnalyzeAsync_ReturnsNull_WhenFolderDoesNotExist()
        {
            (FolderRestructureCoordinator coordinator, _, _) = BuildCoordinator();

            RestructurePreviewDisplay? result = await coordinator.AnalyzeAsync(
                Path.Combine(Path.GetTempPath(), "echoplay-folder-restructure-tests-doesnotexist"), cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task AnalyzeAsync_ReturnsNull_WhenServicePreviewIsEmpty()
        {
            string tempFolder = CreateTempFolder();
            try
            {
                (FolderRestructureCoordinator coordinator, FakeFolderRestructureService restructure, _) =
                    BuildCoordinator();

                restructure.SetAnalyzeResult(new RestructurePreview
                {
                    SeriesFolderPath = tempFolder,
                    Actions = new List<RestructureAction>()
                });

                RestructurePreviewDisplay? result = await coordinator.AnalyzeAsync(tempFolder, cancellationToken: TestContext.Current.CancellationToken);

                Assert.Null(result);
            }
            finally
            {
                Directory.Delete(tempFolder);
            }
        }

        [Fact]
        public async Task AnalyzeAsync_ReturnsDisplayWrapper_WhenPreviewHasActions()
        {
            string tempFolder = CreateTempFolder();
            try
            {
                (FolderRestructureCoordinator coordinator, FakeFolderRestructureService restructure, _) =
                    BuildCoordinator(folderPattern: "{number:000} - {title}");

                restructure.SetAnalyzeResult(TwoEpisodePreview(tempFolder));

                RestructurePreviewDisplay? result = await coordinator.AnalyzeAsync(tempFolder, cancellationToken: TestContext.Current.CancellationToken);

                Assert.NotNull(result);
                Assert.Equal(2, result!.FileCount);
                Assert.Equal(2, result.FolderCount);
                Assert.Equal(2, result.Actions.Count);
                Assert.Equal("001 - Erste.mp3", result.Actions[0].FileName);
                Assert.Equal("001 - Erste", result.Actions[0].TargetFolderName);
                Assert.Equal(tempFolder, restructure.LastAnalyzedFolderPath);
                Assert.Equal("{number:000} - {title}", restructure.LastFolderPattern);
            }
            finally
            {
                Directory.Delete(tempFolder);
            }
        }

        [Fact]
        public async Task ExecuteAsync_DelegatesToServiceAndReturnsCount()
        {
            string tempFolder = CreateTempFolder();
            try
            {
                (FolderRestructureCoordinator coordinator, FakeFolderRestructureService restructure, _) =
                    BuildCoordinator();

                RestructurePreview original = new()
                {
                    SeriesFolderPath = tempFolder,
                    Actions = [Move(tempFolder, "datei.mp3", "001 - Folge")]
                };
                RestructurePreviewDisplay display = new(original);
                restructure.SetExecuteResult(filesMoved: 1);

                int moved = await coordinator.ExecuteAsync(TestIds.SeriesA, display, cancellationToken: TestContext.Current.CancellationToken);

                Assert.Equal(1, moved);
                Assert.Equal(1, restructure.ExecuteCallCount);
            }
            finally
            {
                Directory.Delete(tempFolder);
            }
        }

        /// <summary>Vorschau, die zwei lose Dateien in je einen eigenen Folgenordner verschiebt.</summary>
        private static RestructurePreview TwoEpisodePreview(string folder)
            => new()
            {
                SeriesFolderPath = folder,
                FolderCount = 2,
                Actions =
                [
                    Move(folder, "001 - Erste.mp3", "001 - Erste", 1, "Erste"),
                    Move(folder, "002 - Zweite.mp3", "002 - Zweite", 2, "Zweite")
                ]
            };

        /// <summary>Verschiebt eine Datei aus dem Serienordner in den genannten Folgenordner.</summary>
        private static RestructureAction Move(
            string folder, string fileName, string targetFolderName, int? number = null, string? title = null)
            => new()
            {
                SourcePath = Path.Combine(folder, fileName),
                TargetFolderPath = Path.Combine(folder, targetFolderName),
                TargetFolderName = targetFolderName,
                FileName = fileName,
                EpisodeNumber = number,
                EpisodeTitle = title
            };

        private static string CreateTempFolder()
        {
            string path = Path.Combine(Path.GetTempPath(), $"echoplay-restructure-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            return path;
        }
    }
}

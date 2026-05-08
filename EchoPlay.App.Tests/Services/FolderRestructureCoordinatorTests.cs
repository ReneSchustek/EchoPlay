using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

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

            RestructurePreviewDisplay? result = await coordinator.AnalyzeAsync(string.Empty);

            Assert.Null(result);
        }

        [Fact]
        public async Task AnalyzeAsync_ReturnsNull_WhenFolderDoesNotExist()
        {
            (FolderRestructureCoordinator coordinator, _, _) = BuildCoordinator();

            RestructurePreviewDisplay? result = await coordinator.AnalyzeAsync(
                Path.Combine(Path.GetTempPath(), "echoplay-folder-restructure-tests-doesnotexist"));

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

                RestructurePreviewDisplay? result = await coordinator.AnalyzeAsync(tempFolder);

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

                restructure.SetAnalyzeResult(new RestructurePreview
                {
                    SeriesFolderPath = tempFolder,
                    FolderCount = 2,
                    Actions = new List<RestructureAction>
                    {
                        new()
                        {
                            SourcePath       = Path.Combine(tempFolder, "001 - Erste.mp3"),
                            TargetFolderPath = Path.Combine(tempFolder, "001 - Erste"),
                            TargetFolderName = "001 - Erste",
                            FileName         = "001 - Erste.mp3",
                            EpisodeNumber    = 1,
                            EpisodeTitle     = "Erste"
                        },
                        new()
                        {
                            SourcePath       = Path.Combine(tempFolder, "002 - Zweite.mp3"),
                            TargetFolderPath = Path.Combine(tempFolder, "002 - Zweite"),
                            TargetFolderName = "002 - Zweite",
                            FileName         = "002 - Zweite.mp3",
                            EpisodeNumber    = 2,
                            EpisodeTitle     = "Zweite"
                        }
                    }
                });

                RestructurePreviewDisplay? result = await coordinator.AnalyzeAsync(tempFolder);

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
                    Actions = new List<RestructureAction>
                    {
                        new()
                        {
                            SourcePath       = Path.Combine(tempFolder, "datei.mp3"),
                            TargetFolderPath = Path.Combine(tempFolder, "001 - Folge"),
                            TargetFolderName = "001 - Folge",
                            FileName         = "datei.mp3"
                        }
                    }
                };
                RestructurePreviewDisplay display = new(original);
                restructure.SetExecuteResult(filesMoved: 1);

                int moved = await coordinator.ExecuteAsync(display);

                Assert.Equal(1, moved);
                Assert.Equal(1, restructure.ExecuteCallCount);
            }
            finally
            {
                Directory.Delete(tempFolder);
            }
        }

        private static string CreateTempFolder()
        {
            string path = Path.Combine(Path.GetTempPath(), $"echoplay-restructure-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            return path;
        }
    }
}

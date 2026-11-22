using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft, was der Ordner-Umbau über das Verschieben hinaus erledigt: ein abgebrochener Lauf
    /// wird vor der Vorschau zurückgespielt, und nach dem Umbau zeigen Folgen und Spuren in der
    /// Datenbank auf die neuen Pfade.
    /// </summary>
    public sealed class FolderRestructureCoordinatorRelocationTests : IDisposable
    {
        private readonly string _series = Directory.CreateTempSubdirectory("echoplay_restructure_").FullName;
        private readonly FakeFolderRestructureService _restructure = new();
        private readonly FakeEpisodeDataService _episodes = new();
        private readonly FakeLocalTrackDataService _tracks = new();
        private readonly ServiceProvider _provider;

        /// <summary>Baut den Container mit Fakes für Dateidienst und Datenbank.</summary>
        public FolderRestructureCoordinatorRelocationTests()
        {
            ServiceCollection services = new();
            _ = services.AddSingleton<IFolderRestructureService>(_restructure);
            _ = services.AddSingleton<IAppSettingsDataService>(new FakeAppSettingsDataService(new AppSettings { EpisodeFolderPattern = "{number:000} - {title}" }));
            _ = services.AddSingleton<IEpisodeDataService>(_episodes);
            _ = services.AddSingleton<ILocalTrackDataService>(_tracks);
            _provider = services.BuildServiceProvider();
        }

        /// <summary>Gibt Container und Temp-Ordner frei.</summary>
        public void Dispose()
        {
            _provider.Dispose();
            Directory.Delete(_series, recursive: true);
        }

        [Fact]
        public async Task AnalyzeAsync_RecoversPendingJournalFirst()
        {
            _restructure.SetAnalyzeResult(new RestructurePreview { SeriesFolderPath = _series, Actions = [] });

            _ = await CreateCoordinator().AnalyzeAsync(_series, TestContext.Current.CancellationToken);

            Assert.Equal([_series], _restructure.RecoveredFolders);
        }

        [Fact]
        public async Task ExecuteAsync_MovedFile_UpdatesTrackAndEpisodePaths()
        {
            string source = Path.Combine(_series, "001 - Folge.mp3");
            string targetFolder = Directory.CreateDirectory(Path.Combine(_series, "001 - Folge")).FullName;
            await File.WriteAllTextAsync(Path.Combine(targetFolder, "001 - Folge.mp3"), string.Empty, TestContext.Current.CancellationToken);

            Episode episode = new() { SeriesId = TestIds.SeriesA, EpisodeNumber = 1, LocalFolderPath = _series };
            await _episodes.AddAsync(episode, TestContext.Current.CancellationToken);
            await _tracks.SaveTracksForEpisodeAsync(episode.Id, [new LocalTrack { EpisodeId = episode.Id, FilePath = source, TrackNumber = 1 }], TestContext.Current.CancellationToken);

            _ = await CreateCoordinator().ExecuteAsync(TestIds.SeriesA, Preview(source, targetFolder), TestContext.Current.CancellationToken);

            Assert.Equal(targetFolder, episode.LocalFolderPath);
            Assert.Equal(Path.Combine(targetFolder, "001 - Folge.mp3"), Assert.Single(_tracks.SavedTracks[episode.Id]).FilePath);
        }

        [Fact]
        public void MovedFiles_SkippedFile_IsNotCounted()
        {
            // Lag am Ziel schon eine Datei, verschiebt der Umbau nicht; die Quelle bleibt.
            string source = Path.Combine(_series, "002 - Folge.mp3");
            File.WriteAllText(source, string.Empty);
            string targetFolder = Directory.CreateDirectory(Path.Combine(_series, "002 - Folge")).FullName;
            File.WriteAllText(Path.Combine(targetFolder, "002 - Folge.mp3"), string.Empty);

            Assert.Empty(FolderRestructureCoordinator.MovedFiles(Preview(source, targetFolder).Original));
        }

        private FolderRestructureCoordinator CreateCoordinator()
            => new(_provider.GetRequiredService<IServiceScopeFactory>(), new FakeLoggerFactory());

        private RestructurePreviewDisplay Preview(string source, string targetFolder) => new(new RestructurePreview
        {
            SeriesFolderPath = _series,
            Actions =
            [
                new RestructureAction
                {
                    SourcePath = source,
                    TargetFolderPath = targetFolder,
                    TargetFolderName = Path.GetFileName(targetFolder),
                    FileName = Path.GetFileName(source)
                }
            ]
        });
    }
}

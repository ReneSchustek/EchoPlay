using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft den Ablauf des Koordinators: Muster aus den Einstellungen, danach die neuen Pfade
    /// bei Folgen und Spuren. Das Dateisystem steht hinter einem Fake des Umbenennen-Dienstes.
    /// </summary>
    public sealed class EpisodeFolderRenameCoordinatorFlowTests : IDisposable
    {
        private const string OldFolder = @"D:\Hörspiele\Die drei Ausrufezeichen\Serie - 087 - Alt";
        private const string NewFolder = @"D:\Hörspiele\Die drei Ausrufezeichen\087 - Geheimnisvoller Liebestrank";
        private static readonly Guid SeriesId = new("11111111-1111-1111-1111-111111111111");

        private readonly FakeEpisodeDataService _episodes = new();
        private readonly FakeLocalTrackDataService _tracks;
        private readonly RecordingRenameService _renameService = new();
        private readonly ServiceProvider _provider;
        private readonly Episode _episode;

        /// <summary>Legt eine Folge mit zwei Spuren im alten Ordner an.</summary>
        public EpisodeFolderRenameCoordinatorFlowTests()
        {
            _episode = new Episode { SeriesId = SeriesId, EpisodeNumber = 87, LocalFolderPath = OldFolder };
            _ = _episodes.AddAsync(_episode);
            _tracks = new FakeLocalTrackDataService(new Dictionary<Guid, IReadOnlyList<LocalTrack>>
            {
                [_episode.Id] =
                [
                    new LocalTrack { EpisodeId = _episode.Id, FilePath = OldFolder + @"\01.mp3", TrackNumber = 1, Duration = TimeSpan.FromMinutes(3) },
                    new LocalTrack { EpisodeId = _episode.Id, FilePath = OldFolder + @"\02.mp3", TrackNumber = 2, Duration = TimeSpan.FromMinutes(4) }
                ]
            });

            ServiceCollection services = new();
            _ = services.AddSingleton<IEpisodeDataService>(_episodes);
            _ = services.AddSingleton<ILocalTrackDataService>(_tracks);
            _ = services.AddSingleton<IAppSettingsDataService>(new FakeAppSettingsDataService(new AppSettings { EpisodeFolderPattern = "{number} - {title}" }));
            _ = services.AddSingleton<IEpisodeFolderRenameService>(_renameService);
            _provider = services.BuildServiceProvider();
        }

        /// <summary>Gibt den Container frei.</summary>
        public void Dispose() => _provider.Dispose();

        [Fact]
        public async Task ExecuteAsync_MovedFolder_UpdatesEpisodeAndTrackPaths()
        {
            _renameService.Result = new FolderRenameResult([(OldFolder, NewFolder)], SkippedCount: 0);

            _ = await CreateCoordinator().ExecuteAsync(SeriesId, EmptyPlan(), TestContext.Current.CancellationToken);

            Assert.Equal(NewFolder, _episode.LocalFolderPath);
            IReadOnlyList<LocalTrack> saved = _tracks.SavedTracks[_episode.Id];
            Assert.Equal([NewFolder + @"\01.mp3", NewFolder + @"\02.mp3"], saved.Select(track => track.FilePath));
            Assert.Equal([1, 2], saved.Select(track => track.TrackNumber));
            Assert.Equal(TimeSpan.FromMinutes(4), saved[1].Duration);
        }

        [Fact]
        public async Task ExecuteAsync_NothingMoved_LeavesDatabaseAlone()
        {
            _renameService.Result = new FolderRenameResult([], SkippedCount: 1);

            FolderRenameResult result = await CreateCoordinator().ExecuteAsync(SeriesId, EmptyPlan(), TestContext.Current.CancellationToken);

            Assert.Equal(1, result.SkippedCount);
            Assert.Equal(OldFolder, _episode.LocalFolderPath);
            Assert.Empty(_tracks.SavedTracks);
        }

        [Fact]
        public async Task ExecuteAsync_MoveOfAnotherFolder_DoesNotTouchThisEpisode()
        {
            _renameService.Result = new FolderRenameResult([(OldFolder + " 2", NewFolder + " 2")], SkippedCount: 0);

            _ = await CreateCoordinator().ExecuteAsync(SeriesId, EmptyPlan(), TestContext.Current.CancellationToken);

            Assert.Equal(OldFolder, _episode.LocalFolderPath);
            Assert.Empty(_tracks.SavedTracks);
        }

        [Fact]
        public async Task PlanAsync_ExistingFolder_UsesPatternFromSettings()
        {
            string folder = Directory.CreateTempSubdirectory("echoplay_plan_").FullName;
            try
            {
                FolderRenamePlan? plan = await CreateCoordinator().PlanAsync(folder, TestContext.Current.CancellationToken);

                Assert.NotNull(plan);
                Assert.Equal("{number} - {title}", _renameService.PlannedPattern);
                Assert.Equal(folder, _renameService.PlannedFolder);
            }
            finally
            {
                Directory.Delete(folder);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData(@"X:\gibt\es\nicht")]
        public async Task PlanAsync_MissingFolder_ReturnsNull(string folder)
        {
            Assert.Null(await CreateCoordinator().PlanAsync(folder, TestContext.Current.CancellationToken));
            Assert.Null(_renameService.PlannedFolder);
        }

        private EpisodeFolderRenameCoordinator CreateCoordinator()
            => new(_provider.GetRequiredService<IServiceScopeFactory>(), new FakeLoggerFactory());

        private static FolderRenamePlan EmptyPlan() => new() { SeriesFolderPath = @"D:\Hörspiele\Die drei Ausrufezeichen", Items = [] };

        /// <summary>Merkt sich die Vorschau-Anfrage und liefert ein vorgegebenes Ergebnis.</summary>
        private sealed class RecordingRenameService : IEpisodeFolderRenameService
        {
            public string? PlannedFolder { get; private set; }

            public string? PlannedPattern { get; private set; }

            public FolderRenameResult Result { get; set; } = new([], 0);

            public FolderRenamePlan Plan(string seriesFolderPath, string folderPattern)
            {
                PlannedFolder = seriesFolderPath;
                PlannedPattern = folderPattern;
                return new FolderRenamePlan { SeriesFolderPath = seriesFolderPath, Items = [] };
            }

            public FolderRenameResult Execute(FolderRenamePlan plan) => Result;
        }
    }
}

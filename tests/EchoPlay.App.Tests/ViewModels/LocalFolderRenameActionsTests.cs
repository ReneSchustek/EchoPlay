using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Aktion „Ordner nach Tags umbenennen": Ordner der Serie finden, Vorschau holen,
    /// nach dem Umbenennen die Mediathek neu laden.
    /// </summary>
    public sealed class LocalFolderRenameActionsTests
    {
        private const string SeriesFolder = @"D:\Hörspiele\Die drei Ausrufezeichen";

        private readonly RecordingCoordinator _coordinator = new();
        private int _reloadCount;

        [Fact]
        public async Task PlanAsync_KnownSeries_AsksForItsFolder()
        {
            LocalFolderRenameActions actions = await CreateActionsAsync(_coordinator);

            FolderRenamePlan? plan = await actions.PlanAsync(TestIds.SeriesA);

            Assert.NotNull(plan);
            Assert.Equal(SeriesFolder, _coordinator.PlannedFolder);
        }

        [Fact]
        public async Task PlanAsync_UnknownSeries_ReturnsNull()
        {
            LocalFolderRenameActions actions = await CreateActionsAsync(_coordinator);

            Assert.Null(await actions.PlanAsync(TestIds.SeriesB));
            Assert.Null(_coordinator.PlannedFolder);
        }

        [Fact]
        public async Task ExecuteAsync_SomethingRenamed_ReloadsLibrary()
        {
            _coordinator.Result = new FolderRenameResult([(SeriesFolder + @"\a", SeriesFolder + @"\b")], SkippedCount: 0);
            LocalFolderRenameActions actions = await CreateActionsAsync(_coordinator);

            _ = await actions.ExecuteAsync(TestIds.SeriesA, Plan());

            Assert.Equal(1, _reloadCount);
        }

        [Fact]
        public async Task ExecuteAsync_NothingRenamed_DoesNotReload()
        {
            LocalFolderRenameActions actions = await CreateActionsAsync(_coordinator);

            _ = await actions.ExecuteAsync(TestIds.SeriesA, Plan());

            Assert.Equal(0, _reloadCount);
        }

        [Fact]
        public async Task WithoutCoordinator_DoesNothing()
        {
            LocalFolderRenameActions actions = await CreateActionsAsync(coordinator: null);

            Assert.Null(await actions.PlanAsync(TestIds.SeriesA));
            Assert.Empty((await actions.ExecuteAsync(TestIds.SeriesA, Plan())).Moves);
            Assert.Equal(0, _reloadCount);
        }

        private async Task<LocalFolderRenameActions> CreateActionsAsync(IEpisodeFolderRenameCoordinator? coordinator)
        {
            ServiceCollection services = new();
            IEpisodeDataService episodes = new FakeEpisodeDataService();
            _ = services.AddScoped(_ => episodes);
            LocalArtistsViewModel artists = new(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

            Series series = new() { Title = "Die drei Ausrufezeichen", LocalFolderPath = SeriesFolder };
            typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(series, TestIds.SeriesA);
            await artists.AppendArtistCardAsync(series);

            return new LocalFolderRenameActions(coordinator, artists, () =>
            {
                _reloadCount++;
                return Task.CompletedTask;
            });
        }

        private static FolderRenamePlan Plan() => new() { SeriesFolderPath = SeriesFolder, Items = [] };

        /// <summary>Merkt sich die Anfrage und liefert ein vorgegebenes Ergebnis.</summary>
        private sealed class RecordingCoordinator : IEpisodeFolderRenameCoordinator
        {
            public string? PlannedFolder { get; private set; }

            public FolderRenameResult Result { get; set; } = new([], 0);

            public Task<FolderRenamePlan?> PlanAsync(string seriesFolderPath, CancellationToken cancellationToken = default)
            {
                PlannedFolder = seriesFolderPath;
                return Task.FromResult<FolderRenamePlan?>(new FolderRenamePlan { SeriesFolderPath = seriesFolderPath, Items = [] });
            }

            public Task<FolderRenameResult> ExecuteAsync(Guid seriesId, FolderRenamePlan plan, CancellationToken cancellationToken = default)
                => Task.FromResult(Result);
        }
    }
}

using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft das Speichern aus „Daten bearbeiten": Tags in jeder Datei, Angaben in der
    /// Datenbank und der Ordnername nach Muster, damit das nächste Einlesen die Änderung
    /// nicht zurücknimmt.
    /// </summary>
    public sealed class LocalMetadataEditorTests : IDisposable
    {
        private readonly string _seriesFolder;
        private readonly string _episodeFolder;
        private readonly Series _series;
        private readonly Episode _episode;
        private readonly FakeEpisodeDataService _episodes = new();
        private readonly FakeSeriesDataService _seriesService = new();
        private readonly FakeTagService _tags = new();
        private readonly RecordingRenameCoordinator _rename = new();
        private readonly ServiceProvider _provider;

        /// <summary>Legt eine Folge mit zwei Dateien in einem eigenen Ordner an.</summary>
        public LocalMetadataEditorTests()
        {
            _seriesFolder = Directory.CreateTempSubdirectory("echoplay_edit_").FullName;
            _episodeFolder = Path.Combine(_seriesFolder, "Die drei Ausrufezeichen - 066 -Tanz der Herzen");
            _ = Directory.CreateDirectory(_episodeFolder);

            _series = new Series { Title = "Die drei Ausrufezeichen", LocalFolderPath = _seriesFolder };
            _ = _seriesService.AddAsync(_series);
            _episode = new Episode
            {
                SeriesId = _series.Id,
                Title = "Tanz der Herzen",
                EpisodeNumber = 66,
                LocalFolderPath = _episodeFolder,
                ReleaseDate = new DateTime(2019, 3, 8, 0, 0, 0, DateTimeKind.Utc)
            };
            _ = _episodes.AddAsync(_episode);
            _provider = BuildProvider();
        }

        /// <summary>
        /// Registriert die Nachbauten der Felder und die beiden Dateien der Folge; das Muster der
        /// Ordnernamen ist das der Einstellungen.
        /// </summary>
        private ServiceProvider BuildProvider()
        {
            FakeLocalTrackDataService tracks = new(new Dictionary<Guid, IReadOnlyList<LocalTrack>>
            {
                [_episode.Id] =
                [
                    new LocalTrack { EpisodeId = _episode.Id, FilePath = Path.Combine(_episodeFolder, "01.mp3"), TrackNumber = 1 },
                    new LocalTrack { EpisodeId = _episode.Id, FilePath = Path.Combine(_episodeFolder, "02.mp3"), TrackNumber = 2 }
                ]
            });

            ServiceCollection services = new();
            _ = services.AddSingleton<IEpisodeDataService>(_episodes);
            _ = services.AddSingleton<ISeriesDataService>(_seriesService);
            _ = services.AddSingleton<ILocalTrackDataService>(tracks);
            _ = services.AddSingleton<ITagService>(_tags);
            _ = services.AddSingleton<IAppSettingsDataService>(new FakeAppSettingsDataService(new AppSettings { EpisodeFolderPattern = "{number:000} - {title}" }));
            return services.BuildServiceProvider();
        }

        /// <summary>Räumt Container und Temp-Ordner weg.</summary>
        public void Dispose()
        {
            _provider.Dispose();
            Directory.Delete(_seriesFolder, recursive: true);
        }

        [Fact]
        public async Task SaveEpisode_WritesTagsDatabaseAndRenamesTheFolder()
        {
            EpisodeEditOutcome outcome = await CreateEditor().SaveEpisodeAsync(
                new EpisodeEditData(_episode.Id, "Tanz der Herzen (Neuauflage)", 67, "Die drei !!!", 2020),
                TestContext.Current.CancellationToken);

            Assert.Equal(new EpisodeEditOutcome(2, 0, FolderRenamed: true), outcome);

            AudioTag written = await _tags.ReadAsync(Path.Combine(_episodeFolder, "01.mp3"));
            Assert.Equal("Tanz der Herzen (Neuauflage)", written.Album);
            Assert.Equal("Die drei !!!", written.Artist);
            Assert.Equal(2020u, written.Year);
            // Der Spurtitel nennt das Kapitel und bleibt unangetastet.
            Assert.Null(written.Title);

            Assert.Equal("Tanz der Herzen (Neuauflage)", _episode.Title);
            Assert.Equal(67, _episode.EpisodeNumber);
            Assert.Equal(2020, _episode.ReleaseDate?.Year);

            FolderRenameItem item = Assert.Single(_rename.Plan!.Items);
            Assert.Equal("067 - Tanz der Herzen (Neuauflage)", item.TargetName);
            Assert.Equal(_series.Id, _rename.SeriesId);
        }

        [Fact]
        public async Task SaveEpisode_OnlyArtistChanged_LeavesTheFolderAlone()
        {
            EpisodeEditOutcome outcome = await CreateEditor().SaveEpisodeAsync(
                new EpisodeEditData(_episode.Id, "Tanz der Herzen", 66, "Neuer Interpret", 2019),
                TestContext.Current.CancellationToken);

            Assert.False(outcome.FolderRenamed);
            Assert.Null(_rename.Plan);
            // Gleiches Jahr: Monat und Tag bleiben.
            Assert.Equal(new DateTime(2019, 3, 8, 0, 0, 0, DateTimeKind.Utc), _episode.ReleaseDate);
        }

        [Fact]
        public async Task SaveEpisode_FlatSeries_DoesNotRenameTheSeriesFolder()
        {
            // Liegt die Folge direkt im Serienordner, gibt es keinen eigenen Folgenordner.
            _episode.LocalFolderPath = _seriesFolder;

            EpisodeEditOutcome outcome = await CreateEditor().SaveEpisodeAsync(
                new EpisodeEditData(_episode.Id, "Anderer Titel", 66, null, null),
                TestContext.Current.CancellationToken);

            Assert.False(outcome.FolderRenamed);
            Assert.Null(_rename.Plan);
            Assert.Equal("Anderer Titel", _episode.Title);
            Assert.Null(_episode.ReleaseDate);
        }

        [Fact]
        public async Task SaveEpisode_UnwritableFile_IsCountedAndTheRestIsSaved()
        {
            _tags.WriteFailure = new IOException("gesperrt");

            EpisodeEditOutcome outcome = await CreateEditor().SaveEpisodeAsync(
                new EpisodeEditData(_episode.Id, "Tanz der Herzen", 66, null, 2019),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, outcome.TagsWritten);
            Assert.Equal(2, outcome.TagsFailed);
        }

        [Fact]
        public async Task LoadEpisode_TakesArtistFromTheFirstFile()
        {
            await _tags.WriteAsync(Path.Combine(_episodeFolder, "01.mp3"), new AudioTag { Artist = "Die drei !!!" });

            EpisodeEditData? data = await CreateEditor().LoadEpisodeAsync(_episode.Id, TestContext.Current.CancellationToken);

            Assert.Equal(new EpisodeEditData(_episode.Id, "Tanz der Herzen", 66, "Die drei !!!", 2019), data);
        }

        [Fact]
        public async Task SaveSeriesTitle_StoresTheTrimmedTitle()
        {
            bool saved = await CreateEditor().SaveSeriesTitleAsync(_series.Id, "  Die drei !!!  ", TestContext.Current.CancellationToken);

            Assert.True(saved);
            Assert.Equal("Die drei !!!", _series.Title);
        }

        private LocalMetadataEditor CreateEditor()
            => new(_provider.GetRequiredService<IServiceScopeFactory>(), _rename, new FakeLoggerFactory());

        /// <summary>Merkt sich den Umbenennen-Auftrag statt Ordner zu verschieben.</summary>
        private sealed class RecordingRenameCoordinator : IEpisodeFolderRenameCoordinator
        {
            public FolderRenamePlan? Plan { get; private set; }

            public Guid SeriesId { get; private set; }

            public Task<FolderRenamePlan?> PlanAsync(string seriesFolderPath, CancellationToken cancellationToken = default)
                => Task.FromResult<FolderRenamePlan?>(null);

            public Task<FolderRenameResult> ExecuteAsync(Guid seriesId, FolderRenamePlan plan, CancellationToken cancellationToken = default)
            {
                SeriesId = seriesId;
                Plan = plan;
                FolderRenameItem item = plan.Items[0];
                return Task.FromResult(new FolderRenameResult([(item.CurrentPath, item.TargetPath)], 0));
            }
        }
    }
}

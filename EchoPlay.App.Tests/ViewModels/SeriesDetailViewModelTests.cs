using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="SeriesDetailViewModel"/>.
    /// Prüft Laden der Episodenliste, Statusglyph-Ermittlung und Wiedergabestart.
    /// </summary>
    public sealed class SeriesDetailViewModelTests
    {
        private static SeriesDetailViewModel BuildViewModel(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            FakePlaybackStateDataService playbackService,
            FakeLocalTrackDataService trackService,
            FakePlayerService playerService)
        {
            ServiceCollection services = new();
            services.AddScoped<ISeriesDataService>(_ => seriesService);
            services.AddScoped<IEpisodeDataService>(_ => episodeService);
            services.AddScoped<IPlaybackStateDataService>(_ => playbackService);
            services.AddScoped<ILocalTrackDataService>(_ => trackService);

            ServiceProvider provider = services.BuildServiceProvider();

            return new SeriesDetailViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                playerService);
        }

        [Fact]
        public async Task LoadAsync_SetsSeriesTitle()
        {
            // SeriesTitle muss nach LoadAsync den Titel aus der DB widerspiegeln
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG" });
            Series series = seriesService.All[0];

            SeriesDetailViewModel vm = BuildViewModel(seriesService,
                episodeService: new FakeEpisodeDataService(),
                playbackService: new FakePlaybackStateDataService(),
                trackService: new FakeLocalTrackDataService(),
                playerService: new FakePlayerService());

            await vm.LoadAsync(series.Id);

            Assert.Equal("TKKG", vm.SeriesTitle);
        }

        [Fact]
        public async Task LoadAsync_LoadsAllEpisodes()
        {
            // Alle Episoden der Serie müssen als Zeilen geladen werden
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "Die drei ???" });
            Series series = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode { SeriesId = series.Id, Title = "Folge 1", EpisodeNumber = 1 });
            await episodeService.AddAsync(new Episode { SeriesId = series.Id, Title = "Folge 2", EpisodeNumber = 2 });
            await episodeService.AddAsync(new Episode { SeriesId = series.Id, Title = "Folge 3", EpisodeNumber = 3 });

            SeriesDetailViewModel vm = BuildViewModel(seriesService, episodeService,
                playbackService: new FakePlaybackStateDataService(),
                trackService: new FakeLocalTrackDataService(),
                playerService: new FakePlayerService());

            await vm.LoadAsync(series.Id);

            Assert.Equal(3, vm.Episodes.Count);
        }

        [Fact]
        public async Task LoadAsync_SetsStatusGlyph_WhenNotStarted()
        {
            // Episode ohne PlaybackState erhält den Glyph für "Nicht gespielt"
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG" });
            Series series = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode { SeriesId = series.Id, Title = "Folge 1", EpisodeNumber = 1 });

            SeriesDetailViewModel vm = BuildViewModel(seriesService, episodeService,
                playbackService: new FakePlaybackStateDataService(),  // kein State vorhanden
                trackService: new FakeLocalTrackDataService(),
                playerService: new FakePlayerService());

            await vm.LoadAsync(series.Id);

            // Glyph für "Nicht gespielt" (Radiobuttonleer)
            Assert.Equal("\uE73E", vm.Episodes[0].StatusGlyph);
        }

        [Fact]
        public async Task LoadAsync_SetsStatusGlyph_WhenInProgress()
        {
            // Episode mit nicht-vollständigem PlaybackState erhält den Glyph für "In Bearbeitung"
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG" });
            Series series = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode { SeriesId = series.Id, Title = "Folge 1", EpisodeNumber = 1 });
            Episode episode = episodeService.All[0];

            FakePlaybackStateDataService playbackService = new(
            [
                new PlaybackState
                {
                    EpisodeId    = episode.Id,
                    LastPosition = TimeSpan.FromMinutes(10),
                    IsCompleted  = false
                }
            ]);

            SeriesDetailViewModel vm = BuildViewModel(seriesService, episodeService,
                playbackService: playbackService,
                trackService: new FakeLocalTrackDataService(),
                playerService: new FakePlayerService());

            await vm.LoadAsync(series.Id);

            // Glyph für "In Bearbeitung" (Fortschrittsicon)
            Assert.Equal("\uE916", vm.Episodes[0].StatusGlyph);
        }

        [Fact]
        public async Task LoadAsync_SetsStatusGlyph_WhenFinished()
        {
            // Episode mit abgeschlossenem PlaybackState erhält den Glyph für "Abgeschlossen"
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG" });
            Series series = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode { SeriesId = series.Id, Title = "Folge 1", EpisodeNumber = 1 });
            Episode episode = episodeService.All[0];

            FakePlaybackStateDataService playbackService = new(
            [
                new PlaybackState
                {
                    EpisodeId    = episode.Id,
                    LastPosition = TimeSpan.FromMinutes(50),
                    IsCompleted  = true
                }
            ]);

            SeriesDetailViewModel vm = BuildViewModel(seriesService, episodeService,
                playbackService: playbackService,
                trackService: new FakeLocalTrackDataService(),
                playerService: new FakePlayerService());

            await vm.LoadAsync(series.Id);

            // Glyph für "Abgeschlossen" (Häkchen)
            Assert.Equal("\uE8FB", vm.Episodes[0].StatusGlyph);
        }

        [Fact]
        public async Task PlayEpisodeAsync_DoesNothing_WhenNoLocalTracks()
        {
            // Ohne lokale Tracks darf der PlayerService nicht aufgerufen werden
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG" });
            Series series = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode { SeriesId = series.Id, Title = "Folge 1", EpisodeNumber = 1 });
            Episode episode = episodeService.All[0];

            FakePlayerService playerService = new();

            SeriesDetailViewModel vm = BuildViewModel(seriesService, episodeService,
                playbackService: new FakePlaybackStateDataService(),
                trackService: new FakeLocalTrackDataService(),  // keine Tracks
                playerService: playerService);

            await vm.PlayEpisodeAsync(episode.Id);

            Assert.Empty(playerService.PlayCalls);
        }

        [Fact]
        public async Task PlayEpisodeAsync_CallsPlayerService_WithTrackPaths()
        {
            // Der PlayerService muss mit den richtigen Pfaden aufgerufen werden
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG" });
            Series series = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode { SeriesId = series.Id, Title = "Folge 1", EpisodeNumber = 1 });
            Episode episode = episodeService.All[0];

            IReadOnlyList<LocalTrack> tracks =
            [
                new LocalTrack { EpisodeId = episode.Id, FilePath = "/track1.mp3", TrackNumber = 1 },
                new LocalTrack { EpisodeId = episode.Id, FilePath = "/track2.mp3", TrackNumber = 2 }
            ];

            FakeLocalTrackDataService trackService = new(new System.Collections.Generic.Dictionary<Guid, IReadOnlyList<LocalTrack>>
            {
                [episode.Id] = tracks
            });

            FakePlayerService playerService = new();

            SeriesDetailViewModel vm = BuildViewModel(seriesService, episodeService,
                playbackService: new FakePlaybackStateDataService(),
                trackService: trackService,
                playerService: playerService);

            await vm.PlayEpisodeAsync(episode.Id);

            Assert.Single(playerService.PlayCalls);
            Assert.Equal(episode.Id, playerService.PlayCalls[0].EpisodeId);
            Assert.Equal(2, playerService.PlayCalls[0].TrackPaths.Count);
        }

        [Fact]
        public async Task PlayEpisodeAsync_ResumesAtSavedPosition_WhenNotCompleted()
        {
            // Nicht abgeschlossene Episode wird an der gespeicherten Position fortgesetzt
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG" });
            Series series = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode { SeriesId = series.Id, Title = "Folge 1", EpisodeNumber = 1 });
            Episode episode = episodeService.All[0];

            TimeSpan savedPosition = TimeSpan.FromMinutes(15);

            FakePlaybackStateDataService playbackService = new(
            [
                new PlaybackState
                {
                    EpisodeId    = episode.Id,
                    LastPosition = savedPosition,
                    IsCompleted  = false
                }
            ]);

            IReadOnlyList<LocalTrack> tracks =
            [
                new LocalTrack { EpisodeId = episode.Id, FilePath = "/track1.mp3", TrackNumber = 1 }
            ];

            FakeLocalTrackDataService trackService = new(new System.Collections.Generic.Dictionary<Guid, IReadOnlyList<LocalTrack>>
            {
                [episode.Id] = tracks
            });

            FakePlayerService playerService = new();

            SeriesDetailViewModel vm = BuildViewModel(seriesService, episodeService,
                playbackService: playbackService,
                trackService: trackService,
                playerService: playerService);

            await vm.PlayEpisodeAsync(episode.Id);

            Assert.Equal(savedPosition, playerService.PlayCalls[0].ResumePosition);
        }

        [Fact]
        public async Task PlayEpisodeAsync_StartsFromBeginning_WhenCompleted()
        {
            // Abgeschlossene Episode wird von Anfang an gespielt
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG" });
            Series series = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode { SeriesId = series.Id, Title = "Folge 1", EpisodeNumber = 1 });
            Episode episode = episodeService.All[0];

            FakePlaybackStateDataService playbackService = new(
            [
                new PlaybackState
                {
                    EpisodeId    = episode.Id,
                    LastPosition = TimeSpan.FromMinutes(50),
                    IsCompleted  = true  // Abgeschlossen
                }
            ]);

            IReadOnlyList<LocalTrack> tracks =
            [
                new LocalTrack { EpisodeId = episode.Id, FilePath = "/track1.mp3", TrackNumber = 1 }
            ];

            FakeLocalTrackDataService trackService = new(new System.Collections.Generic.Dictionary<Guid, IReadOnlyList<LocalTrack>>
            {
                [episode.Id] = tracks
            });

            FakePlayerService playerService = new();

            SeriesDetailViewModel vm = BuildViewModel(seriesService, episodeService,
                playbackService: playbackService,
                trackService: trackService,
                playerService: playerService);

            await vm.PlayEpisodeAsync(episode.Id);

            // Position = TimeSpan.Zero bedeutet: von vorne starten
            Assert.Equal(TimeSpan.Zero, playerService.PlayCalls[0].ResumePosition);
        }
    }
}

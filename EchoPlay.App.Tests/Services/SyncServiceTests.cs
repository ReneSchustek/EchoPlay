using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Matching;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="SyncService"/>.
    /// Prüft das Matching-Verhalten und die korrekte Erstellung von LocalTracks.
    /// </summary>
    public sealed class SyncServiceTests
    {
        private static SyncService BuildService(
            FakeAppSettingsDataService settingsService,
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            FakeLocalTrackDataService trackService,
            FakeLocalLibraryScanner scanner,
            FakeTrackMatcher trackMatcher,
            FakeMp3MetadataReader metadataReader)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => settingsService);
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<ILocalTrackDataService>(_ => trackService);
            _ = services.AddScoped<ILocalLibraryScanner>(_ => scanner);
            _ = services.AddScoped<IScanOrchestrator>(_ => new FakeScanOrchestrator(scanner));
            _ = services.AddScoped<ILocalCoverService>(_ => new FakeLocalCoverService());
            _ = services.AddScoped<ITrackMatcher>(_ => trackMatcher);
            _ = services.AddScoped<IMp3MetadataReader>(_ => metadataReader);
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());

            _ = services.AddSingleton<ILoggerFactory>(new FakeLoggerFactory());
            ServiceProvider provider = services.BuildServiceProvider();

            EchoPlay.App.Services.CoverService coverService = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<ILoggerFactory>());

            return new SyncService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<ILoggerFactory>(),
                new FakeScanEventService(),
                coverService);
        }

        [Fact]
        public async Task SyncAsync_ReturnsEmpty_WhenLibraryDisabled()
        {
            // Kein Scan, wenn LocalLibraryEnabled = false
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled  = false,
                LocalLibraryRootPath = "/music"
            });

            SyncService service = BuildService(settings,
                seriesService: new FakeSeriesDataService(),
                episodeService: new FakeEpisodeDataService(),
                trackService: new FakeLocalTrackDataService(),
                scanner: new FakeLocalLibraryScanner([]),
                trackMatcher: new FakeTrackMatcher(),
                metadataReader: new FakeMp3MetadataReader());

            SyncResult result = await service.SyncAsync();

            Assert.Equal(0, result.SeriesMatched);
            Assert.Equal(0, result.SeriesUnmatched);
            Assert.Equal(0, result.EpisodesUpdated);
            Assert.Equal(0, result.TracksCreated);
        }

        [Fact]
        public async Task SyncAsync_ReturnsEmpty_WhenRootPathEmpty()
        {
            // Kein Scan, wenn kein Bibliothekspfad gesetzt ist
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled  = true,
                LocalLibraryRootPath = null
            });

            SyncService service = BuildService(settings,
                seriesService: new FakeSeriesDataService(),
                episodeService: new FakeEpisodeDataService(),
                trackService: new FakeLocalTrackDataService(),
                scanner: new FakeLocalLibraryScanner([]),
                trackMatcher: new FakeTrackMatcher(),
                metadataReader: new FakeMp3MetadataReader());

            SyncResult result = await service.SyncAsync();

            Assert.Equal(0, result.SeriesMatched);
        }

        [Fact]
        public async Task SyncAsync_MatchesSeries_ByNormalizedName()
        {
            // Seriennamen mit unterschiedlicher Schreibweise werden über Normalisierung gematcht
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled  = true,
                LocalLibraryRootPath = "/music"
            });

            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "Die Drei ???" });

            IReadOnlyList<LocalScanResult> scanResults =
            [
                new LocalScanResult
                {
                    SeriesName       = "die drei ???",  // Andere Schreibweise
                    SeriesFolderPath = "/music/die drei ???",
                    Episodes         = []
                }
            ];

            SyncService service = BuildService(settings, seriesService,
                episodeService: new FakeEpisodeDataService(),
                trackService: new FakeLocalTrackDataService(),
                scanner: new FakeLocalLibraryScanner(scanResults),
                trackMatcher: new FakeTrackMatcher(),
                metadataReader: new FakeMp3MetadataReader());

            SyncResult result = await service.SyncAsync();

            Assert.Equal(1, result.SeriesMatched);
        }

        [Fact]
        public async Task SyncAsync_SkipsUnmatchedSeries_CountsAsUnmatched()
        {
            // Lokale Ordner ohne passende DB-Serie werden als "unmatched" gezählt –
            // AutoImportAfterScan muss deaktiviert sein, damit kein Eintrag angelegt wird.
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled    = true,
                LocalLibraryRootPath   = "/music",
                AutoImportAfterScan    = false
            });

            IReadOnlyList<LocalScanResult> scanResults =
            [
                new LocalScanResult
                {
                    SeriesName       = "Unbekannte Serie",
                    SeriesFolderPath = "/music/unbekannt",
                    Episodes         = []
                }
            ];

            SyncService service = BuildService(settings,
                seriesService: new FakeSeriesDataService(),  // leere DB
                episodeService: new FakeEpisodeDataService(),
                trackService: new FakeLocalTrackDataService(),
                scanner: new FakeLocalLibraryScanner(scanResults),
                trackMatcher: new FakeTrackMatcher(),
                metadataReader: new FakeMp3MetadataReader());

            SyncResult result = await service.SyncAsync();

            Assert.Equal(0, result.SeriesMatched);
            Assert.Equal(1, result.SeriesUnmatched);
        }

        [Fact]
        public async Task SyncAsync_UpdatesEpisode_WhenNumberMatches()
        {
            // Lokale Episode wird aktualisiert, wenn die Episodennummer übereinstimmt
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled  = true,
                LocalLibraryRootPath = "/music"
            });

            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG" });

            Series addedSeries = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode
            {
                SeriesId      = addedSeries.Id,
                Title         = "Folge 1",
                EpisodeNumber = 1
            });

            IReadOnlyList<LocalScanResult> scanResults =
            [
                new LocalScanResult
                {
                    SeriesName       = "TKKG",
                    SeriesFolderPath = "/music/TKKG",
                    Episodes         =
                    [
                        new LocalEpisodeScan
                        {
                            FolderPath   = "/music/TKKG/001 - Folge 1",
                            ParsedNumber = 1,
                            TrackPaths   = ["/music/TKKG/001 - Folge 1/track1.mp3"]
                        }
                    ]
                }
            ];

            SyncService service = BuildService(settings, seriesService, episodeService,
                trackService: new FakeLocalTrackDataService(),
                scanner: new FakeLocalLibraryScanner(scanResults),
                trackMatcher: new FakeTrackMatcher(),
                metadataReader: new FakeMp3MetadataReader());

            SyncResult result = await service.SyncAsync();

            Assert.Equal(1, result.EpisodesUpdated);
        }

        [Fact]
        public async Task SyncAsync_CreatesLocalTracks_ForMatchedEpisode()
        {
            // Für jede gematchte Episode werden LocalTracks gespeichert
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled  = true,
                LocalLibraryRootPath = "/music"
            });

            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "Die drei ???" });

            Series series = seriesService.All[0];
            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode
            {
                SeriesId      = series.Id,
                Title         = "Folge 1",
                EpisodeNumber = 1
            });

            Episode episode = episodeService.All[0];

            IReadOnlyList<LocalScanResult> scanResults =
            [
                new LocalScanResult
                {
                    SeriesName       = "Die drei ???",
                    SeriesFolderPath = "/music/Die drei ???",
                    Episodes         =
                    [
                        new LocalEpisodeScan
                        {
                            FolderPath   = "/music/Die drei ???/001",
                            ParsedNumber = 1,
                            TrackPaths   = ["/music/001/track1.mp3", "/music/001/track2.mp3"]
                        }
                    ]
                }
            ];

            FakeLocalTrackDataService trackService = new();
            SyncService service = BuildService(settings, seriesService, episodeService,
                trackService: trackService,
                scanner: new FakeLocalLibraryScanner(scanResults),
                trackMatcher: new FakeTrackMatcher(),
                metadataReader: new FakeMp3MetadataReader());

            _ = await service.SyncAsync();

            Assert.True(trackService.SavedTracks.ContainsKey(episode.Id));
            Assert.Equal(2, trackService.SavedTracks[episode.Id].Count);
        }

        [Fact]
        public async Task SyncAsync_CountsCorrectly_AllCounters()
        {
            // Alle Zähler müssen nach einem erfolgreichen Sync korrekt befüllt sein –
            // AutoImportAfterScan deaktiviert, damit die unbekannte Serie als "unmatched" gezählt wird.
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled  = true,
                LocalLibraryRootPath = "/music",
                AutoImportAfterScan  = false
            });

            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG" });
            Series series = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode { SeriesId = series.Id, Title = "Folge 1", EpisodeNumber = 1 });
            await episodeService.AddAsync(new Episode { SeriesId = series.Id, Title = "Folge 2", EpisodeNumber = 2 });

            IReadOnlyList<LocalScanResult> scanResults =
            [
                new LocalScanResult
                {
                    SeriesName       = "TKKG",
                    SeriesFolderPath = "/music/TKKG",
                    Episodes         =
                    [
                        new LocalEpisodeScan
                        {
                            FolderPath   = "/music/TKKG/001",
                            ParsedNumber = 1,
                            TrackPaths   = ["/music/TKKG/001/t1.mp3"]
                        },
                        new LocalEpisodeScan
                        {
                            FolderPath   = "/music/TKKG/002",
                            ParsedNumber = 2,
                            TrackPaths   = ["/music/TKKG/002/t1.mp3", "/music/TKKG/002/t2.mp3"]
                        }
                    ]
                },
                new LocalScanResult
                {
                    SeriesName       = "Unbekannte Serie",
                    SeriesFolderPath = "/music/Unbekannte Serie",
                    Episodes         = []
                }
            ];

            SyncService service = BuildService(settings, seriesService, episodeService,
                trackService: new FakeLocalTrackDataService(),
                scanner: new FakeLocalLibraryScanner(scanResults),
                trackMatcher: new FakeTrackMatcher(),
                metadataReader: new FakeMp3MetadataReader());

            SyncResult result = await service.SyncAsync();

            Assert.Equal(1, result.SeriesMatched);
            Assert.Equal(1, result.SeriesUnmatched);
            Assert.Equal(2, result.EpisodesUpdated);
            Assert.Equal(3, result.TracksCreated);  // 1 + 2 Tracks
        }

        [Fact]
        public async Task SyncAsync_SkipsEpisodeScan_WhenParsedNumberNull()
        {
            // Episodenordner ohne erkennbare Nummer werden übersprungen
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled  = true,
                LocalLibraryRootPath = "/music"
            });

            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG" });

            IReadOnlyList<LocalScanResult> scanResults =
            [
                new LocalScanResult
                {
                    SeriesName       = "TKKG",
                    SeriesFolderPath = "/music/TKKG",
                    Episodes         =
                    [
                        new LocalEpisodeScan
                        {
                            FolderPath   = "/music/TKKG/unbekannt",
                            ParsedNumber = null,  // Keine Nummer erkannt
                            TrackPaths   = ["/music/TKKG/unbekannt/track.mp3"]
                        }
                    ]
                }
            ];

            SyncService service = BuildService(settings, seriesService,
                episodeService: new FakeEpisodeDataService(),
                trackService: new FakeLocalTrackDataService(),
                scanner: new FakeLocalLibraryScanner(scanResults),
                trackMatcher: new FakeTrackMatcher(),
                metadataReader: new FakeMp3MetadataReader());

            SyncResult result = await service.SyncAsync();

            // Serie matched, aber keine Episode aktualisiert
            Assert.Equal(1, result.SeriesMatched);
            Assert.Equal(0, result.EpisodesUpdated);
        }
    }
}

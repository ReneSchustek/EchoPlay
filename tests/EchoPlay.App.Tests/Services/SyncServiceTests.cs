using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.LocalLibrary.Models;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="SyncService"/>.
    /// Prüft das Matching-Verhalten und die korrekte Erstellung von LocalTracks.
    /// </summary>
    public sealed class SyncServiceTests
    {
        /// <summary>Einstellungen für <see cref="BuildService"/>; ohne Angabe gelten die Vorgaben.</summary>
        private sealed record SyncSetup
        {
            public FakeSeriesDataService? SeriesService { get; init; }
            public FakeEpisodeDataService? EpisodeService { get; init; }
            public FakeLocalTrackDataService? TrackService { get; init; }
            public FakeLocalLibraryScanner? Scanner { get; init; }
            public FakeTrackMatcher? TrackMatcher { get; init; }
            public FakeAudioMetadataReader? MetadataReader { get; init; }
            public FakeScanEventService? ScanEventService { get; init; }
        }

        private static SyncService BuildService(FakeAppSettingsDataService settingsService, SyncSetup? setup = null)
            => new SyncServiceSetup
            {
                Settings = settingsService,
                Series = setup?.SeriesService ?? new FakeSeriesDataService(),
                Episodes = setup?.EpisodeService ?? new FakeEpisodeDataService(),
                Tracks = setup?.TrackService ?? new FakeLocalTrackDataService(),
                Scanner = setup?.Scanner ?? new FakeLocalLibraryScanner([]),
                TrackMatcher = setup?.TrackMatcher ?? new FakeTrackMatcher(),
                MetadataReader = setup?.MetadataReader ?? new FakeAudioMetadataReader(),
                ScanEvents = setup?.ScanEventService ?? new FakeScanEventService(),
            }.Build();

        /// <summary>
        /// Einstellungen mit eingeschalteter Mediathek unter <c>/music</c>. Ohne Angabe bleibt
        /// <see cref="AppSettings.AutoImportAfterScan"/> auf seinem Vorgabewert.
        /// </summary>
        private static FakeAppSettingsDataService EnabledSettings(bool? autoImportAfterScan = null)
        {
            AppSettings settings = new()
            {
                LocalLibraryEnabled = true,
                LocalLibraryRootPath = "/music"
            };

            if (autoImportAfterScan is bool autoImport)
            {
                settings.AutoImportAfterScan = autoImport;
            }

            return new FakeAppSettingsDataService(settings);
        }

        /// <summary>Datenbestand mit genau einer Serie des angegebenen Titels.</summary>
        private static async Task<FakeSeriesDataService> SeriesWithAsync(string title)
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = title }, cancellationToken: TestContext.Current.CancellationToken);
            return seriesService;
        }

        /// <summary>Legt die Folge mit Nummer und dem Titel „Folge n“ zur Serie an.</summary>
        private static Task AddEpisodeAsync(FakeEpisodeDataService episodeService, Series series, int number)
            => episodeService.AddAsync(
                new Episode { SeriesId = series.Id, Title = $"Folge {number}", EpisodeNumber = number },
                cancellationToken: TestContext.Current.CancellationToken);

        /// <summary>Scan-Ergebnis mit einem Serienordner und seinen Folgenordnern.</summary>
        private static IReadOnlyList<LocalScanResult> ScanOf(
            string seriesName, string seriesFolder, params IReadOnlyList<LocalEpisodeScan> episodes)
            =>
            [
                new LocalScanResult
                {
                    SeriesName = seriesName,
                    SeriesFolderPath = seriesFolder,
                    Episodes = episodes
                }
            ];

        /// <summary>Ein Folgenordner mit erkannter (oder fehlender) Nummer und seinen Spuren.</summary>
        private static LocalEpisodeScan EpisodeScan(string folder, int? number, params string[] tracks)
            => new() { FolderPath = folder, ParsedNumber = number, TrackPaths = tracks };

        /// <summary>Folgenordner 001 bis <paramref name="count"/> einer neuen Serie, je mit den Spuren.</summary>
        private static List<LocalEpisodeScan> NumberedEpisodes(int count, params string[] trackNames)
        {
            List<LocalEpisodeScan> episodes = new(count);
            for (int i = 0; i < count; i++)
            {
                string folder = $"/music/Neue Serie/{i + 1:000}";
                episodes.Add(EpisodeScan(folder, i + 1, [.. trackNames.Select(name => $"{folder}/{name}")]));
            }

            return episodes;
        }

        [Fact]
        public async Task SyncAsync_ReturnsEmpty_WhenLibraryDisabled()
        {
            // Kein Scan, wenn LocalLibraryEnabled = false
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled = false,
                LocalLibraryRootPath = "/music"
            });

            SyncService service = BuildService(settings, new()
            {
                SeriesService = new FakeSeriesDataService(),
                EpisodeService = new FakeEpisodeDataService(),
                TrackService = new FakeLocalTrackDataService(),
                Scanner = new FakeLocalLibraryScanner([]),
                TrackMatcher = new FakeTrackMatcher(),
                MetadataReader = new FakeAudioMetadataReader(),
            });

            SyncResult result = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

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
                LocalLibraryEnabled = true,
                LocalLibraryRootPath = null
            });

            SyncService service = BuildService(settings, new()
            {
                SeriesService = new FakeSeriesDataService(),
                EpisodeService = new FakeEpisodeDataService(),
                TrackService = new FakeLocalTrackDataService(),
                Scanner = new FakeLocalLibraryScanner([]),
                TrackMatcher = new FakeTrackMatcher(),
                MetadataReader = new FakeAudioMetadataReader(),
            });

            SyncResult result = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, result.SeriesMatched);
        }

        [Fact]
        public async Task SyncAsync_MatchesSeries_ByNormalizedName()
        {
            // Seriennamen mit unterschiedlicher Schreibweise werden über Normalisierung gematcht
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled = true,
                LocalLibraryRootPath = "/music"
            });

            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "Die Drei ???" }, cancellationToken: TestContext.Current.CancellationToken);

            IReadOnlyList<LocalScanResult> scanResults =
            [
                new LocalScanResult
                {
                    SeriesName       = "die drei ???",  // Andere Schreibweise
                    SeriesFolderPath = "/music/die drei ???",
                    Episodes         = []
                }
            ];

            SyncService service = BuildService(settings, new()
            {
                SeriesService = seriesService,
                EpisodeService = new FakeEpisodeDataService(),
                TrackService = new FakeLocalTrackDataService(),
                Scanner = new FakeLocalLibraryScanner(scanResults),
                TrackMatcher = new FakeTrackMatcher(),
                MetadataReader = new FakeAudioMetadataReader(),
            });

            SyncResult result = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, result.SeriesMatched);
        }

        [Fact]
        public async Task SyncAsync_SkipsUnmatchedSeries_CountsAsUnmatched()
        {
            // Lokale Ordner ohne passende DB-Serie werden als "unmatched" gezählt;
            // AutoImportAfterScan muss deaktiviert sein, damit kein Eintrag angelegt wird.
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled = true,
                LocalLibraryRootPath = "/music",
                AutoImportAfterScan = false
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

            SyncService service = BuildService(settings, new()
            {
                SeriesService = new FakeSeriesDataService(), // leere DB
                EpisodeService = new FakeEpisodeDataService(),
                TrackService = new FakeLocalTrackDataService(),
                Scanner = new FakeLocalLibraryScanner(scanResults),
                TrackMatcher = new FakeTrackMatcher(),
                MetadataReader = new FakeAudioMetadataReader(),
            });

            SyncResult result = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, result.SeriesMatched);
            Assert.Equal(1, result.SeriesUnmatched);
        }

        [Fact]
        public async Task SyncAsync_UpdatesEpisode_WhenNumberMatches()
        {
            // Lokale Episode wird aktualisiert, wenn die Episodennummer übereinstimmt
            FakeSeriesDataService seriesService = await SeriesWithAsync("TKKG");
            FakeEpisodeDataService episodeService = new();
            await AddEpisodeAsync(episodeService, seriesService.All[0], 1);

            IReadOnlyList<LocalScanResult> scanResults = ScanOf("TKKG", "/music/TKKG",
                EpisodeScan("/music/TKKG/001 - Folge 1", 1, "/music/TKKG/001 - Folge 1/track1.mp3"));

            SyncService service = BuildService(EnabledSettings(), new()
            {
                SeriesService = seriesService,
                EpisodeService = episodeService,
                Scanner = new FakeLocalLibraryScanner(scanResults),
            });

            SyncResult result = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, result.EpisodesUpdated);
        }

        [Fact]
        public async Task SyncAsync_CreatesLocalTracks_ForMatchedEpisode()
        {
            // Für jede gematchte Episode werden LocalTracks gespeichert
            FakeSeriesDataService seriesService = await SeriesWithAsync("Die drei ???");
            FakeEpisodeDataService episodeService = new();
            await AddEpisodeAsync(episodeService, seriesService.All[0], 1);
            Episode episode = episodeService.All[0];

            IReadOnlyList<LocalScanResult> scanResults = ScanOf("Die drei ???", "/music/Die drei ???",
                EpisodeScan("/music/Die drei ???/001", 1, "/music/001/track1.mp3", "/music/001/track2.mp3"));

            FakeLocalTrackDataService trackService = new();
            SyncService service = BuildService(EnabledSettings(), new()
            {
                SeriesService = seriesService,
                EpisodeService = episodeService,
                TrackService = trackService,
                Scanner = new FakeLocalLibraryScanner(scanResults),
            });

            _ = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(trackService.SavedTracks.ContainsKey(episode.Id));
            Assert.Equal(2, trackService.SavedTracks[episode.Id].Count);
        }

        [Fact]
        public async Task SyncAsync_CountsCorrectly_AllCounters()
        {
            // Alle Zähler müssen nach einem erfolgreichen Sync korrekt befüllt sein;
            // AutoImportAfterScan deaktiviert, damit die unbekannte Serie als "unmatched" gezählt wird.
            FakeSeriesDataService seriesService = await SeriesWithAsync("TKKG");
            FakeEpisodeDataService episodeService = new();
            await AddEpisodeAsync(episodeService, seriesService.All[0], 1);
            await AddEpisodeAsync(episodeService, seriesService.All[0], 2);

            IReadOnlyList<LocalScanResult> scanResults =
            [
                .. ScanOf("TKKG", "/music/TKKG",
                    EpisodeScan("/music/TKKG/001", 1, "/music/TKKG/001/t1.mp3"),
                    EpisodeScan("/music/TKKG/002", 2, "/music/TKKG/002/t1.mp3", "/music/TKKG/002/t2.mp3")),
                .. ScanOf("Unbekannte Serie", "/music/Unbekannte Serie"),
            ];

            SyncService service = BuildService(EnabledSettings(autoImportAfterScan: false), new()
            {
                SeriesService = seriesService,
                EpisodeService = episodeService,
                Scanner = new FakeLocalLibraryScanner(scanResults),
            });

            SyncResult result = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, result.SeriesMatched);
            Assert.Equal(1, result.SeriesUnmatched);
            Assert.Equal(2, result.EpisodesUpdated);
            Assert.Equal(3, result.TracksCreated);  // 1 + 2 Tracks
        }

        [Fact]
        public async Task SyncAsync_AutoImport_TenEpisodes_TriggersSingleAddRangeCall()
        {
            // Auto-Import einer neuen lokalen Serie mit 10 Folgen muss einen einzigen
            // AddRangeAsync-Aufruf für alle Episoden absetzen, danach pro Episode einen
            // Track-Batch (das bleibt pro Episode, weil Tracks pro Folge variieren).
            FakeEpisodeDataService episodeService = new();
            FakeLocalTrackDataService trackService = new();

            const int episodeCount = 10;
            IReadOnlyList<LocalScanResult> scanResults = ScanOf("Neue Serie", "/music/Neue Serie",
                NumberedEpisodes(episodeCount, "track1.mp3", "track2.mp3"));

            SyncService service = BuildService(EnabledSettings(autoImportAfterScan: true), new()
            {
                EpisodeService = episodeService,
                TrackService = trackService,
                Scanner = new FakeLocalLibraryScanner(scanResults),
            });

            SyncResult result = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, result.SeriesMatched);
            Assert.Equal(episodeCount, result.EpisodesUpdated);
            // Genau ein AddRangeAsync für alle Episoden, kein einzelnes AddAsync.
            Assert.Equal(1, episodeService.AddRangeAsyncCallCount);
            Assert.Equal(0, episodeService.AddAsyncCallCount);
            // Track-Batches: pro Episode genau ein SaveTracksForEpisodeAsync-Aufruf.
            Assert.Equal(episodeCount, trackService.SavedTracks.Count);
        }

        [Fact]
        public async Task SyncAsync_RaisesSeriesSynced_AfterEpisodesPersisted()
        {
            // Meldete SeriesSynced nur in Phase 3, also vor dem Speichern der Episoden, bliebe die
            // Kachel einer neu gescannten Serie auf "0 / 0" stehen. Die Meldung muss (auch) nach
            // Phase 4 kommen, dann sieht der Kachel-Handler die echten Zähler.
            FakeEpisodeDataService episodeService = new();

            const int episodeCount = 5;
            IReadOnlyList<LocalScanResult> scanResults = ScanOf("Neue Serie", "/music/Neue Serie",
                NumberedEpisodes(episodeCount, "track1.mp3"));

            FakeScanEventService scanEvents = new();

            // Zähler, den der Kachel-Callback zum Zeitpunkt jedes Emits sehen würde.
            int maxEpisodesSeenAtEmit = 0;
            int emitCount = 0;
            scanEvents.SeriesSynced += s =>
            {
                emitCount++;

                // Der Event ist synchron, kein sync-over-async hier. Der Fake hält die
                // Folgen im Speicher, die direkte Sicht darauf reicht für die Messung.
                int seen = episodeService.All.Count(e => e.SeriesId == s.Id);
                maxEpisodesSeenAtEmit = Math.Max(maxEpisodesSeenAtEmit, seen);
            };

            SyncService service = BuildService(EnabledSettings(autoImportAfterScan: true), new()
            {
                EpisodeService = episodeService,
                Scanner = new FakeLocalLibraryScanner(scanResults),
                ScanEventService = scanEvents,
            });

            _ = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(emitCount >= 1, "Serie wurde nie über SeriesSynced gemeldet.");
            // Der Kern: Zum Zeitpunkt (mindestens) einer Meldung müssen alle Episoden
            // bereits persistiert sein, sonst zeigt die Kachel dauerhaft 0.
            Assert.Equal(episodeCount, maxEpisodesSeenAtEmit);
        }

        [Fact]
        public async Task SyncAsync_NewFolderWithoutNumberInKnownSeries_IsCreatedAsSpecialEpisode()
        {
            // Ein neuer Ordner ohne erkennbare Nummer in einer bekannten Serie ist eine
            // Sonderfolge: Er wird angelegt, aber ohne Nummer, damit er keiner regulären Folge
            // die Nummer nimmt.
            FakeSeriesDataService seriesService = await SeriesWithAsync("TKKG");

            // Keine Nummer erkannt
            IReadOnlyList<LocalScanResult> scanResults = ScanOf("TKKG", "/music/TKKG",
                EpisodeScan("/music/TKKG/unbekannt", null, "/music/TKKG/unbekannt/track.mp3"));

            FakeEpisodeDataService episodeService = new();
            SyncService service = BuildService(EnabledSettings(), new()
            {
                SeriesService = seriesService,
                EpisodeService = episodeService,
                Scanner = new FakeLocalLibraryScanner(scanResults),
            });

            SyncResult result = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, result.SeriesMatched);
            Assert.Equal(1, result.EpisodesUpdated);
            Episode created = Assert.Single(episodeService.All);
            Assert.Null(created.EpisodeNumber);
            Assert.Equal("/music/TKKG/unbekannt", created.LocalFolderPath);
        }

        [Fact]
        public async Task SyncAsync_EpisodeOnTwoCds_NumbersTracksInFileOrder()
        {
            // Jede CD beginnt im Tag wieder bei Spur 1; die Wiedergabe sortiert nach Spurnummer.
            FakeSeriesDataService seriesService = await SeriesWithAsync("TKKG");

            IReadOnlyList<LocalScanResult> scanResults = ScanOf("TKKG", "/music/TKKG",
                EpisodeScan("/music/TKKG/100", 100,
                    "/music/TKKG/100/CD1/01.mp3", "/music/TKKG/100/CD1/02.mp3", "/music/TKKG/100/CD2/01.mp3"));

            FakeEpisodeDataService episodeService = new();
            FakeLocalTrackDataService trackService = new();
            SyncService service = BuildService(EnabledSettings(), new()
            {
                SeriesService = seriesService,
                EpisodeService = episodeService,
                TrackService = trackService,
                Scanner = new FakeLocalLibraryScanner(scanResults),
                MetadataReader = new FakeAudioMetadataReader(defaultTrackNumber: 1),
            });

            _ = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            IReadOnlyList<LocalTrack> saved = trackService.SavedTracks[Assert.Single(episodeService.All).Id];
            Assert.Equal([1, 2, 3], saved.Select(track => track.TrackNumber));
        }

        [Fact]
        public async Task RunDetectionPhase_NewFolders_ReturnsCorrectCount()
        {
            // Detection-Phase: 3 Ordner im Root, davon 1 in der DB bekannt -> alle 3
            // landen in DetectionResult.SeriesFolders, der bekannte wird via onSeriesSynced gemeldet.
            FakeAppSettingsDataService settings = new(new AppSettings
            {
                LocalLibraryEnabled = true,
                LocalLibraryRootPath = "/music",
                AutoImportAfterScan = false
            });
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG", LocalFolderPath = "/music/TKKG" }, cancellationToken: TestContext.Current.CancellationToken);

            FakeLocalLibraryScanner scanner = new([], ["/music/TKKG", "/music/Neu1", "/music/Neu2"]);

            SyncService service = BuildService(settings, new()
            {
                SeriesService = seriesService,
                EpisodeService = new FakeEpisodeDataService(),
                TrackService = new FakeLocalTrackDataService(),
                Scanner = scanner,
                TrackMatcher = new FakeTrackMatcher(),
                MetadataReader = new FakeAudioMetadataReader(),
            });

            // Kein Progress<T>: das stellt seine Rückrufe über den ThreadPool zu, wodurch der
            // Test unter Last am Zeitverhalten kippte statt an der Sache.
            SignalingProgress<Series> progress = new();

            _ = await service.SyncAsync(onSeriesSynced: progress, cancellationToken: TestContext.Current.CancellationToken);

            // Bekannte Serie wird in der Detection-Phase und (mit aktualisiertem Pfad) erneut
            // in der Materialize-Phase gemeldet, d.h. mindestens 1 Aufruf erwartet.
            Assert.True(progress.Count >= 1, $"onSeriesSynced wurde {progress.Count}-mal aufgerufen, erwartet >= 1");
        }

        [Fact]
        public async Task MaterializeSeries_TitleMatchesExisting_AssignsExistingId()
        {
            // Materialize-Series: scanResult.SeriesName matcht bestehende DB-Serie nach
            // Normalizer-Vergleich -> die existierende Series-Id wird an Phase 4 weitergereicht,
            // KEINE neue Serie wird angelegt.
            FakeSeriesDataService seriesService = await SeriesWithAsync("Die drei ???");
            Guid existingId = seriesService.All[0].Id;
            int seriesCountBefore = seriesService.All.Count;

            IReadOnlyList<LocalScanResult> scanResults = ScanOf("Die drei ???", "/music/Die drei ???");

            SyncService service = BuildService(EnabledSettings(), new()
            {
                SeriesService = seriesService,
                Scanner = new FakeLocalLibraryScanner(scanResults),
            });

            SyncResult result = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, result.SeriesMatched);
            // Keine neue Serie angelegt, der Count unverändert.
            Assert.Equal(seriesCountBefore, seriesService.All.Count);
            // Die bestehende Serie behält ihre Id.
            Assert.Equal(existingId, seriesService.All[0].Id);
            // LocalFolderPath wurde aktualisiert.
            Assert.Equal("/music/Die drei ???", seriesService.All[0].LocalFolderPath);
        }

        [Fact]
        public async Task MaterializeEpisodes_NewEpisodes_PersistsViaAddRange()
        {
            // Materialize-Episodes: bei Auto-Import einer neuen Serie geht jeder Episodenblock
            // über AddRangeAsync (1 SaveChanges für N Episoden), nicht einzeln per AddAsync.
            FakeEpisodeDataService episodeService = new();

            IReadOnlyList<LocalScanResult> scanResults = ScanOf("Brand-neue Serie", "/music/Brand-neue Serie",
                EpisodeScan("/music/Neu/001", 1, "/music/Neu/001/t.mp3"),
                EpisodeScan("/music/Neu/002", 2, "/music/Neu/002/t.mp3"),
                EpisodeScan("/music/Neu/003", 3, "/music/Neu/003/t.mp3"));

            SyncService service = BuildService(EnabledSettings(autoImportAfterScan: true), new()
            {
                EpisodeService = episodeService,
                Scanner = new FakeLocalLibraryScanner(scanResults),
            });

            SyncResult result = await service.SyncAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, result.EpisodesUpdated);
            // Genau 1 AddRangeAsync, KEIN einzelner AddAsync (N+1-Vermeidung beim Auto-Import).
            Assert.Equal(1, episodeService.AddRangeAsyncCallCount);
            Assert.Equal(0, episodeService.AddAsyncCallCount);
        }
    }
}

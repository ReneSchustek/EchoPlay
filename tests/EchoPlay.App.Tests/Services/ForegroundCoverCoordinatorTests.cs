using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft den Vorrang beim Cover-Laden: was am Hintergrundlauf vorbei darf, weil der
    /// Anwender davor sitzt und zusieht.
    /// </summary>
    /// <remarks>
    /// Zwei Zusagen tragen diese Klasse. Erstens die Reihenfolge der Quellen — was schon in
    /// der Ablage liegt, wird nicht erneut geholt, und die Platte kommt vor dem Netz.
    /// Zweitens der Zähler, an dem der Hintergrundlauf erkennt, dass er pausieren soll.
    /// </remarks>
    public sealed class ForegroundCoverCoordinatorTests
    {
        private const string CoverUrl = "https://i.example.com/cover.jpg";
        private const string SpotifyArtistId = "4tZwfgrHOc3mvqYlEYSvVi";
        private static readonly byte[] DownloadedBytes = [0x11, 0x22];
        private static readonly byte[] StoredBytes = [0x33, 0x44];

        [Fact]
        public async Task RequestCoverForSearchResult_WithoutUrl_DoesNotDownload()
        {
            FakeCoverDownloader downloader = new();
            ForegroundCoverCoordinator coordinator = BuildCoordinator(downloader: downloader);

            byte[]? result = await coordinator.RequestCoverForSearchResultAsync(
                ProviderKeys.Spotify, SpotifyArtistId, string.Empty, TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task RequestCoverForSearchResult_WithMalformedUrl_DoesNotDownload()
        {
            FakeCoverDownloader downloader = new();
            ForegroundCoverCoordinator coordinator = BuildCoordinator(downloader: downloader);

            byte[]? result = await coordinator.RequestCoverForSearchResultAsync(
                ProviderKeys.Spotify, SpotifyArtistId, "nicht-als-adresse-lesbar", TestContext.Current.CancellationToken);

            // Eine Adresse, die sich nicht auflösen lässt, ist kein Grund für einen Versuch.
            Assert.Null(result);
            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task RequestCoverForSearchResult_WhenSeriesAlreadyImported_UsesStoredCover()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Bereits importiert", SpotifyArtistId = SpotifyArtistId };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeCoverImageDataService coverImages = new();
            await coverImages.SetCoverAsync(
                CoverEntityTypes.Series, series.Id, StoredBytes, cancellationToken: TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new(new Dictionary<string, byte[]?> { [CoverUrl] = DownloadedBytes });
            ForegroundCoverCoordinator coordinator = BuildCoordinator(
                seriesService: seriesService, coverImages: coverImages, downloader: downloader);

            byte[]? result = await coordinator.RequestCoverForSearchResultAsync(
                ProviderKeys.Spotify, SpotifyArtistId, CoverUrl, TestContext.Current.CancellationToken);

            // Steht die Serie schon in der Mediathek, ist ihr Bild bereits bezahlt. Ein
            // erneuter Abruf kostet Wartekontingent beim Anbieter für nichts.
            Assert.Equal(StoredBytes, result);
            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task RequestCoverForSearchResult_WhenSeriesUnknown_DownloadsFromProvider()
        {
            FakeCoverDownloader downloader = new(new Dictionary<string, byte[]?> { [CoverUrl] = DownloadedBytes });
            ForegroundCoverCoordinator coordinator = BuildCoordinator(downloader: downloader);

            byte[]? result = await coordinator.RequestCoverForSearchResultAsync(
                ProviderKeys.Spotify, SpotifyArtistId, CoverUrl, TestContext.Current.CancellationToken);

            Assert.Equal(DownloadedBytes, result);
            Assert.Equal([CoverUrl], downloader.RequestedUrls);
        }

        [Fact]
        public async Task RequestCoverForSearchResult_AsksTheRateLimiterForTheHostWithPriority()
        {
            RecordingHostRateLimiter rateLimiter = new();
            FakeCoverDownloader downloader = new(new Dictionary<string, byte[]?> { [CoverUrl] = DownloadedBytes });
            ForegroundCoverCoordinator coordinator = BuildCoordinator(downloader: downloader, rateLimiter: rateLimiter);

            _ = await coordinator.RequestCoverForSearchResultAsync(
                ProviderKeys.Spotify, SpotifyArtistId, CoverUrl, TestContext.Current.CancellationToken);

            // Die Suche läuft, während der Anwender zusieht — sie muss sich beim Wartelimit
            // als Vordergrund melden, sonst steht sie hinter dem Hintergrundlauf an.
            (string host, CoverFetchPriority priority) = Assert.Single(rateLimiter.Waits);
            Assert.Equal("i.example.com", host);
            Assert.Equal(CoverFetchPriority.Foreground, priority);
        }

        [Fact]
        public async Task RequestCoverForSearchResult_WithUnknownProvider_SkipsTheLookupAndDownloads()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Bereits importiert", SpotifyArtistId = SpotifyArtistId };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new(new Dictionary<string, byte[]?> { [CoverUrl] = DownloadedBytes });
            ForegroundCoverCoordinator coordinator = BuildCoordinator(
                seriesService: seriesService, downloader: downloader);

            byte[]? result = await coordinator.RequestCoverForSearchResultAsync(
                "EinAnbieterDenEsNichtGibt", SpotifyArtistId, CoverUrl, TestContext.Current.CancellationToken);

            // Bei unbekannter Quelle lässt sich die Serie nicht zuordnen. Dann wird geladen,
            // statt stillschweigend das Bild einer fremden Serie zu liefern.
            Assert.Equal(DownloadedBytes, result);
            Assert.Equal([CoverUrl], downloader.RequestedUrls);
        }

        [Fact]
        public async Task RequestPriorityForSeries_WithEmptyId_DoesNothing()
        {
            FakeEpisodeDataService episodeService = new();
            ForegroundCoverCoordinator coordinator = BuildCoordinator(episodeService: episodeService);

            await coordinator.RequestPriorityForSeriesAsync(Guid.Empty, TestContext.Current.CancellationToken);

            Assert.False(coordinator.IsActive);
            Assert.Equal(0, episodeService.GetBySeriesIdAsyncCallCount);
        }

        [Fact]
        public async Task RequestPriorityForSeries_LoadsCoversFromDiskForEpisodesThatLackOne()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Die drei Fragezeichen" };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            const string episodeFolder = @"D:\Media\Folge 001";
            FakeEpisodeDataService episodeService = new();
            Episode episode = new() { SeriesId = series.Id, Title = "Folge 001", LocalFolderPath = episodeFolder };
            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);

            ConfigurableLocalCoverLoader loader = new(new Dictionary<string, byte[]> { [episodeFolder] = StoredBytes });
            FakeCoverService coverService = new();
            ForegroundCoverCoordinator coordinator = BuildCoordinator(
                seriesService: seriesService,
                episodeService: episodeService,
                coverLoader: loader,
                coverService: coverService);

            await coordinator.RequestPriorityForSeriesAsync(series.Id, TestContext.Current.CancellationToken);

            Assert.Equal([episode.Id], coverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task RequestPriorityForSeries_WhenCoverAlreadyStored_ReadsNothingFromDisk()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Die drei Fragezeichen" };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            const string episodeFolder = @"D:\Media\Folge 001";
            FakeEpisodeDataService episodeService = new();
            Episode episode = new() { SeriesId = series.Id, Title = "Folge 001", LocalFolderPath = episodeFolder };
            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);

            FakeCoverImageDataService coverImages = new();
            await coverImages.SetCoverAsync(
                CoverEntityTypes.Episode, episode.Id, StoredBytes, cancellationToken: TestContext.Current.CancellationToken);

            ConfigurableLocalCoverLoader loader = new(new Dictionary<string, byte[]> { [episodeFolder] = StoredBytes });
            ForegroundCoverCoordinator coordinator = BuildCoordinator(
                seriesService: seriesService,
                episodeService: episodeService,
                coverImages: coverImages,
                coverLoader: loader);

            await coordinator.RequestPriorityForSeriesAsync(series.Id, TestContext.Current.CancellationToken);

            Assert.Empty(loader.Calls);
        }

        [Fact]
        public async Task RequestPriorityForSeries_WhenDone_LeavesNoActiveClaim()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Die drei Fragezeichen" };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            ForegroundCoverCoordinator coordinator = BuildCoordinator(seriesService: seriesService);

            await coordinator.RequestPriorityForSeriesAsync(series.Id, TestContext.Current.CancellationToken);

            // Bleibt der Zähler stehen, pausiert der Hintergrundlauf für immer und die
            // Mediathek bekommt nie wieder ein Cover nachgeladen.
            Assert.False(coordinator.IsActive);
        }

        [Fact]
        public void EnqueueForEpisodes_WithEmptyList_DoesNothing()
        {
            FakeCoverService coverService = new();
            ForegroundCoverCoordinator coordinator = BuildCoordinator(coverService: coverService);

            coordinator.EnqueueForEpisodes([], onCoverReady: null);

            // Eine leere Warteschlange darf keinen Hintergrundlauf anwerfen — sonst zahlt
            // das Dashboard bei jedem Aufbau für nichts.
            Assert.False(coordinator.IsActive);
        }

        [Fact]
        public async Task EnqueueForEpisodes_WithStoredCover_ReturnsItWithoutLoadingFromDisk()
        {
            Guid episodeId = new("dddddddd-1111-2222-3333-777777777777");

            FakeCoverService coverService = new();
            coverService.ExistingEpisodeCovers[episodeId] = StoredBytes;

            ConfigurableLocalCoverLoader loader = new();
            ForegroundCoverCoordinator coordinator = BuildCoordinator(coverService: coverService, coverLoader: loader);

            TaskCompletionSource<byte[]> delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            coordinator.EnqueueForEpisodes([episodeId], (episode, bytes) => _ = delivered.TrySetResult(bytes));

            byte[] result = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            // Was in der Ablage liegt, geht sofort an die Kachel. Ein Griff zur Platte wäre
            // hier verschwendet — das Dashboard baut sich bei jedem Öffnen neu auf.
            Assert.Equal(StoredBytes, result);
            Assert.Empty(loader.Calls);
        }

        [Fact]
        public async Task EnqueueForEpisodes_WithDuplicateIds_ReportsEachEpisodeOnce()
        {
            Guid episodeId = new("dddddddd-1111-2222-3333-888888888888");

            FakeCoverService coverService = new();
            coverService.ExistingEpisodeCovers[episodeId] = StoredBytes;

            ForegroundCoverCoordinator coordinator = BuildCoordinator(coverService: coverService);

            List<Guid> reported = [];
            TaskCompletionSource delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            coordinator.EnqueueForEpisodes(
                [episodeId, episodeId, episodeId],
                (id, bytes) =>
                {
                    lock (reported)
                    {
                        reported.Add(id);
                    }

                    _ = delivered.TrySetResult();
                });

            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            // Dieselbe Folge steht im Dashboard mehrfach — als laufende Folge und als
            // Neuerscheinung. Ohne Entdopplung liefe die Arbeit dreifach.
            lock (reported)
            {
                _ = Assert.Single(reported);
            }
        }

        [Fact]
        public async Task WaitWhileInFlight_WithNothingRunning_ReturnsImmediately()
        {
            ForegroundCoverCoordinator coordinator = BuildCoordinator();

            await coordinator.WaitWhileInFlightAsync(TestContext.Current.CancellationToken);

            Assert.False(coordinator.IsActive);
        }

        [Fact]
        public async Task EnqueueForEpisodes_OhneAblage_HoltDasCoverVonDerPlatte()
        {
            // Reihenfolge der Quellen: erst die Ablage, dann die Platte. Was von der Platte
            // kommt, wandert in die Ablage — beim nächsten Aufbau fällt der Griff weg.

            FakeEpisodeDataService episodes = new();
            Episode episode = new()
            {
                Title = "Folge 1",
                SeriesId = TestIds.SeriesA,
                LocalFolderPath = @"D:\Media\TKKG\001"
            };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);
            Guid episodeId = episode.Id;

            ConfigurableLocalCoverLoader loader = new(new Dictionary<string, byte[]>
            {
                [@"D:\Media\TKKG\001"] = StoredBytes
            });
            FakeCoverService coverService = new();

            ForegroundCoverCoordinator coordinator = BuildCoordinator(
                episodeService: episodes, coverLoader: loader, coverService: coverService);

            TaskCompletionSource<byte[]> delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            coordinator.EnqueueForEpisodes([episodeId], (id, bytes) => _ = delivered.TrySetResult(bytes));

            byte[] result = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Equal(StoredBytes, result);
            Assert.Equal([episodeId], coverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task EnqueueForEpisodes_OhneCoverAufDerPlatte_LaedtVomAnbieter()
        {
            FakeEpisodeDataService episodes = new();
            Episode episode = new()
            {
                Title = "Folge 2",
                SeriesId = TestIds.SeriesA,
                CoverImageUrl = "https://example.com/cover.jpg"
            };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);
            Guid episodeId = episode.Id;

            FakeCoverDownloader downloader = new();
            downloader.SetResponse("https://example.com/cover.jpg", DownloadedBytes);
            FakeCoverService coverService = new();

            ForegroundCoverCoordinator coordinator = BuildCoordinator(
                episodeService: episodes, coverService: coverService, downloader: downloader);

            TaskCompletionSource<byte[]> delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            coordinator.EnqueueForEpisodes([episodeId], (id, bytes) => _ = delivered.TrySetResult(bytes));

            byte[] result = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Equal(DownloadedBytes, result);
            Assert.Equal(["https://example.com/cover.jpg"], downloader.RequestedUrls);
            Assert.Equal([episodeId], coverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task EnqueueForEpisodes_OhneQuelleMeldetNichts()
        {
            // Eine Folge ohne Ordner und ohne Adresse hat keine Cover-Quelle. Die Kachel
            // behält ihren Platzhalter, und die übrigen Folgen laufen weiter durch.
            Guid mitAblage = new("dddddddd-1111-2222-3333-999999999994");

            FakeEpisodeDataService episodes = new();
            Episode episode = new() { Title = "Ohne Quelle", SeriesId = TestIds.SeriesA };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);
            Guid ohneQuelle = episode.Id;

            FakeCoverService coverService = new();
            coverService.ExistingEpisodeCovers[mitAblage] = StoredBytes;

            ForegroundCoverCoordinator coordinator = BuildCoordinator(
                episodeService: episodes, coverService: coverService);

            List<Guid> gemeldet = [];
            TaskCompletionSource delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            coordinator.EnqueueForEpisodes([ohneQuelle, mitAblage], (id, bytes) =>
            {
                lock (gemeldet)
                {
                    gemeldet.Add(id);
                }

                if (id == mitAblage)
                {
                    _ = delivered.TrySetResult();
                }
            });

            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            lock (gemeldet)
            {
                Assert.Equal([mitAblage], gemeldet);
            }
        }

        [Fact]
        public async Task EnqueueForEpisodes_UnbekannteFolgeWirdUebersprungen()
        {
            // Die Kachel kann aus einem Bestand stammen, der inzwischen gelöscht wurde.
            Guid unbekannt = new("dddddddd-1111-2222-3333-999999999995");
            Guid bekannt = new("dddddddd-1111-2222-3333-999999999996");

            FakeCoverService coverService = new();
            coverService.ExistingEpisodeCovers[bekannt] = StoredBytes;

            ForegroundCoverCoordinator coordinator = BuildCoordinator(coverService: coverService);

            TaskCompletionSource delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            List<Guid> gemeldet = [];
            coordinator.EnqueueForEpisodes([unbekannt, bekannt], (id, bytes) =>
            {
                lock (gemeldet)
                {
                    gemeldet.Add(id);
                }

                _ = delivered.TrySetResult();
            });

            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            lock (gemeldet)
            {
                Assert.Equal([bekannt], gemeldet);
            }
        }

        private static ForegroundCoverCoordinator BuildCoordinator(
            FakeSeriesDataService? seriesService = null,
            FakeEpisodeDataService? episodeService = null,
            FakeCoverImageDataService? coverImages = null,
            ConfigurableLocalCoverLoader? coverLoader = null,
            FakeCoverService? coverService = null,
            FakeCoverDownloader? downloader = null,
            RecordingHostRateLimiter? rateLimiter = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService ?? new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService ?? new FakeEpisodeDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => coverLoader ?? new ConfigurableLocalCoverLoader());
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImages ?? new FakeCoverImageDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            return new ForegroundCoverCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(),
                coverService ?? new FakeCoverService(),
                downloader ?? new FakeCoverDownloader(),
                rateLimiter,
                new FakeLogger());
        }
    }
}

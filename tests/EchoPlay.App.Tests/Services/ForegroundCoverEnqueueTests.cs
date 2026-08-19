using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft die Reihenfolge der Quellen beim Nachreichen einzelner Folgen-Cover:
    /// Ablage, dann Ordner, dann die Adresse des Anbieters.
    /// </summary>
    /// <remarks>
    /// Die Reihenfolge ist die ganze Zusage dieser Warteschlange. Griffe sie zuerst ins
    /// Netz, zahlte jede Kachel eine Anfrage, obwohl das Bild längst auf der Platte liegt.
    /// </remarks>
    public sealed class ForegroundCoverEnqueueTests
    {
        private const string EpisodeFolder = @"D:\Media\TKKG\Folge 001";
        private const string CoverUrl = "https://i.example.invalid/folge-1.jpg";
        private static readonly byte[] FolderBytes = [0x0A, 0x0B];
        private static readonly byte[] ProviderBytes = [0x0C, 0x0D];

        [Fact]
        public async Task EnqueueForEpisodes_WithoutStoredCover_TakesTheOneFromTheFolder()
        {
            FakeEpisodeDataService episodes = new();
            Episode episode = new() { Title = "Folge 1", LocalFolderPath = EpisodeFolder };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);

            ConfigurableLocalCoverLoader loader = new(new Dictionary<string, byte[]>
            {
                [EpisodeFolder] = FolderBytes,
            });
            FakeCoverDownloader downloader = new();
            downloader.SetResponse(CoverUrl, ProviderBytes);

            ForegroundCoverCoordinator sut = BuildCoordinator(episodes, loader, downloader);

            (Guid Id, byte[] Bytes) delivered = await EnqueueAndWaitAsync(sut, episode.Id);

            Assert.Equal(episode.Id, delivered.Id);
            Assert.Equal(FolderBytes, delivered.Bytes);
            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task EnqueueForEpisodes_WithoutCoverOnDisk_DownloadsFromTheProviderUrl()
        {
            FakeEpisodeDataService episodes = new();
            Episode episode = new()
            {
                Title = "Folge 1",
                LocalFolderPath = EpisodeFolder,
                CoverImageUrl = CoverUrl,
            };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);

            ConfigurableLocalCoverLoader loader = new();
            FakeCoverDownloader downloader = new();
            downloader.SetResponse(CoverUrl, ProviderBytes);

            ForegroundCoverCoordinator sut = BuildCoordinator(episodes, loader, downloader);

            (Guid Id, byte[] Bytes) delivered = await EnqueueAndWaitAsync(sut, episode.Id);

            Assert.Equal(ProviderBytes, delivered.Bytes);
            Assert.Equal([CoverUrl], downloader.RequestedUrls);
        }

        [Fact]
        public void EnqueueForEpisodes_WithoutIds_DoesNothing()
        {
            FakeCoverDownloader downloader = new();
            ForegroundCoverCoordinator sut = BuildCoordinator(
                new FakeEpisodeDataService(), new ConfigurableLocalCoverLoader(), downloader);

            sut.EnqueueForEpisodes([], (_, _) => { });

            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public void EnqueueForEpisodes_WithoutList_ThrowsArgumentNullException()
        {
            ForegroundCoverCoordinator sut = BuildCoordinator(
                new FakeEpisodeDataService(), new ConfigurableLocalCoverLoader(), new FakeCoverDownloader());

            _ = Assert.Throws<ArgumentNullException>(() => sut.EnqueueForEpisodes(null!, (_, _) => { }));
        }

        [Fact]
        public async Task EnqueueForEpisodes_WithForegroundPriority_DeliversTheCoverAsWell()
        {
            FakeEpisodeDataService episodes = new();
            Episode episode = new() { Title = "Folge 1", LocalFolderPath = EpisodeFolder };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);

            ConfigurableLocalCoverLoader loader = new(new Dictionary<string, byte[]>
            {
                [EpisodeFolder] = FolderBytes,
            });
            ForegroundCoverCoordinator sut = BuildCoordinator(episodes, loader, new FakeCoverDownloader());

            TaskCompletionSource<byte[]> delivered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.EnqueueForEpisodes(
                [episode.Id],
                (_, bytes) => delivered.TrySetResult(bytes),
                CoverFetchPriority.Foreground);

            // Der Vorrang hält den Hintergrundlauf an, solange die sichtbare Ansicht
            // wartet — geliefert werden muss trotzdem dasselbe.
            byte[] result = await delivered.Task.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(FolderBytes, result);
        }

        [Fact]
        public async Task RequestPriorityForSeries_WithACoverInTheFolder_StoresItWithoutTheNetwork()
        {
            FakeEpisodeDataService episodes = new();
            Episode episode = new() { SeriesId = SeriesId, Title = "Folge 1", LocalFolderPath = EpisodeFolder };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);

            ConfigurableLocalCoverLoader loader = new(new Dictionary<string, byte[]>
            {
                [EpisodeFolder] = FolderBytes,
            });
            FakeCoverDownloader downloader = new();
            FakeCoverService coverService = new();

            ForegroundCoverCoordinator sut = BuildCoordinator(
                episodes, loader, downloader, coverService);

            await sut.RequestPriorityForSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

            Assert.Contains(episode.Id, coverService.StoredEpisodeCovers);
            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task RequestPriorityForSeries_WhenTheDiskFails_StillTriesTheProvider()
        {
            FakeEpisodeDataService episodes = new();
            Episode episode = new()
            {
                SeriesId = SeriesId,
                Title = "Folge 1",
                LocalFolderPath = EpisodeFolder,
                CoverImageUrl = CoverUrl,
            };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();
            downloader.SetResponse(CoverUrl, ProviderBytes);

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => episodes);
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => new ThrowingCoverLoader());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            ServiceProvider provider = services.BuildServiceProvider();

            FakeCoverService coverService = new();
            ForegroundCoverCoordinator sut = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                coverService, downloader, rateLimiter: null, new FakeLogger());

            await sut.RequestPriorityForSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

            // Eine unlesbare Datei darf die Folge nicht um ihr Cover bringen — die Adresse
            // des Anbieters bleibt als zweite Quelle.
            Assert.Equal([CoverUrl], downloader.RequestedUrls);
            Assert.Contains(episode.Id, coverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task RequestPriorityForSeries_WhenTheProviderFails_FinishesWithoutThrowing()
        {
            FakeEpisodeDataService episodes = new();
            await episodes.AddAsync(
                new Episode { SeriesId = SeriesId, Title = "Folge 1", CoverImageUrl = CoverUrl },
                TestContext.Current.CancellationToken);

            FakeCoverService coverService = new();
            ForegroundCoverCoordinator sut = BuildCoordinator(
                episodes, new ConfigurableLocalCoverLoader(), new ThrowingCoverDownloader(), coverService);

            await sut.RequestPriorityForSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

            // Der Lauf hängt an der geöffneten Detailseite; ein Fehler einer einzelnen Folge
            // darf sie nicht reißen.
            Assert.Empty(coverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task EnqueueForEpisodes_WhenTheEpisodeStoreIsUnreachable_EndsWithoutThrowing()
        {
            SignallingLogger logger = new();
            ForegroundCoverCoordinator sut = BuildWithBrokenEpisodeStore(logger);

            sut.EnqueueForEpisodes([Guid.NewGuid()], (_, _) => { });

            // Die Warteschlange läuft im Hintergrund. Reißt sie ab, wäre die Ausnahme
            // nirgends zu greifen — sie muss im Lauf selbst enden und dort vermerkt werden.
            string warning = await logger.FirstWarning.Task.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Contains("EnqueueForEpisodes", warning, StringComparison.Ordinal);
        }

        [Fact]
        public async Task WaitWhileInFlight_WithARunningForegroundRequest_WaitsForItsEnd()
        {
            FakeEpisodeDataService episodes = new();
            Episode episode = new() { Title = "Folge 1", LocalFolderPath = EpisodeFolder };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);

            BlockingCoverLoader loader = new();
            ForegroundCoverCoordinator sut = BuildWithLoader(episodes, loader);

            sut.EnqueueForEpisodes([episode.Id], (_, _) => { }, CoverFetchPriority.Foreground);

            await loader.Entered.Task.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Task waiting = sut.WaitWhileInFlightAsync(TestContext.Current.CancellationToken);
            Assert.False(waiting.IsCompleted);

            // Der Hintergrundlauf darf erst weiterarbeiten, wenn die Anfrage der sichtbaren
            // Seite durch ist — sonst nimmt er ihr die Verbindung zum Anbieter weg.
            _ = loader.Release.TrySetResult([0x01]);

            await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        private static async Task<(Guid Id, byte[] Bytes)> EnqueueAndWaitAsync(
            ForegroundCoverCoordinator sut, Guid episodeId)
        {
            TaskCompletionSource<(Guid, byte[])> delivered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            sut.EnqueueForEpisodes([episodeId], (id, bytes) => delivered.TrySetResult((id, bytes)));

            return await delivered.Task.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        private static readonly Guid SeriesId = new("dddddddd-1111-2222-3333-000000000001");

        /// <summary>Cover-Lader, der jede Datei mit einem Lesefehler beantwortet.</summary>
        private sealed class ThrowingCoverLoader : ILocalCoverLoader
        {
            public Task<byte[]?> LoadAsync(string? episodeFolderPath, string? firstTrackPath)
                => Task.FromException<byte[]?>(new IOException("Datei nicht lesbar"));
        }

        /// <summary>Downloader, der jede Anfrage mit einem Netzfehler beantwortet.</summary>
        private sealed class ThrowingCoverDownloader : ICoverDownloader
        {
            public Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken = default)
                => Task.FromException<byte[]?>(new System.Net.Http.HttpRequestException("Nicht erreichbar"));
        }

        private static ForegroundCoverCoordinator BuildCoordinator(
            FakeEpisodeDataService episodes,
            ConfigurableLocalCoverLoader loader,
            ICoverDownloader downloader,
            FakeCoverService? coverService = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => episodes);
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => loader);
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            return new ForegroundCoverCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(),
                coverService ?? new FakeCoverService(),
                downloader,
                rateLimiter: null,
                new FakeLogger());
        }
        /// <summary>Protokollkanal, der die erste Warnung meldet.</summary>
        private sealed class SignallingLogger : EchoPlay.Logger.Abstractions.ILogger
        {
            public TaskCompletionSource<string> FirstWarning { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool IsDebugEnabled => false;

            public void Trace(string message) { }

            public void Debug(string message) { }

            public void Info(string message) { }

            public void Warning(string message) => FirstWarning.TrySetResult(message);

            public void Error(string message, Exception? exception = null) { }

            public void Fatal(string message, Exception? exception = null) { }

            public EchoPlay.Logger.Scoping.LogScope BeginScope(string name) => new(name);
        }

        /// <summary>Cover-Lader, der beim Eintritt meldet und erst auf Freigabe antwortet.</summary>
        private sealed class BlockingCoverLoader : ILocalCoverLoader
        {
            public TaskCompletionSource Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<byte[]?> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<byte[]?> LoadAsync(string? episodeFolderPath, string? firstTrackPath)
            {
                _ = Entered.TrySetResult();
                return Release.Task;
            }
        }

        private static ForegroundCoverCoordinator BuildWithBrokenEpisodeStore(
            EchoPlay.Logger.Abstractions.ILogger logger)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(
                _ => throw new IOException("Datenbankdatei nicht erreichbar"));
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => new FakeLocalCoverLoader());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            return new ForegroundCoverCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeCoverService(),
                new FakeCoverDownloader(),
                rateLimiter: null,
                logger);
        }

        private static ForegroundCoverCoordinator BuildWithLoader(
            FakeEpisodeDataService episodes, ILocalCoverLoader loader)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => episodes);
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => loader);
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            return new ForegroundCoverCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeCoverService(),
                new FakeCoverDownloader(),
                rateLimiter: null,
                new FakeLogger());
        }
    }
}

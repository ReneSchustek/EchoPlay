using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using AppCoverService = EchoPlay.App.Services.CoverService;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für die Foreground-Priorisierung des <see cref="BackgroundCoverService"/>.
    /// Prüft, dass ein priorisierter Serien-Aufruf ausschließlich die Folgen der
    /// angeforderten Serie bearbeitet und den Hintergrund-Loop nicht blockiert.
    /// </summary>
    public sealed class BackgroundCoverServiceTests
    {
        private static readonly byte[] CoverBytes = [0x01, 0x02, 0x03, 0x04];

        [Fact]
        public async Task RequestPriorityForSeries_SkipsOtherSeries()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "Target", LocalFolderPath = "C:/target" });
            await seriesService.AddAsync(new Series { Title = "Other", LocalFolderPath = "C:/other" });

            Series targetSeries = seriesService.All[0];
            Series otherSeries = seriesService.All[1];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode
            {
                SeriesId = targetSeries.Id,
                Title = "Target 1",
                EpisodeNumber = 1,
                LocalFolderPath = "C:/target/1"
            });
            await episodeService.AddAsync(new Episode
            {
                SeriesId = otherSeries.Id,
                Title = "Other 1",
                EpisodeNumber = 1,
                LocalFolderPath = "C:/other/1"
            });

            FakeCoverImageDataService coverImageService = new();
            RecordingLocalCoverLoader coverLoader = new(CoverBytes);

            BackgroundCoverService service = BuildService(
                seriesService, episodeService, coverImageService, coverLoader);

            await service.RequestPriorityForSeriesAsync(targetSeries.Id, CancellationToken.None);

            Episode targetEpisode = episodeService.All[0];
            Episode otherEpisode = episodeService.All[1];

            Assert.True(await coverImageService.ExistsAsync(CoverEntityTypes.Episode, targetEpisode.Id));
            Assert.False(await coverImageService.ExistsAsync(CoverEntityTypes.Episode, otherEpisode.Id));
            _ = Assert.Single(coverLoader.LoadedFolders);
            Assert.Equal("C:/target/1", coverLoader.LoadedFolders[0]);
        }

        [Fact]
        public async Task RequestPriorityForSeries_WhenCancelled_SwallowsOperationCanceledException()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "Canceled", LocalFolderPath = "C:/canceled" });
            Series series = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            for (int i = 0; i < 6; i++)
            {
                await episodeService.AddAsync(new Episode
                {
                    SeriesId = series.Id,
                    Title = $"Folge {i + 1}",
                    EpisodeNumber = i + 1,
                    LocalFolderPath = $"C:/canceled/{i + 1}"
                });
            }

            FakeCoverImageDataService coverImageService = new();
            SlowLocalCoverLoader coverLoader = new(TimeSpan.FromMilliseconds(200));

            BackgroundCoverService service = BuildService(
                seriesService, episodeService, coverImageService, coverLoader);

            using CancellationTokenSource cts = new();
            cts.CancelAfter(TimeSpan.FromMilliseconds(20));

            // Darf keine Exception werfen — der Foreground-Pfad muss OperationCanceled schlucken,
            // damit das Verlassen der Detailseite kein Log-Rauschen und keinen UI-Fehler erzeugt.
            await service.RequestPriorityForSeriesAsync(series.Id, cts.Token);

            Assert.False(service.IsPriorityActive);
        }

        [Fact]
        public async Task Dispose_ReleasesServiceScope()
        {
            // Memory-Leak-Schutz: Wenn Dispose nicht auf den Hintergrund-Task wartet,
            // kann der Loop eine Service-Scope-Closure am Heap behalten. Zwei Garantien:
            // (1) jeder vom Service erzeugte IServiceScope wird wieder disposed,
            // (2) Dispose hinterlässt keinen ScopeCount > 0 (Zähler differenziert Created vs. Disposed).
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeCoverImageDataService coverImageService = new();
            RecordingLocalCoverLoader coverLoader = new(null);

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImageService);
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ICoverCopyService>(_ => new FakeCoverCopyService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => coverLoader);

            ServiceProvider provider = services.BuildServiceProvider();
            CountingScopeFactory scopeFactory = new(provider.GetRequiredService<IServiceScopeFactory>());

            FakeLoggerFactory loggerFactory = new();
            AppCoverService coverService = new(scopeFactory, loggerFactory);

            BackgroundCoverService service = new(
                scopeFactory,
                coverService,
                new FakeHttpClientFactory(),
                new FakeSpotifyCredentialStore(),
                new BackgroundCoverServiceOptions
                {
                    InitialDelay = TimeSpan.FromMinutes(5),
                    Interval = TimeSpan.FromMinutes(30)
                },
                loggerFactory);

            // Eine echte Iteration (RunOnceAsync) durchläuft sämtliche Scope-erzeugenden Phasen.
            _ = await service.RunOnceAsync(cancellationToken: TestContext.Current.CancellationToken);

            int created = scopeFactory.CreatedCount;
            int active = scopeFactory.ActiveCount;
            Assert.True(created > 0, "RunOnceAsync sollte mindestens einen Scope erstellen.");
            Assert.Equal(0, active);

            service.Dispose();

            // Dispose darf den Zähler nicht negativ ziehen und keinen Scope offen lassen.
            Assert.Equal(0, scopeFactory.ActiveCount);

            // Zweiter Dispose-Aufruf bleibt ein No-Op (Idempotenz).
            service.Dispose();
        }

        [Fact]
        public async Task RequestCoverForSearchResult_RespectsRateLimiter_Foreground()
        {
            // Stellt sicher, dass Such-Treffer den zentralen Rate-Limiter mit Foreground-
            // Priorität durchlaufen – sonst überlasten 20+ parallele Treffer den Provider
            // und Cover erscheinen tröpfchenweise.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeCoverImageDataService coverImageService = new();
            RecordingLocalCoverLoader coverLoader = new(null);

            byte[] coverBytes = [0x10, 0x20, 0x30];
            RecordingHttpMessageHandler handler = new(coverBytes);
            RecordingHttpClientFactory httpFactory = new(handler);
            RecordingHostRateLimiter rateLimiter = new();

            BackgroundCoverService service = BuildService(
                seriesService, episodeService, coverImageService, coverLoader,
                httpFactory, rateLimiter);

            byte[]? result = await service.RequestCoverForSearchResultAsync(
                "Spotify", "unknown-id", "https://i.scdn.co/image/abc123", CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(coverBytes, result);

            (string Host, CoverFetchPriority Priority) recorded = Assert.Single(rateLimiter.Waits);
            Assert.Equal("i.scdn.co", recorded.Host);
            Assert.Equal(CoverFetchPriority.Foreground, recorded.Priority);
            _ = Assert.Single(handler.RequestedUris);
        }

        private static BackgroundCoverService BuildService(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            FakeCoverImageDataService coverImageService,
            ILocalCoverLoader coverLoader)
            => BuildService(seriesService, episodeService, coverImageService, coverLoader,
                new FakeHttpClientFactory(), rateLimiter: null);

        private static BackgroundCoverService BuildService(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            FakeCoverImageDataService coverImageService,
            ILocalCoverLoader coverLoader,
            IHttpClientFactory httpClientFactory,
            IHostRateLimiter? rateLimiter)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImageService);
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ICoverCopyService>(_ => new FakeCoverCopyService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => coverLoader);

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            FakeLoggerFactory loggerFactory = new();
            AppCoverService coverService = new(scopeFactory, loggerFactory);

            return new BackgroundCoverService(
                scopeFactory,
                coverService,
                httpClientFactory,
                new FakeSpotifyCredentialStore(),
                new BackgroundCoverServiceOptions
                {
                    InitialDelay = TimeSpan.FromMinutes(5),
                    Interval = TimeSpan.FromMinutes(30)
                },
                loggerFactory,
                rateLimiter);
        }

        private sealed class FakeHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new();
        }

        private sealed class CountingScopeFactory : IServiceScopeFactory
        {
            private readonly IServiceScopeFactory _inner;
            private int _created;
            private int _disposed;

            public CountingScopeFactory(IServiceScopeFactory inner)
            {
                _inner = inner;
            }

            public int CreatedCount => Volatile.Read(ref _created);

            public int ActiveCount => Volatile.Read(ref _created) - Volatile.Read(ref _disposed);

            public IServiceScope CreateScope()
            {
                _ = Interlocked.Increment(ref _created);
                return new CountingScope(_inner.CreateScope(), () => Interlocked.Increment(ref _disposed));
            }

            private sealed class CountingScope : IServiceScope
            {
                private readonly IServiceScope _inner;
                private readonly Action _onDispose;
                private int _disposed;

                public CountingScope(IServiceScope inner, Action onDispose)
                {
                    _inner = inner;
                    _onDispose = onDispose;
                }

                public IServiceProvider ServiceProvider => _inner.ServiceProvider;

                public void Dispose()
                {
                    if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    {
                        _inner.Dispose();
                        _onDispose();
                    }
                }
            }
        }

        private sealed class RecordingLocalCoverLoader : ILocalCoverLoader
        {
            private readonly byte[]? _bytes;
            public List<string> LoadedFolders { get; } = [];

            public RecordingLocalCoverLoader(byte[]? bytes)
            {
                _bytes = bytes;
            }

            public Task<byte[]?> LoadAsync(string? episodeFolderPath, string? firstTrackPath)
            {
                if (!string.IsNullOrEmpty(episodeFolderPath))
                {
                    lock (LoadedFolders) { LoadedFolders.Add(episodeFolderPath); }
                }
                return Task.FromResult(_bytes);
            }
        }

        private sealed class SlowLocalCoverLoader : ILocalCoverLoader
        {
            private readonly TimeSpan _delay;

            public SlowLocalCoverLoader(TimeSpan delay)
            {
                _delay = delay;
            }

            public async Task<byte[]?> LoadAsync(string? episodeFolderPath, string? firstTrackPath)
            {
                await Task.Delay(_delay);
                return null;
            }
        }

        private sealed class RecordingHostRateLimiter : IHostRateLimiter
        {
            public List<(string Host, CoverFetchPriority Priority)> Waits { get; } = [];

            public Task WaitAsync(string host, CancellationToken ct = default)
                => WaitAsync(host, CoverFetchPriority.Background, ct);

            public Task WaitAsync(string host, CoverFetchPriority priority, CancellationToken ct = default)
            {
                Waits.Add((host, priority));
                return Task.CompletedTask;
            }

            public void Dispose() { }
        }

        private sealed class RecordingHttpMessageHandler : HttpMessageHandler
        {
            private readonly byte[] _response;
            public List<Uri> RequestedUris { get; } = [];

            public RecordingHttpMessageHandler(byte[] response)
            {
                _response = response;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri is not null)
                {
                    RequestedUris.Add(request.RequestUri);
                }
                HttpResponseMessage message = new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_response)
                };
                return Task.FromResult(message);
            }
        }

        private sealed class RecordingHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;

            public RecordingHttpClientFactory(HttpMessageHandler handler)
            {
                _handler = handler;
            }

            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }
    }
}

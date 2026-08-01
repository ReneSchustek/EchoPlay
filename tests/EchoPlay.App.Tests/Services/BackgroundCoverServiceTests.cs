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
            await seriesService.AddAsync(new Series { Title = "Target", LocalFolderPath = "C:/target" }, cancellationToken: TestContext.Current.CancellationToken);
            await seriesService.AddAsync(new Series { Title = "Other", LocalFolderPath = "C:/other" }, cancellationToken: TestContext.Current.CancellationToken);

            Series targetSeries = seriesService.All[0];
            Series otherSeries = seriesService.All[1];

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(new Episode
            {
                SeriesId = targetSeries.Id,
                Title = "Target 1",
                EpisodeNumber = 1,
                LocalFolderPath = "C:/target/1"
            }, cancellationToken: TestContext.Current.CancellationToken);
            await episodeService.AddAsync(new Episode
            {
                SeriesId = otherSeries.Id,
                Title = "Other 1",
                EpisodeNumber = 1,
                LocalFolderPath = "C:/other/1"
            }, cancellationToken: TestContext.Current.CancellationToken);

            FakeCoverImageDataService coverImageService = new();
            RecordingLocalCoverLoader coverLoader = new(CoverBytes);

            BackgroundCoverService service = BuildService(
                seriesService, episodeService, coverImageService, coverLoader);

            await service.RequestPriorityForSeriesAsync(targetSeries.Id, CancellationToken.None);

            Episode targetEpisode = episodeService.All[0];
            Episode otherEpisode = episodeService.All[1];

            Assert.True(await coverImageService.ExistsAsync(CoverEntityTypes.Episode, targetEpisode.Id, cancellationToken: TestContext.Current.CancellationToken));
            Assert.False(await coverImageService.ExistsAsync(CoverEntityTypes.Episode, otherEpisode.Id, cancellationToken: TestContext.Current.CancellationToken));
            _ = Assert.Single(coverLoader.LoadedFolders);
            Assert.Equal("C:/target/1", coverLoader.LoadedFolders[0]);
        }

        [Fact]
        public async Task RequestPriorityForSeries_WhenCancelled_SwallowsOperationCanceledException()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "Canceled", LocalFolderPath = "C:/canceled" }, cancellationToken: TestContext.Current.CancellationToken);
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
                }, cancellationToken: TestContext.Current.CancellationToken);
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
                new CoverDownloader(new FakeHttpClientFactory(), loggerFactory),
                new FakeSpotifyCredentialStore(),
                new BackgroundCoverServiceOptions
                {
                    InitialDelay = TimeSpan.FromMinutes(5),
                    Interval = TimeSpan.FromMinutes(30)
                },
                loggerFactory,
                new FakeClock());

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

        [Fact]
        public async Task RunOnce_StoesstSucheFuerEpisodeOhneCoverAn()
        {
            // Weder lokale Datei noch Provider-URL: Vor dieser Phase blieb so eine Episode
            // dauerhaft ohne Cover, weil die Suchkette nur beim Import lief.
            SuchKontext k = BuildServiceMitSuche();

            _ = await k.Service.RunOnceAsync(TestContext.Current.CancellationToken);

            // CoverLastChecked setzt ausschließlich der EpisodeCoverCacheService. Ist der
            // Zeitstempel gesetzt, wurde die Suchkette tatsächlich durchlaufen. Auf CallCount
            // des Kopierdienstes darf man sich hier NICHT stützen - den ruft die Phase
            // "CopyLocalToOnline" ohnehin bei jedem Lauf auf, der Test wäre immer grün.
            _ = Assert.NotNull(k.Episodes.All[0].CoverLastChecked);
            Assert.False(
                await k.Covers.ExistsAsync(CoverEntityTypes.Episode, k.Episode.Id, cancellationToken: TestContext.Current.CancellationToken),
                "Ohne Treffer darf kein Cover entstehen.");
        }

        [Fact]
        public async Task RunOnce_KeineSucheWennCoverVorhanden()
        {
            SuchKontext k = BuildServiceMitSuche();

            await k.Covers.SetCoverAsync(
                CoverEntityTypes.Episode, k.Episode.Id, CoverBytes, null, TestContext.Current.CancellationToken);

            _ = await k.Service.RunOnceAsync(TestContext.Current.CancellationToken);

            // Kein Zeitstempel: Die Serie hatte keine Lücke, die Suche blieb aus.
            Assert.Null(k.Episodes.All[0].CoverLastChecked);
        }

        [Fact]
        public async Task RunOnce_StoesstSucheFuerSerieOhneCoverAn()
        {
            // Eine Serie ohne lokale cover.jpg und ohne CoverImageUrl wurde vor dieser Phase
            // nie gesucht - der URL-Nachtrag füllt ausschließlich Episoden.
            SuchKontext k = BuildServiceMitSuche();

            _ = await k.Service.RunOnceAsync(TestContext.Current.CancellationToken);

            // Zeitstempel gesetzt heißt: gesucht. Er wird auch ohne Treffer gesetzt,
            // genau das ist der Cooldown.
            _ = Assert.NotNull(k.SeriesService.All[0].CoverLastChecked);
        }

        [Fact]
        public async Task RunOnce_TrefferLandetAlsSerienCoverInDerDatenbank()
        {
            byte[] gefunden = [0x42, 0x43, 0x44];
            RecordingHttpMessageHandler handler = new(gefunden);

            SuchKontext k = BuildServiceMitSuche(
                new FakeCoverSearchService("Ohne Cover - Folge 1", "https://coverartarchive.org/release/abc/front"),
                new RecordingHttpClientFactory(handler));

            _ = await k.Service.RunOnceAsync(TestContext.Current.CancellationToken);

            CoverImage? gespeichert = await k.Covers.GetByEntityAsync(
                CoverEntityTypes.Series, k.Series.Id, TestContext.Current.CancellationToken);

            Assert.NotNull(gespeichert);
            Assert.Equal(gefunden, gespeichert.ImageData);
        }

        [Fact]
        public async Task RunOnce_KeineSerienSucheWaehrendCooldown()
        {
            SuchKontext k = BuildServiceMitSuche();

            // Gestern schon erfolglos gesucht - der Cooldown läuft noch sechs Tage.
            DateTime gestern = k.Clock.UtcNow.AddDays(-1);
            k.SeriesService.All[0].CoverLastChecked = gestern;

            _ = await k.Service.RunOnceAsync(TestContext.Current.CancellationToken);

            // Unverändert: Wäre erneut gesucht worden, stünde hier die aktuelle Zeit.
            Assert.Equal(gestern, k.SeriesService.All[0].CoverLastChecked);
        }

        [Fact]
        public async Task RunOnce_SerienSucheLaeuftNachAblaufDesCooldownsWieder()
        {
            SuchKontext k = BuildServiceMitSuche();

            DateTime vorAchtTagen = k.Clock.UtcNow.AddDays(-8);
            k.SeriesService.All[0].CoverLastChecked = vorAchtTagen;

            _ = await k.Service.RunOnceAsync(TestContext.Current.CancellationToken);

            // Der Cooldown beträgt sieben Tage - nach acht ist die Serie wieder fällig.
            Assert.Equal(k.Clock.UtcNow, k.SeriesService.All[0].CoverLastChecked);
        }

        // Baut den Dienst samt EpisodeCoverCacheService, damit die Online-Phase erreichbar ist.
        // Eine Serie, eine Episode, kein lokaler Ordner, keine Provider-URL.
        // Ohne coverSearch liefert GetService<ICoverSearchService>() null - die Suche läuft
        // dann bis zum Zeitstempel durch, findet aber nichts. Das ist der Normalfall der Tests;
        // nur der Treffer-Test registriert einen Suchdienst.
        private static SuchKontext BuildServiceMitSuche(
            ICoverSearchService? coverSearch = null,
            IHttpClientFactory? httpClientFactory = null)
        {
            FakeSeriesDataService seriesService = new();
            seriesService.AddAsync(new Series { Title = "Ohne Cover" }).GetAwaiter().GetResult();
            Series series = seriesService.All[0];

            FakeEpisodeDataService episodeService = new();
            episodeService.AddAsync(new Episode
            {
                SeriesId = series.Id,
                Title = "Folge 1",
                EpisodeNumber = 1
            }).GetAwaiter().GetResult();
            Episode episode = episodeService.All[0];

            FakeCoverImageDataService coverImageService = new();
            FakeCoverCopyService coverCopy = new();
            FakeClock clock = new();

            // Kein echter HttpClient als Rückfallwert: Die Suchphasen würden sonst gegen das
            // echte Netz laufen. Der Recording-Handler beantwortet jede Anfrage lokal.
            IHttpClientFactory httpFactory = httpClientFactory
                ?? new RecordingHttpClientFactory(new RecordingHttpMessageHandler(CoverBytes));

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImageService);
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ICoverCopyService>(_ => coverCopy);
            _ = services.AddScoped<ILocalCoverLoader>(_ => new RecordingLocalCoverLoader(null));
            _ = services.AddSingleton(httpFactory);

            if (coverSearch is not null)
            {
                _ = services.AddScoped(_ => coverSearch);
            }

            _ = services.AddSingleton(sp => new EpisodeCoverCacheService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                new FakeLoggerFactory(),
                new FakeCoverService(),
                clock,
                new CoverDownloader(sp.GetRequiredService<IHttpClientFactory>(), new FakeLoggerFactory())));

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            FakeLoggerFactory loggerFactory = new();
            AppCoverService coverService = new(scopeFactory, loggerFactory);

            BackgroundCoverService service = new(
                scopeFactory,
                coverService,
                new CoverDownloader(httpFactory, loggerFactory),
                new FakeSpotifyCredentialStore(),
                new BackgroundCoverServiceOptions
                {
                    InitialDelay = TimeSpan.FromMinutes(5),
                    Interval = TimeSpan.FromMinutes(30)
                },
                loggerFactory,
                clock,
                rateLimiter: null);

            return new SuchKontext
            {
                Service = service,
                Covers = coverImageService,
                SeriesService = seriesService,
                Episodes = episodeService,
                Series = series,
                Episode = episode,
                Clock = clock
            };
        }

        // Bündelt, was die Suchtests gemeinsam brauchen. Als Klasse statt Tupel, weil sich
        // sonst bei jedem zusätzlichen Feld sämtliche Destrukturierungen ändern.
        private sealed class SuchKontext
        {
            public required BackgroundCoverService Service { get; init; }
            public required FakeCoverImageDataService Covers { get; init; }
            public required FakeSeriesDataService SeriesService { get; init; }
            public required FakeEpisodeDataService Episodes { get; init; }
            public required Series Series { get; init; }
            public required Episode Episode { get; init; }
            public required FakeClock Clock { get; init; }
        }

        // Liefert immer denselben Treffer. Der ReleaseTitle muss den Seriennamen enthalten,
        // sonst verwirft der CoverRelevanceScorer das Ergebnis unter der Mindestschwelle.
        private sealed class FakeCoverSearchService : ICoverSearchService
        {
            private readonly CoverSearchResult _result;

            public FakeCoverSearchService(string releaseTitle, string fullUrl)
            {
                _result = new CoverSearchResult(fullUrl, fullUrl, releaseTitle, "Test");
            }

            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CancellationToken ct = default)
                => SearchAsync(title, CoverSearchPage.First, ct);

            public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CoverSearchPage page, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<CoverSearchResult>>(page.Index == 0 ? [_result] : []);
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
                new CoverDownloader(httpClientFactory, loggerFactory),
                new FakeSpotifyCredentialStore(),
                new BackgroundCoverServiceOptions
                {
                    InitialDelay = TimeSpan.FromMinutes(5),
                    Interval = TimeSpan.FromMinutes(30)
                },
                loggerFactory,
                new FakeClock(),
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
                await Task.Delay(_delay, cancellationToken: TestContext.Current.CancellationToken);
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

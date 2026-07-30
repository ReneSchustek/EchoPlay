using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="EpisodeCoverCacheService"/>. Der Dienst holt fehlende
    /// Folgen-Cover in drei Stufen: lokal kopieren, Provider-URL laden, online suchen.
    /// Geprüft wird die Ablaufsteuerung — der Cooldown, das Überspringen vorhandener
    /// Cover und dass Fehler den Import nicht abbrechen.
    /// </summary>
    public sealed class EpisodeCoverCacheServiceTests
    {
        private static (EpisodeCoverCacheService Service, FakeCoverCopyService Copy, FakeEpisodeDataService Episodes)
            Build(FakeClock? clock = null, params Episode[] episodes)
        {
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeCoverCopyService coverCopy = new();

            Series series = new() { Title = "Testserie", IsSubscribed = true };
            seriesService.AddAsync(series).GetAwaiter().GetResult();

            foreach (Episode episode in episodes)
            {
                episode.SeriesId = series.Id;
                episodeService.AddAsync(episode).GetAwaiter().GetResult();
            }

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<ICoverCopyService>(_ => coverCopy);
            _ = services.AddHttpClient();

            ServiceProvider provider = services.BuildServiceProvider();

            EpisodeCoverCacheService service = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeLoggerFactory(),
                new FakeCoverService(),
                clock ?? new FakeClock(),
                new CoverDownloader(provider.GetRequiredService<System.Net.Http.IHttpClientFactory>(), new FakeLoggerFactory()));

            SeriesId = series.Id;
            return (service, coverCopy, episodeService);
        }

        private static Guid SeriesId { get; set; }

        [Fact]
        public async Task CacheCoversAsync_AlwaysTriesLocalCopyFirst()
        {
            // Lokale Kopie ist die billigste Quelle – sie läuft vor jedem Netzzugriff.
            (EpisodeCoverCacheService service, FakeCoverCopyService copy, _) = Build(
                clock: null,
                new Episode { Title = "Folge 1" });

            await service.CacheCoversAsync(SeriesId, ct: TestContext.Current.CancellationToken);

            Assert.Equal(1, copy.CallCount);
        }

        [Fact]
        public async Task CacheCoversAsync_WithinCooldown_SkipsEpisode()
        {
            // Erst kürzlich erfolglos gesucht: nicht erneut anfragen, sonst läuft die App
            // bei jedem Start in dieselben Fehlversuche.
            FakeClock clock = new();
            (EpisodeCoverCacheService service, _, FakeEpisodeDataService episodes) = Build(
                clock,
                new Episode { Title = "Folge 1", CoverLastChecked = clock.UtcNow.AddHours(-1) });

            await service.CacheCoversAsync(SeriesId, ct: TestContext.Current.CancellationToken);

            // Der Zeitstempel bleibt unangetastet, weil die Folge übersprungen wurde.
            Assert.Equal(clock.UtcNow.AddHours(-1), episodes.All[0].CoverLastChecked);
        }

        [Fact]
        public async Task CacheCoversAsync_WithoutEpisodes_DoesNotThrow()
        {
            (EpisodeCoverCacheService service, FakeCoverCopyService copy, _) = Build();

            await service.CacheCoversAsync(SeriesId, ct: TestContext.Current.CancellationToken);

            Assert.Equal(1, copy.CallCount);
        }

        [Fact]
        public async Task CacheCoversAsync_UnknownSeries_DoesNotThrow()
        {
            // Der Aufrufer kann eine bereits gelöschte Serie übergeben – das darf
            // den Import nicht abbrechen.
            (EpisodeCoverCacheService service, _, _) = Build();

            await service.CacheCoversAsync(Guid.NewGuid(), ct: TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task CacheCoversAsync_WithCancelledToken_EndsQuietly()
        {
            (EpisodeCoverCacheService service, _, _) = Build(
                clock: null,
                new Episode { Title = "Folge 1" });

            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            // Abbruch ist ein erwarteter Zustand (z. B. Serienwechsel), keine Ausnahme nach außen.
            await service.CacheCoversAsync(SeriesId, ct: cts.Token);
        }

        [Fact]
        public async Task CacheCoversAsync_WithImportEpisodes_AcceptsProviderUrls()
        {
            (EpisodeCoverCacheService service, FakeCoverCopyService copy, _) = Build(
                clock: null,
                new Episode { Title = "Folge 1" });

            List<ImportEpisode> importEpisodes =
            [
                new()
                {
                    SourceEpisodeId = "itunes-1",
                    Title = "Folge 1",
                    CoverImageUrl = "https://example.invalid/cover.jpg"
                }
            ];

            await service.CacheCoversAsync(
                SeriesId,
                importEpisodes,
                CoverFetchPriority.Foreground,
                TestContext.Current.CancellationToken);

            Assert.Equal(1, copy.CallCount);
        }
    }
}

using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft, dass ein Abbruch im <see cref="EpisodeCoverCacheService"/> nicht als
    /// Fehlschlag einer Folge protokolliert wird. Der Dienst fing in beiden Schleifen
    /// pauschal <c>Exception</c> — damit landete jeder Abbruch als Warnung im Protokoll,
    /// und weil der Zweig die Ausnahme schluckte, lief die Schleife noch eine Runde weiter.
    /// </summary>
    public sealed class EpisodeCoverCacheServiceCancelTests
    {
        [Fact]
        public async Task CacheCoversAsync_AbbruchImDownload_ProtokolliertKeineWarnung()
        {
            using CancellationTokenSource cts = new();

            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            Series series = new() { Title = "Testserie", IsSubscribed = true };
            await seriesService.AddAsync(series, cancellationToken: TestContext.Current.CancellationToken);

            // Zwei Folgen mit Provider-Adresse: Die erste bricht ab, die zweite darf danach
            // nicht mehr an die Reihe kommen.
            foreach (string titel in new[] { "Folge 1", "Folge 2" })
            {
                await episodeService.AddAsync(
                    new Episode
                    {
                        SeriesId = series.Id,
                        Title = titel,
                        CoverImageUrl = "https://example.invalid/" + titel + ".jpg"
                    },
                    cancellationToken: TestContext.Current.CancellationToken);
            }

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<ICoverCopyService>(_ => new FakeCoverCopyService());
            ServiceProvider provider = services.BuildServiceProvider();

            CapturingLogger logger = new();
            CancellingCoverDownloader downloader = new(cts);

            EpisodeCoverCacheService service = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new CapturingLoggerFactory(logger),
                new FakeCoverService(),
                new FakeClock(),
                downloader);

            await service.CacheCoversAsync(series.Id, ct: cts.Token);

            // Der öffentliche Einstiegspunkt schluckt den Abbruch bewusst — er darf aber
            // nicht als Fehlschlag der Folge im Protokoll erscheinen.
            Assert.DoesNotContain(logger.Entries, e => e.Level == "Warning");

            // Und der Abbruch muss die Schleife wirklich verlassen, nicht bloß eine Runde
            // später greifen: die zweite Folge wurde nie angefragt.
            Assert.Equal(1, downloader.CallCount);
        }

        /// <summary>
        /// Downloader, der beim ersten Aufruf abbricht und sich danach wie der echte
        /// <see cref="CoverDownloader"/> verhält: Abbruch des Aufrufer-Tokens wird geworfen.
        /// </summary>
        private sealed class CancellingCoverDownloader : ICoverDownloader
        {
            private readonly CancellationTokenSource _cts;

            public CancellingCoverDownloader(CancellationTokenSource cts) => _cts = cts;

            public int CallCount { get; private set; }

            public async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken = default)
            {
                CallCount++;
                await _cts.CancelAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                return null;
            }
        }
    }
}

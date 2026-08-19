using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
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
    /// Prüft die beiden Download-Phasen des Hintergrundlaufs: Serien- und Folgen-Cover
    /// werden über die hinterlegte Adresse geholt.
    /// </summary>
    /// <remarks>
    /// Der Nachtrag der Adressen steht in <see cref="OnlineCoverPhasesUrlTests"/>. Hier geht
    /// es um die Stufe danach, und die trägt eine Zusage: Was schon in der Ablage liegt, wird
    /// nicht erneut geladen. Ohne sie zöge jeder Programmstart den kompletten Bestand erneut
    /// über die Leitung.
    /// </remarks>
    public sealed class OnlineCoverPhasesDownloadTests
    {
        private const string SeriesCoverUrl = "https://example.com/serie.jpg";
        private const string EpisodeCoverUrl = "https://example.com/folge.jpg";
        private static readonly byte[] CoverBytes = [0x42, 0x43];

        [Fact]
        public async Task SerienCover_WerdenUeberDieAdresseGeholtUndAbgelegt()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG", CoverImageUrl = SeriesCoverUrl };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();
            downloader.SetResponse(SeriesCoverUrl, CoverBytes);
            FakeCoverService coverService = new();

            OnlineCoverPhases phases = BuildPhases(seriesService, new FakeEpisodeDataService(),
                new FakeCoverImageDataService(), coverService, downloader);

            int geladen = await phases.DownloadMissingSeriesProviderCoversAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, geladen);
            Assert.Equal([SeriesCoverUrl], downloader.RequestedUrls);
            Assert.Equal([series.Id], coverService.StoredSeriesCovers);
        }

        [Fact]
        public async Task SerienCover_OhneAdresseWerdenNichtGeholt()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "Nur lokal" }, TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();

            OnlineCoverPhases phases = BuildPhases(seriesService, new FakeEpisodeDataService(),
                new FakeCoverImageDataService(), new FakeCoverService(), downloader);

            int geladen = await phases.DownloadMissingSeriesProviderCoversAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, geladen);
            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task SerienCover_WasSchonAbgelegtIstWirdNichtErneutGeholt()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG", CoverImageUrl = SeriesCoverUrl };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeCoverImageDataService coverImages = new();
            await coverImages.SetCoverAsync(
                CoverEntityTypes.Series, series.Id, CoverBytes, SeriesCoverUrl, TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();
            downloader.SetResponse(SeriesCoverUrl, CoverBytes);

            OnlineCoverPhases phases = BuildPhases(seriesService, new FakeEpisodeDataService(),
                coverImages, new FakeCoverService(), downloader);

            int geladen = await phases.DownloadMissingSeriesProviderCoversAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, geladen);
            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task SerienCover_EinFehlgeschlagenerDownloadZaehltNicht()
        {
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(
                new Series { Title = "TKKG", CoverImageUrl = SeriesCoverUrl }, TestContext.Current.CancellationToken);

            // Ohne hinterlegte Antwort meldet der Downloader „nichts bekommen".
            FakeCoverDownloader downloader = new();
            FakeCoverService coverService = new();

            OnlineCoverPhases phases = BuildPhases(seriesService, new FakeEpisodeDataService(),
                new FakeCoverImageDataService(), coverService, downloader);

            int geladen = await phases.DownloadMissingSeriesProviderCoversAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, geladen);
            Assert.Empty(coverService.StoredSeriesCovers);
        }

        [Fact]
        public async Task FolgenCover_WerdenUeberDieAdresseGeholtUndAbgelegt()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG" };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            Episode episode = new() { SeriesId = series.Id, Title = "Folge 1", CoverImageUrl = EpisodeCoverUrl };
            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();
            downloader.SetResponse(EpisodeCoverUrl, CoverBytes);
            FakeCoverService coverService = new();

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService,
                new FakeCoverImageDataService(), coverService, downloader);

            int geladen = await phases.DownloadMissingEpisodeProviderCoversAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, geladen);
            Assert.Equal([episode.Id], coverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task FolgenCover_UeberspringenSerienOhneKandidaten()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG" };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(
                new Episode { SeriesId = series.Id, Title = "Folge ohne Adresse" }, TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService,
                new FakeCoverImageDataService(), new FakeCoverService(), downloader);

            int geladen = await phases.DownloadMissingEpisodeProviderCoversAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, geladen);
            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task FolgenCover_WasSchonAbgelegtIstWirdNichtErneutGeholt()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG" };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            Episode episode = new() { SeriesId = series.Id, Title = "Folge 1", CoverImageUrl = EpisodeCoverUrl };
            await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);

            FakeCoverImageDataService coverImages = new();
            await coverImages.SetCoverAsync(
                CoverEntityTypes.Episode, episode.Id, CoverBytes, EpisodeCoverUrl, TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();
            downloader.SetResponse(EpisodeCoverUrl, CoverBytes);

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService,
                coverImages, new FakeCoverService(), downloader);

            int geladen = await phases.DownloadMissingEpisodeProviderCoversAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, geladen);
            Assert.Empty(downloader.RequestedUrls);
        }

        [Fact]
        public async Task FolgenCover_AbbruchBeendetDenLauf()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG" };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            await episodeService.AddAsync(
                new Episode { SeriesId = series.Id, Title = "Folge 1", CoverImageUrl = EpisodeCoverUrl },
                TestContext.Current.CancellationToken);

            FakeCoverDownloader downloader = new();
            downloader.SetResponse(EpisodeCoverUrl, CoverBytes);

            OnlineCoverPhases phases = BuildPhases(seriesService, episodeService,
                new FakeCoverImageDataService(), new FakeCoverService(), downloader);

            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            int geladen = await phases.DownloadMissingEpisodeProviderCoversAsync(cts.Token);

            Assert.Equal(0, geladen);
            Assert.Empty(downloader.RequestedUrls);
        }

        private static OnlineCoverPhases BuildPhases(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            FakeCoverImageDataService coverImages,
            FakeCoverService coverService,
            FakeCoverDownloader downloader)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImages);

            ServiceProvider provider = services.BuildServiceProvider();

            return new OnlineCoverPhases(
                provider.GetRequiredService<IServiceScopeFactory>(),
                coverService,
                downloader,
                new FakeSpotifyCredentialStore(),
                new FakeClock(),
                rateLimiter: null,
                new FakeLogger());
        }
    }
}

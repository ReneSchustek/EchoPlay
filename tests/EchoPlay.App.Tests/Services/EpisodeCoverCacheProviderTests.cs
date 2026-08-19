using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft die erste Stufe des Cover-Nachladens nach einem Import: die Adresse, die der
    /// Anbieter zur Folge mitgeliefert hat.
    /// </summary>
    /// <remarks>
    /// Der Anbieter liefert die Cover-Adresse beim Import kostenlos mit. Wird sie nicht
    /// genutzt, landet dieselbe Folge später in der langsamen Online-Suchkette — mit
    /// Wartezeit für den Anwender und Last bei fremden Diensten.
    /// </remarks>
    public sealed class EpisodeCoverCacheProviderTests
    {
        private const string CoverUrl = "https://i.example.invalid/folge-1.jpg";

        [Fact]
        public async Task CacheCovers_WithProviderUrlFromTheImport_StoresTheCover()
        {
            Fixture fixture = await BuildAsync("Folge 1");
            fixture.Downloader.SetResponse(CoverUrl, [1, 2, 3]);

            await fixture.Service.CacheCoversAsync(
                fixture.SeriesId,
                [BuildImportEpisode("Folge 1", CoverUrl)],
                ct: TestContext.Current.CancellationToken);

            Assert.Equal([CoverUrl], fixture.Downloader.RequestedUrls);
            Assert.Contains(fixture.EpisodeId, fixture.CoverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task CacheCovers_WithProviderUrlOnTheEpisode_StoresTheCover()
        {
            Fixture fixture = await BuildAsync("Folge 1", episodeCoverUrl: CoverUrl);
            fixture.Downloader.SetResponse(CoverUrl, [1, 2, 3]);

            await fixture.Service.CacheCoversAsync(fixture.SeriesId, ct: TestContext.Current.CancellationToken);

            // Auch ohne Importdaten steht die Adresse an der Folge — etwa nach einem
            // Nachimport.
            Assert.Equal([CoverUrl], fixture.Downloader.RequestedUrls);
        }

        [Fact]
        public async Task CacheCovers_WhenTheDownloadFails_StoresNothing()
        {
            Fixture fixture = await BuildAsync("Folge 1", episodeCoverUrl: CoverUrl);

            await fixture.Service.CacheCoversAsync(fixture.SeriesId, ct: TestContext.Current.CancellationToken);

            Assert.Empty(fixture.CoverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task CacheCovers_WithoutMatchingTitle_DownloadsNothing()
        {
            Fixture fixture = await BuildAsync("Folge 1");

            await fixture.Service.CacheCoversAsync(
                fixture.SeriesId,
                [BuildImportEpisode("Ganz andere Folge", CoverUrl)],
                ct: TestContext.Current.CancellationToken);

            // Die Zuordnung läuft über den Titel. Passt er nicht, gehört das Cover zu einer
            // anderen Folge — lieber keines als ein falsches.
            Assert.Empty(fixture.Downloader.RequestedUrls);
        }

        [Fact]
        public async Task CacheCovers_ForASeriesWithoutEpisodes_StaysQuiet()
        {
            Fixture fixture = await BuildAsync(episodeTitle: null);

            await fixture.Service.CacheCoversAsync(fixture.SeriesId, ct: TestContext.Current.CancellationToken);

            Assert.Empty(fixture.Downloader.RequestedUrls);
        }

        [Fact]
        public async Task CacheCovers_WithoutProviderUrl_FallsBackToTheOnlineSearch()
        {
            FakeCoverSearchService search = new();
            search.SetResults(
            [
                new CoverSearchResult(
                    "https://example.invalid/thumb.jpg", "https://example.invalid/full.jpg",
                    "TKKG Folge 1", "Cover Art Archive"),
            ]);

            Fixture fixture = await BuildAsync("TKKG - 001 - Folge 1", search: search);
            fixture.Downloader.SetResponse("https://example.invalid/full.jpg", [9, 9]);

            await fixture.Service.CacheCoversAsync(fixture.SeriesId, ct: TestContext.Current.CancellationToken);

            // Ohne Adresse vom Anbieter bleibt nur die Suchkette — sonst trüge die Folge
            // dauerhaft einen Platzhalter.
            Assert.NotNull(search.LastSearchTitle);
            Assert.Contains(fixture.EpisodeId, fixture.CoverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task CacheCovers_WhenTheOnlineSearchFindsNothing_StillStartsTheCooldown()
        {
            Fixture fixture = await BuildAsync("TKKG - 001 - Folge 1");

            await fixture.Service.CacheCoversAsync(fixture.SeriesId, ct: TestContext.Current.CancellationToken);

            // Der Zeitstempel verhindert, dass dieselbe Folge bei jedem Start erneut
            // durch die ganze Suchkette geschickt wird.
            Episode? stored = await fixture.Episodes.GetByIdAsync(
                fixture.EpisodeId, TestContext.Current.CancellationToken);
            Assert.NotNull(stored);
            Assert.True(stored.CoverLastChecked.HasValue);
        }

        [Fact]
        public async Task CacheCovers_WhenEverythingWasCopiedLocally_AsksNobodyElse()
        {
            Fixture fixture = await BuildAsync("Folge 1", copiedLocally: 2, alreadyCovered: true);

            await fixture.Service.CacheCoversAsync(fixture.SeriesId, ct: TestContext.Current.CancellationToken);

            // Wenn der eigene Bestand schon alles hergibt, ist jede Anfrage nach draußen
            // überflüssig — und bei fremden Diensten obendrein unhöflich.
            Assert.Empty(fixture.Downloader.RequestedUrls);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private sealed class Fixture
        {
            public required EpisodeCoverCacheService Service { get; init; }
            public required FakeCoverDownloader Downloader { get; init; }
            public required FakeCoverService CoverService { get; init; }
            public required FakeEpisodeDataService Episodes { get; init; }
            public required FakeCoverSearchService Search { get; init; }
            public required Guid EpisodeId { get; init; }
            public required Guid SeriesId { get; init; }
        }

        private static ImportEpisode BuildImportEpisode(string title, string coverUrl)
            => new()
            {
                SourceEpisodeId = "src-1",
                Title = title,
                CoverImageUrl = coverUrl,
            };

        private static async Task<Fixture> BuildAsync(
            string? episodeTitle,
            string? episodeCoverUrl = null,
            FakeCoverSearchService? search = null,
            int copiedLocally = 0,
            bool alreadyCovered = false)
        {
            FakeCoverSearchService coverSearch = search ?? new FakeCoverSearchService();
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG" };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeEpisodeDataService episodeService = new();
            Guid episodeId = Guid.Empty;
            if (episodeTitle is not null)
            {
                Episode episode = new()
                {
                    SeriesId = series.Id,
                    Title = episodeTitle,
                    CoverImageUrl = episodeCoverUrl,
                };
                await episodeService.AddAsync(episode, TestContext.Current.CancellationToken);
                episodeId = episode.Id;
            }

            FakeCoverDownloader downloader = new();
            FakeCoverService coverService = new();
            if (alreadyCovered && episodeId != Guid.Empty)
            {
                coverService.ExistingEpisodeCovers[episodeId] = [1];
            }

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddScoped<ICoverCopyService>(_ => new FakeCoverCopyService { CopiedCount = copiedLocally });
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ILocalCoverLoader>(_ => new FakeLocalCoverLoader());
            _ = services.AddScoped<ICoverSearchService>(_ => coverSearch);

            ServiceProvider provider = services.BuildServiceProvider();

            return new Fixture
            {
                Service = new EpisodeCoverCacheService(
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    new FakeLoggerFactory(),
                    coverService,
                    new FakeClock(),
                    downloader),
                Downloader = downloader,
                CoverService = coverService,
                Episodes = episodeService,
                Search = coverSearch,
                EpisodeId = episodeId,
                SeriesId = series.Id,
            };
        }
    }
}

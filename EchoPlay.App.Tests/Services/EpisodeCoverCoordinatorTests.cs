using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using AppCoverService = EchoPlay.App.Services.CoverService;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="EpisodeCoverCoordinator"/>. Deckt die rein lesende
    /// Cover-Suche ab. Die Apply-Methoden werden hier nicht getestet, weil sie
    /// echten Datei-IO und Bildkonvertierung über WinUI-Bitmaps voraussetzen würden.
    /// </summary>
    public sealed class EpisodeCoverCoordinatorTests
    {
        private static EpisodeCoverCoordinator BuildCoordinator(FakeCoverSearchService searchService)
        {
            ServiceCollection services = new();
            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            return new EpisodeCoverCoordinator(
                scopeFactory,
                searchService,
                new AppCoverService(scopeFactory, new FakeLoggerFactory()),
                new FakeConfirmationDialogService(),
                new FakeErrorDialogService());
        }

        [Fact]
        public async Task SearchCoversAsync_ReturnsEmpty_WhenServiceHasNoResults()
        {
            FakeCoverSearchService searchService = new();
            EpisodeCoverCoordinator coordinator = BuildCoordinator(searchService);

            IReadOnlyList<CoverSearchHit> hits = await coordinator.SearchCoversAsync(
                "Die drei Fragezeichen", CancellationToken.None);

            Assert.Empty(hits);
            Assert.Equal("Die drei Fragezeichen", searchService.LastSearchTitle);
        }

        [Fact]
        public async Task SearchCoversAsync_MapsServiceResultsToHits()
        {
            FakeCoverSearchService searchService = new();
            searchService.SetResults(new List<CoverSearchResult>
            {
                new("https://thumb/1", "https://full/1", "Folge 1",  "Cover Art Archive"),
                new("https://thumb/2", "https://full/2", "Folge 2",  "iTunes")
            });

            EpisodeCoverCoordinator coordinator = BuildCoordinator(searchService);

            IReadOnlyList<CoverSearchHit> hits = await coordinator.SearchCoversAsync(
                "Hörspiel", CancellationToken.None);

            Assert.Equal(2, hits.Count);
            Assert.Equal("https://thumb/1",     hits[0].ThumbnailUrl);
            Assert.Equal("https://full/1",      hits[0].FullUrl);
            Assert.Equal("Folge 1",             hits[0].ReleaseTitle);
            Assert.Equal("Cover Art Archive",   hits[0].Source);
            Assert.Equal("https://full/2",      hits[1].FullUrl);
            Assert.Equal("iTunes",              hits[1].Source);
        }
    }
}

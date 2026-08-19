using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using AppCoverService = EchoPlay.App.Services.CoverService;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="EpisodeCoverCoordinator"/>: die rein lesende Cover-Suche und die
    /// Wege, auf denen das Anwenden eines Covers vorzeitig endet.
    /// </summary>
    /// <remarks>
    /// Die Erfolgspfade der Apply-Methoden bleiben außen vor — sie schreiben Dateien und
    /// erzeugen am Ende ein Bildobjekt, das an den Dispatcher gehört. Ihre Abbruchwege sind
    /// dagegen prüfbar und wichtiger, als sie aussehen: Dort entscheidet sich, ob ein
    /// gescheiterter Download dem Anwender gemeldet wird oder ob die Kachel einfach leer
    /// bleibt und er den Fehler bei sich sucht.
    /// </remarks>
    public sealed class EpisodeCoverCoordinatorTests
    {
        private static readonly Guid SeriesId = new("cccccccc-1111-2222-3333-666666666666");

        private static EpisodeCoverCoordinator BuildCoordinator(
            FakeCoverSearchService searchService,
            FakeCoverDownloader? downloader = null,
            FakeErrorDialogService? errorDialogService = null)
        {
            ServiceCollection services = new();
            _ = services.AddHttpClient();
            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            return new EpisodeCoverCoordinator(
                scopeFactory,
                searchService,
                new AppCoverService(scopeFactory, new FakeLoggerFactory()),
                new FakeConfirmationDialogService(),
                errorDialogService ?? new FakeErrorDialogService(),
                downloader ?? new FakeCoverDownloader(),
                new FakeLocalizationService());
        }

        [Fact]
        public async Task RohesCover_LandetInDerAblageDerSerie()
        {
            // Bis hierher ist der Weg ohne Fenster prüfbar: Die Ablage bekommt die Bytes.
            // Das Bildobjekt danach entsteht am Dispatcher und bleibt im Testlauf leer.
            FakeCoverService coverService = new();
            EpisodeCoverCoordinator coordinator = BuildCoordinatorWithStore(coverService);
            LocalArtistCardViewModel card = BuildArtistCard();

            await coordinator.ApplySeriesCoverFromBytesAsync(card, [1, 2, 3], TestContext.Current.CancellationToken);

            Assert.Equal([card.SeriesId], coverService.StoredSeriesCovers);
            Assert.Null(card.CoverImage);
        }

        [Fact]
        public async Task RohesCover_LandetInDerAblageDerFolge()
        {
            FakeCoverService coverService = new();
            EpisodeCoverCoordinator coordinator = BuildCoordinatorWithStore(coverService);
            LocalEpisodeCardViewModel card = BuildEpisodeCard();

            await coordinator.ApplyEpisodeCoverFromBytesAsync(card, [1, 2, 3], TestContext.Current.CancellationToken);

            Assert.Equal([card.EpisodeId], coverService.StoredEpisodeCovers);
        }

        [Fact]
        public async Task GewaehlterTreffer_WirdGeladenUndAbgelegt()
        {
            // Der Klick auf einen Treffer lädt erst das Bild und legt es dann ab. Ohne den
            // Download wäre die Ablage leer, ohne die Ablage wäre das Bild nach dem
            // Seitenwechsel wieder weg.
            FakeCoverDownloader downloader = new();
            downloader.SetResponse("https://example.com/voll.jpg", [9, 9]);
            FakeCoverService coverService = new();
            EpisodeCoverCoordinator coordinator = BuildCoordinatorWithStore(coverService, downloader);
            LocalArtistCardViewModel card = BuildArtistCard();

            await coordinator.ApplySelectedSeriesCoverAsync(
                card,
                new CoverSearchHit("https://example.com/klein.jpg", "https://example.com/voll.jpg", "TKKG 1", "Test"),
                TestContext.Current.CancellationToken);

            Assert.Equal(["https://example.com/voll.jpg"], downloader.RequestedUrls);
            Assert.Equal([card.SeriesId], coverService.StoredSeriesCovers);
        }

        [Fact]
        public async Task GewaehlterTreffer_LandetAuchAufDerFolge()
        {
            FakeCoverDownloader downloader = new();
            downloader.SetResponse("https://example.com/voll.jpg", [9, 9]);
            FakeCoverService coverService = new();
            EpisodeCoverCoordinator coordinator = BuildCoordinatorWithStore(coverService, downloader);
            LocalEpisodeCardViewModel card = BuildEpisodeCard();

            await coordinator.ApplySelectedEpisodeCoverAsync(
                card,
                new CoverSearchHit("https://example.com/klein.jpg", "https://example.com/voll.jpg", "TKKG 1", "Test"),
                TestContext.Current.CancellationToken);

            Assert.Equal([card.EpisodeId], coverService.StoredEpisodeCovers);
        }

        /// <summary>
        /// Baut den Koordinator mit einer Ablage, die sich merkt, was sie bekommen hat.
        /// Die übrigen Tests nutzen den echten Cover-Dienst, weil sie vor der Ablage abbiegen.
        /// </summary>
        private static EpisodeCoverCoordinator BuildCoordinatorWithStore(
            FakeCoverService coverService,
            FakeCoverDownloader? downloader = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<EchoPlay.Data.Services.Interfaces.IAppSettingsDataService>(
                _ => new FakeAppSettingsDataService());
            ServiceProvider provider = services.BuildServiceProvider();

            return new EpisodeCoverCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeCoverSearchService(),
                coverService,
                new FakeConfirmationDialogService(),
                new FakeErrorDialogService(),
                downloader ?? new FakeCoverDownloader(),
                new FakeLocalizationService());
        }

        /// <summary>
        /// Baut eine Folgen-Kachel ohne Cover und ohne Ordner. Ohne Ordner bleibt das
        /// Schreiben der cover.jpg aus — geprüft wird die Ablage, nicht das Dateisystem.
        /// </summary>
        private static LocalEpisodeCardViewModel BuildEpisodeCard()
            => new(
                episodeId: new Guid("cccccccc-1111-2222-3333-777777777777"),
                episodeNumber: 1,
                title: "Der Superhund",
                localTrackCount: 3,
                folderPath: null);

        /// <summary>
        /// Baut eine Künstler-Kachel ohne Cover. Der Bildwert bleibt leer, weil ein
        /// Bildobjekt im Testlauf nicht erzeugbar ist — für die geprüften Abbruchwege
        /// spielt er keine Rolle.
        /// </summary>
        private static LocalArtistCardViewModel BuildArtistCard()
        {
            ServiceCollection services = new();
            ServiceProvider provider = services.BuildServiceProvider();

            return new LocalArtistCardViewModel(
                SeriesId,
                "Die drei Fragezeichen",
                coverImage: null,
                localFolderPath: null,
                localEpisodeCount: 3,
                totalEpisodeCount: 3,
                isFavorite: false,
                isWatched: false,
                provider.GetRequiredService<IServiceScopeFactory>());
        }

        [Fact]
        public async Task SearchCoversAsync_ReturnsEmpty_WhenServiceHasNoResults()
        {
            FakeCoverSearchService searchService = new();
            EpisodeCoverCoordinator coordinator = BuildCoordinator(searchService);

            IReadOnlyList<CoverSearchHit> hits = await coordinator.SearchCoversAsync(
                "Die drei Fragezeichen", EchoPlay.LocalLibrary.Cover.CoverSearchPage.First, CancellationToken.None);

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
                "Hörspiel", EchoPlay.LocalLibrary.Cover.CoverSearchPage.First, CancellationToken.None);

            Assert.Equal(2, hits.Count);
            Assert.Equal("https://thumb/1", hits[0].ThumbnailUrl);
            Assert.Equal("https://full/1", hits[0].FullUrl);
            Assert.Equal("Folge 1", hits[0].ReleaseTitle);
            Assert.Equal("Cover Art Archive", hits[0].Source);
            Assert.Equal("https://full/2", hits[1].FullUrl);
            Assert.Equal("iTunes", hits[1].Source);
        }

        [Fact]
        public async Task ApplySelectedSeriesCover_WhenDownloadFails_TellsTheUser()
        {
            FakeErrorDialogService errorDialog = new();
            EpisodeCoverCoordinator coordinator = BuildCoordinator(
                new FakeCoverSearchService(), new FakeCoverDownloader(), errorDialog);

            LocalArtistCardViewModel card = BuildArtistCard();
            CoverSearchHit hit = new("https://thumb/1", "https://full/1", "Folge 1", "iTunes");

            await coordinator.ApplySelectedSeriesCoverAsync(card, hit, TestContext.Current.CancellationToken);

            // Scheitert der Download stillschweigend, klickt der Anwender auf einen Treffer
            // und nichts passiert — er hält die Auswahl für kaputt statt die Verbindung.
            _ = Assert.Single(errorDialog.ShownDialogs);
            Assert.Null(card.CoverImage);
        }

        [Fact]
        public async Task ApplySelectedEpisodeCover_WithoutHit_Throws()
        {
            EpisodeCoverCoordinator coordinator = BuildCoordinator(new FakeCoverSearchService());

            _ = await Assert.ThrowsAsync<ArgumentNullException>(
                () => coordinator.ApplySelectedEpisodeCoverAsync(
                    card: null!, hit: null!, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ApplySeriesCoverFromBytes_WithoutCard_Throws()
        {
            EpisodeCoverCoordinator coordinator = BuildCoordinator(new FakeCoverSearchService());

            _ = await Assert.ThrowsAsync<ArgumentNullException>(
                () => coordinator.ApplySeriesCoverFromBytesAsync(
                    card: null!, bytes: [0x42], TestContext.Current.CancellationToken));
        }
    }
}

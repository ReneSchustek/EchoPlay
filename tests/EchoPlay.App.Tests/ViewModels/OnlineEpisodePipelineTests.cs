using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests der Episoden-Pipeline in der Online-Mediathek: Serienwahl, Aufbau der
    /// Folgenliste, Chargen beim Abruf der Bilddaten und Übernahme eines gewählten Covers.
    ///
    /// Nicht geprüft ist das Setzen der Bildobjekte selbst — <c>BitmapImage</c> gehört an den
    /// Zeichenfaden des Fensters. Ohne ihn liefert die Umwandlung nichts, die Kacheln behalten
    /// also ihren Platzhalter. Alles davor und danach läuft trotzdem durch; genau das ist hier
    /// festgehalten.
    /// </summary>
    public sealed class OnlineEpisodePipelineTests
    {
        private static readonly Guid SeriesId = new("00000000-0000-0000-0000-00000000a001");
        private static readonly Guid OtherSeriesId = new("00000000-0000-0000-0000-00000000a002");
        private static readonly Guid EpisodeId = new("00000000-0000-0000-0000-00000000b001");

        /// <summary>Wie viele Cover die Pipeline vor dem ersten Zeichnen holt.</summary>
        private const int FirstVisibleCovers = 24;

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private sealed class Harness
        {
            public required OnlineLibraryActionsContext Context { get; init; }
            public required FakeEpisodeDataService Episodes { get; init; }
            public required FakeCoverCopyService CoverCopy { get; init; }
            public required FakeCoverImageDataService CoverImages { get; init; }
            public required FakeCoverDownloader Downloader { get; init; }
            public OnlineSeriesViewModel SeriesVM { get; } = new();
            public OnlineEpisodesViewModel EpisodesVM { get; } = new();
            public OnlineActionsState State { get; } = new();

            public OnlineEpisodePipeline CreatePipeline()
                => new(Context, SeriesVM, EpisodesVM, State);

            public SeriesCardViewModel CreateCard(Guid id, string title)
                => new(
                    id: id, title: title, coverImage: null,
                    totalEpisodeCount: 0, newEpisodeCount: 0, inProgressCount: 0, finishedCount: 0,
                    isSubscribed: false, isFavorite: false, isWatched: false,
                    scopeFactory: Context.ScopeFactory,
                    confirmationDialogService: Context.ConfirmationDialogService,
                    localizationService: Context.LocalizationService);
        }

        private static Harness BuildHarness(bool withCoverCache = false)
        {
            FakeEpisodeDataService episodeService = new();
            FakeCoverCopyService coverCopy = new();
            FakeCoverImageDataService coverImages = new();
            FakeCoverDownloader downloader = new();

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(
                new AppSettings { ActiveProvider = ProviderType.Spotify }));
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImages);
            _ = services.AddScoped<ICoverCopyService>(_ => coverCopy);
            _ = services.AddSingleton<ISpotifyClientCredentialsProvider>(
                FakeSpotifyClientCredentialsProvider.WithCredentials());
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddSingleton<ICoverDownloader>(downloader);
            _ = services.AddSingleton<CoverService>();
            _ = services.AddSingleton<ICoverService>(sp => sp.GetRequiredService<CoverService>());
            _ = services.AddSingleton<EpisodeCoverCacheService>();

            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            ImportService importService = new(
                scopeFactory,
                provider.GetRequiredService<EpisodeCoverCacheService>(),
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>());

            OnlineLibraryActionsContext context = new(
                ScopeFactory: scopeFactory,
                ConfirmationDialogService: new FakeConfirmationDialogService(),
                ImportService: importService,
                ErrorDialogService: new FakeErrorDialogService(),
                LocalizationService: new FakeLocalizationService(),
                OnlineAccessGuard: new FakeOnlineAccessGuard(),
                CoverCacheService: withCoverCache ? provider.GetRequiredService<EpisodeCoverCacheService>() : null,
                CoverService: provider.GetRequiredService<CoverService>(),
                BackgroundCoverService: null,
                WatchToggleService: null,
                CoverDownloader: downloader,
                RateLimiter: null);

            return new Harness
            {
                Context = context,
                Episodes = episodeService,
                CoverCopy = coverCopy,
                CoverImages = coverImages,
                Downloader = downloader,
            };
        }

        /// <summary>
        /// Legt fortlaufend nummerierte Folgen einer Serie an und gibt sie in dieser
        /// Reihenfolge zurück. Die Kennungen vergibt der Nachbau beim Anlegen.
        /// </summary>
        private static async Task<IReadOnlyList<Episode>> SeedEpisodesAsync(
            FakeEpisodeDataService service, Guid seriesId, int count)
        {
            List<Episode> created = new(count);
            for (int number = 1; number <= count; number++)
            {
                Episode episode = new()
                {
                    SeriesId = seriesId,
                    Title = $"Folge {number}",
                    EpisodeNumber = number,
                };
                await service.AddAsync(episode, TestContext.Current.CancellationToken);
                created.Add(episode);
            }
            return created;
        }

        // ── Serienwahl ───────────────────────────────────────────────────────────

        [Fact]
        public async Task SelectSeriesAsync_WithEpisodesOfTheSeries_ShowsThemAsCards()
        {
            Harness harness = BuildHarness();
            _ = await SeedEpisodesAsync(harness.Episodes, SeriesId, 3);
            SeriesCardViewModel card = harness.CreateCard(SeriesId, "TKKG");
            harness.SeriesVM.SetAllSeries([card]);

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.SelectSeriesAsync(card);
            sut.Dispose();

            Assert.Equal(3, harness.EpisodesVM.Episodes.Count);
            Assert.Equal("Folge 1", harness.EpisodesVM.Episodes[0].Title);
            Assert.Equal(3, harness.EpisodesVM.Episodes[2].EpisodeNumber);
        }

        [Fact]
        public async Task SelectSeriesAsync_WithEpisodesOfAnotherSeries_LeavesTheListEmpty()
        {
            Harness harness = BuildHarness();
            _ = await SeedEpisodesAsync(harness.Episodes, OtherSeriesId, 2);
            SeriesCardViewModel card = harness.CreateCard(SeriesId, "TKKG");
            harness.SeriesVM.SetAllSeries([card]);

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.SelectSeriesAsync(card);
            sut.Dispose();

            Assert.Empty(harness.EpisodesVM.Episodes);
        }

        [Fact]
        public async Task SelectSeriesAsync_WithCompletedEpisode_MarksThatCardAsCompleted()
        {
            Harness harness = BuildHarness();
            IReadOnlyList<Episode> episodes = await SeedEpisodesAsync(harness.Episodes, SeriesId, 2);
            SeriesCardViewModel card = harness.CreateCard(SeriesId, "TKKG");
            harness.SeriesVM.SetAllSeries([card]);
            _ = harness.State.CompletedEpisodeIds.Add(episodes[1].Id);

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.SelectSeriesAsync(card);
            sut.Dispose();

            Assert.False(harness.EpisodesVM.Episodes[0].IsCompleted);
            Assert.True(harness.EpisodesVM.Episodes[1].IsCompleted);
        }

        [Fact]
        public async Task SelectSeriesAsync_WhenDone_EndsTheLoadingIndicator()
        {
            Harness harness = BuildHarness();
            _ = await SeedEpisodesAsync(harness.Episodes, SeriesId, 1);
            SeriesCardViewModel card = harness.CreateCard(SeriesId, "TKKG");
            harness.SeriesVM.SetAllSeries([card]);

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.SelectSeriesAsync(card);
            sut.Dispose();

            Assert.False(harness.EpisodesVM.IsLoadingEpisodes);
        }

        [Fact]
        public async Task SelectSeriesAsync_ForAnotherSeries_ReplacesThePreviousEpisodeList()
        {
            Harness harness = BuildHarness();
            _ = await SeedEpisodesAsync(harness.Episodes, SeriesId, 3);
            _ = await SeedEpisodesAsync(harness.Episodes, OtherSeriesId, 1);
            SeriesCardViewModel first = harness.CreateCard(SeriesId, "TKKG");
            SeriesCardViewModel second = harness.CreateCard(OtherSeriesId, "Die drei Fragezeichen");
            harness.SeriesVM.SetAllSeries([first, second]);

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.SelectSeriesAsync(first);
            await sut.SelectSeriesAsync(second);
            sut.Dispose();

            _ = Assert.Single(harness.EpisodesVM.Episodes);
            Assert.False(first.IsSelectedInAccordion);
            Assert.True(second.IsSelectedInAccordion);
        }

        [Fact]
        public async Task SelectSeriesAsync_BeforeReadingEpisodes_CopiesCoversOfTheSelectedSeries()
        {
            Harness harness = BuildHarness();
            _ = await SeedEpisodesAsync(harness.Episodes, SeriesId, 1);
            SeriesCardViewModel card = harness.CreateCard(SeriesId, "TKKG");
            harness.SeriesVM.SetAllSeries([card]);

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.SelectSeriesAsync(card);
            sut.Dispose();

            Assert.Equal(1, harness.CoverCopy.CallCount);
            Assert.Equal(SeriesId, harness.CoverCopy.LastTargetSeriesId);
        }

        [Fact]
        public async Task SelectSeriesAsync_CarriesProviderAlbumIdsToTheCard()
        {
            Harness harness = BuildHarness();
            Episode episode = new()
            {
                SeriesId = SeriesId,
                Title = "Folge 1",
                EpisodeNumber = 1,
                SpotifyAlbumId = "sp_album_1",
                AppleMusicAlbumId = "am_album_1",
                ProviderUrl = "https://example.invalid/album/1",
            };
            await harness.Episodes.AddAsync(episode, TestContext.Current.CancellationToken);
            SeriesCardViewModel card = harness.CreateCard(SeriesId, "TKKG");
            harness.SeriesVM.SetAllSeries([card]);

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.SelectSeriesAsync(card);
            sut.Dispose();

            OnlineEpisodeCardViewModel shown = harness.EpisodesVM.Episodes[0];
            Assert.Equal("sp_album_1", shown.SpotifyAlbumId);
            Assert.Equal("am_album_1", shown.AppleMusicAlbumId);
        }

        [Fact]
        public async Task SelectSeriesAsync_WithoutCard_ThrowsArgumentNullException()
        {
            Harness harness = BuildHarness();
            OnlineEpisodePipeline sut = harness.CreatePipeline();

            _ = await Assert.ThrowsAsync<ArgumentNullException>(
                () => sut.SelectSeriesAsync(null!));

            sut.Dispose();
        }

        // ── Chargen beim Abruf der Bilddaten ─────────────────────────────────────

        [Fact]
        public async Task SelectSeriesAsync_WithMoreEpisodesThanTheFirstScreen_RequestsOnlyTheVisibleCoversUpFront()
        {
            Harness harness = BuildHarness();
            _ = await SeedEpisodesAsync(harness.Episodes, SeriesId, FirstVisibleCovers + 6);
            SeriesCardViewModel card = harness.CreateCard(SeriesId, "TKKG");
            harness.SeriesVM.SetAllSeries([card]);

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.SelectSeriesAsync(card);

            // Das Nachtragen der übrigen Cover läuft nebenher und beginnt mit einer Pause;
            // der Abbruch beendet es, bevor es die aufgezeichneten Anfragen verändern kann.
            sut.Dispose();

            Assert.Equal(FirstVisibleCovers, harness.CoverImages.BatchRequests[0].Count);
        }

        [Fact]
        public async Task SelectSeriesAsync_WithFewEpisodes_RequestsAllCoversInOneBatch()
        {
            Harness harness = BuildHarness();
            _ = await SeedEpisodesAsync(harness.Episodes, SeriesId, 5);
            SeriesCardViewModel card = harness.CreateCard(SeriesId, "TKKG");
            harness.SeriesVM.SetAllSeries([card]);

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.SelectSeriesAsync(card);
            sut.Dispose();

            _ = Assert.Single(harness.CoverImages.BatchRequests);
            Assert.Equal(5, harness.CoverImages.BatchRequests[0].Count);
        }

        [Fact]
        public async Task SelectSeriesAsync_WithoutEpisodes_AsksForNoCoverAtAll()
        {
            Harness harness = BuildHarness();
            SeriesCardViewModel card = harness.CreateCard(SeriesId, "TKKG");
            harness.SeriesVM.SetAllSeries([card]);

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.SelectSeriesAsync(card);
            sut.Dispose();

            Assert.Empty(harness.CoverImages.BatchRequests);
        }

        [Fact]
        public async Task SelectSeriesAsync_WithMoreEpisodesThanTheFirstScreen_FetchesTheRestAfterwards()
        {
            Harness harness = BuildHarness();
            _ = await SeedEpisodesAsync(harness.Episodes, SeriesId, FirstVisibleCovers + 6);
            SeriesCardViewModel card = harness.CreateCard(SeriesId, "TKKG");
            harness.SeriesVM.SetAllSeries([card]);
            harness.CoverImages.SignalAfterBatches(2);

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.SelectSeriesAsync(card);

            // Die Liste steht sofort; die übrigen Bilder kommen nach. Bleibt das aus, endet
            // eine lange Serie ab der fünfundzwanzigsten Kachel in Platzhaltern.
            await harness.CoverImages.BatchesReached.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            sut.Dispose();

            Assert.Equal(6, harness.CoverImages.BatchRequests[1].Count);
        }

        [Fact]
        public async Task SelectSeriesAsync_WithCoverCache_AsksAgainForTheStillMissingCovers()
        {
            Harness harness = BuildHarness(withCoverCache: true);
            _ = await SeedEpisodesAsync(harness.Episodes, SeriesId, 3);
            SeriesCardViewModel card = harness.CreateCard(SeriesId, "TKKG");
            harness.SeriesVM.SetAllSeries([card]);
            // Drei Abfragen: die erste Charge, die Bestandsaufnahme des Zwischenspeichers
            // und der erneute Blick der Pipeline danach.
            harness.CoverImages.SignalAfterBatches(3);

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.SelectSeriesAsync(card);

            // Nach dem Nachladen im Hintergrund liest die Pipeline erneut, was inzwischen
            // in der Ablage liegt — sonst blieben die frisch geholten Bilder unsichtbar,
            // bis der Anwender die Serie erneut öffnet.
            await harness.CoverImages.BatchesReached.WaitAsync(
                TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            sut.Dispose();

            Assert.True(harness.CoverImages.BatchRequests.Count >= 3);
        }

        [Fact]
        public async Task SelectSeriesAsync_WithCoverCache_KeepsLookingWhileTheDownloadRuns()
        {
            Harness harness = BuildHarness(withCoverCache: true);
            _ = await SeedEpisodesAsync(harness.Episodes, SeriesId, 3);
            SeriesCardViewModel card = harness.CreateCard(SeriesId, "TKKG");
            harness.SeriesVM.SetAllSeries([card]);
            // Eine Abfrage mehr als im Test darüber: Sie kann nur aus dem Blick während
            // des laufenden Downloads stammen, nicht aus dem einmaligen danach.
            harness.CoverImages.SignalAfterBatches(4);

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.SelectSeriesAsync(card);

            // Der Zwischenspeicher füllt sich nach und nach. Schaute die Pipeline nur einmal
            // am Ende nach, blieben die Kacheln bis dahin leer — bei einer langen Serie
            // sind das mehrere Minuten Platzhalter.
            await harness.CoverImages.BatchesReached.WaitAsync(
                TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            sut.Dispose();

            Assert.True(harness.CoverImages.BatchRequests.Count >= 4);
        }

        // ── Cover-Suche ──────────────────────────────────────────────────────────

        [Fact]
        public async Task SearchEpisodeCoversAsync_WithResults_MapsEveryResultToAHit()
        {
            FakeCoverSearchService search = new();
            search.SetResults(
            [
                new("https://example.invalid/thumb1.jpg", "https://example.invalid/full1.jpg", "Folge 1", "Cover Art Archive"),
                new("https://example.invalid/thumb2.jpg", "https://example.invalid/full2.jpg", "Folge 2", "iTunes"),
            ]);

            IReadOnlyList<CoverSearchHit> hits = await OnlineEpisodePipeline.SearchEpisodeCoversAsync(
                search, "TKKG Folge", EchoPlay.LocalLibrary.Cover.CoverSearchPage.First,
                TestContext.Current.CancellationToken);

            Assert.Equal(2, hits.Count);
            Assert.Equal("https://example.invalid/full1.jpg", hits[0].FullUrl);
            Assert.Equal("Folge 2", hits[1].ReleaseTitle);
            Assert.Equal("iTunes", hits[1].Source);
        }

        [Fact]
        public async Task SearchEpisodeCoversAsync_PassesQueryAndPageToTheSearchService()
        {
            FakeCoverSearchService search = new();

            _ = await OnlineEpisodePipeline.SearchEpisodeCoversAsync(
                search, "Bibi Blocksberg", EchoPlay.LocalLibrary.Cover.CoverSearchPage.First,
                TestContext.Current.CancellationToken);

            Assert.Equal("Bibi Blocksberg", search.LastSearchTitle);
            Assert.Equal(0, search.LastPage.Index);
        }

        // ── Übernahme eines gewählten Covers ─────────────────────────────────────

        [Fact]
        public async Task ApplySelectedEpisodeCoverAsync_WhenDownloadFails_StoresNothing()
        {
            Harness harness = BuildHarness();
            OnlineEpisodeCardViewModel card = new(
                episodeId: EpisodeId, episodeNumber: 1, title: "Folge 1");
            CoverSearchHit hit = new(
                "https://example.invalid/thumb.jpg", "https://example.invalid/full.jpg",
                "Folge 1", "Cover Art Archive");

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.ApplySelectedEpisodeCoverAsync(card, hit);

            Assert.Equal(1, sut.ApplyEpisodeCoverCallCount);
            Assert.False(await harness.CoverImages.ExistsAsync(
                CoverService.EntityTypeEpisode, EpisodeId, TestContext.Current.CancellationToken));

            sut.Dispose();
        }

        [Fact]
        public async Task ApplySelectedEpisodeCoverAsync_WithDownloadedBytes_StoresThemForTheEpisode()
        {
            Harness harness = BuildHarness();
            byte[] coverBytes = [1, 2, 3, 4];
            harness.Downloader.SetResponse("https://example.invalid/full.jpg", coverBytes);

            OnlineEpisodeCardViewModel card = new(
                episodeId: EpisodeId, episodeNumber: 1, title: "Folge 1");
            CoverSearchHit hit = new(
                "https://example.invalid/thumb.jpg", "https://example.invalid/full.jpg",
                "Folge 1", "Cover Art Archive");

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.ApplySelectedEpisodeCoverAsync(card, hit);

            CoverImage? stored = await harness.CoverImages.GetByEntityAsync(
                CoverService.EntityTypeEpisode, EpisodeId, TestContext.Current.CancellationToken);
            Assert.NotNull(stored);
            Assert.Equal(coverBytes, stored.ImageData);

            sut.Dispose();
        }

        [Fact]
        public async Task ApplySelectedEpisodeCoverAsync_DownloadsTheFullSizeImage_NotTheThumbnail()
        {
            Harness harness = BuildHarness();
            OnlineEpisodeCardViewModel card = new(
                episodeId: EpisodeId, episodeNumber: 1, title: "Folge 1");
            CoverSearchHit hit = new(
                "https://example.invalid/thumb.jpg", "https://example.invalid/full.jpg",
                "Folge 1", "Cover Art Archive");

            OnlineEpisodePipeline sut = harness.CreatePipeline();
            await sut.ApplySelectedEpisodeCoverAsync(card, hit);

            Assert.Equal(["https://example.invalid/full.jpg"], harness.Downloader.RequestedUrls);

            sut.Dispose();
        }
    }
}

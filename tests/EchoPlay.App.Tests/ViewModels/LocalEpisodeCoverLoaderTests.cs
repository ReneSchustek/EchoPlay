using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft das Nachladen der Folgen-Cover in Chargen.
    /// </summary>
    /// <remarks>
    /// Geprüft wird der Abbruchweg, nicht der Erfolgsweg: Das fertige Bildobjekt entsteht am
    /// Fenster und lässt sich im Testlauf nicht erzeugen. Der Abbruch ist dafür der Teil, der
    /// im Betrieb zählt — wer eine Serie verlässt, während noch dreißig Kacheln nachladen,
    /// erwartet, dass die Arbeit endet und nicht im Hintergrund weiterläuft.
    /// </remarks>
    public sealed class LocalEpisodeCoverLoaderTests
    {
        [Fact]
        public async Task LoadRest_WithoutQueue_Throws()
        {
            LocalEpisodeCoverLoader loader = BuildLoader(out _);

            _ = await Assert.ThrowsAsync<ArgumentNullException>(
                () => loader.LoadRestAsync(null!, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task LoadRest_WithEmptyQueue_ReadsNothing()
        {
            LocalEpisodeCoverLoader loader = BuildLoader(out ConfigurableLocalCoverLoader coverLoader);

            await loader.LoadRestAsync([], TestContext.Current.CancellationToken);

            Assert.Empty(coverLoader.Calls);
        }

        [Fact]
        public async Task LoadRest_WhenCancelledUpfront_ReadsNothing()
        {
            LocalEpisodeCoverLoader loader = BuildLoader(out ConfigurableLocalCoverLoader coverLoader);

            List<(LocalEpisodeCardViewModel Card, Episode Episode)> queue = [];
            for (int i = 0; i < 5; i++)
            {
                Episode episode = new() { Title = $"Folge {i}", LocalFolderPath = @"D:\Media" };
                queue.Add((BuildCard(episode), episode));
            }

            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();

            await loader.LoadRestAsync(queue, cancelled.Token);

            // Der Abbruch kommt vom Serienwechsel. Greift er erst nach der ersten Charge,
            // liest die Anwendung Bilder für eine Ansicht, die niemand mehr sieht.
            Assert.Empty(coverLoader.Calls);
        }

        // ── Reihenfolge der drei Stufen ──────────────────────────────────────

        [Fact]
        public async Task LoadSingle_WithCoverInDatabase_DoesNotTouchTheFolder()
        {
            Episode episode = new() { Title = "Folge 1", LocalFolderPath = FolderWithoutCover };
            ConfigurableLocalCoverLoader coverLoader = new();
            EchoPlay.App.Services.CoverService coverService = BuildCoverService();
            await coverService.SetEpisodeCoverAsync(
                episode.Id, [7, 7, 7], cancellationToken: TestContext.Current.CancellationToken);
            LocalEpisodeCoverLoader loader = BuildLoader(coverLoader, coverService);

            await loader.LoadSingleAsync(BuildCard(episode), episode);

            // Steht das Cover schon in der Ablage, ist jeder Griff auf die Platte verschenkte
            // Zeit — bei vierhundert Kacheln vierhundertmal.
            Assert.Empty(coverLoader.Calls);
        }

        [Fact]
        public async Task LoadSingle_WithoutCoverInDatabase_FallsBackToTheFolder()
        {
            Episode episode = new() { Title = "Folge 1", LocalFolderPath = FolderWithoutCover };
            ConfigurableLocalCoverLoader coverLoader = new();
            LocalEpisodeCoverLoader loader = BuildLoader(coverLoader, BuildCoverService());

            await loader.LoadSingleAsync(BuildCard(episode), episode);

            _ = Assert.Single(coverLoader.Calls);
            Assert.Equal(FolderWithoutCover, coverLoader.Calls[0].FolderPath);
        }

        [Fact]
        public async Task LoadSingle_WithoutFolderPath_AsksTheLoaderWithoutTrack()
        {
            Episode episode = new() { Title = "Folge ohne Ordner", LocalFolderPath = null };
            ConfigurableLocalCoverLoader coverLoader = new();
            LocalEpisodeCoverLoader loader = BuildLoader(coverLoader, coverService: null);

            await loader.LoadSingleAsync(BuildCard(episode), episode);

            // Ohne Ordner gibt es weder ein cover.jpg noch eine erste Spur; die Kennzeichnung
            // wird gar nicht erst nachgeschlagen.
            _ = Assert.Single(coverLoader.Calls);
            Assert.Null(coverLoader.Calls[0].FolderPath);
            Assert.Null(coverLoader.Calls[0].FirstTrackPath);
        }

        [Fact]
        public async Task LoadSingle_WithSeveralTracks_HandsTheLowestTrackNumberToTheLoader()
        {
            Episode episode = new() { Title = "Folge 1", LocalFolderPath = FolderWithoutCover };
            ConfigurableLocalCoverLoader coverLoader = new();
            FakeLocalTrackDataService trackService = new(new Dictionary<Guid, IReadOnlyList<LocalTrack>>
            {
                [episode.Id] =
                [
                    new LocalTrack { EpisodeId = episode.Id, TrackNumber = 3, FilePath = FolderWithoutCover + @"\Spur03.mp3" },
                    new LocalTrack { EpisodeId = episode.Id, TrackNumber = 1, FilePath = FolderWithoutCover + @"\Spur01.mp3" },
                ],
            });
            LocalEpisodeCoverLoader loader = BuildLoader(coverLoader, coverService: null, trackService: trackService);

            await loader.LoadSingleAsync(BuildCard(episode), episode);

            // Die Kennzeichnung der ersten Spur trägt das Cover — nicht die, die zufällig
            // zuerst in der Ablage steht.
            Assert.Equal(FolderWithoutCover + @"\Spur01.mp3", coverLoader.Calls[0].FirstTrackPath);
        }

        [Fact]
        public async Task LoadSingle_WithCoverBytesFromTheFolder_KeepsThePlaceholder()
        {
            // Die Bilddaten kommen an, das Bildobjekt entsteht am Fenster — im Testlauf
            // bleibt die Kachel deshalb bei ihrem Platzhalter. Geprüft ist, dass der Weg
            // bis dahin durchläuft, statt an den Bilddaten zu scheitern.
            ConcurrencyCountingCoverLoader coverLoader = new();
            Episode episode = new() { Title = "Folge 1", LocalFolderPath = FolderWithoutCover };
            LocalEpisodeCardViewModel card = BuildCard(episode);
            LocalEpisodeCoverLoader loader = BuildLoader(coverLoader, coverService: null);

            await loader.LoadSingleAsync(card, episode);

            Assert.Equal(1, coverLoader.CallCount);
            Assert.Null(card.CoverImage);
        }

        // ── Chargen ──────────────────────────────────────────────────────

        [Fact]
        public async Task LoadRest_WithOneBatch_ReadsEveryEpisodeOnce()
        {
            LocalEpisodeCoverLoader loader = BuildLoader(out ConfigurableLocalCoverLoader coverLoader);

            await loader.LoadRestAsync(BuildQueue(5), TestContext.Current.CancellationToken);

            Assert.Equal(5, coverLoader.Calls.Count);
        }

        [Fact]
        public async Task LoadRest_WithMoreEpisodesThanOneBatch_ReadsThemAllAcrossBatches()
        {
            LocalEpisodeCoverLoader loader = BuildLoader(out ConfigurableLocalCoverLoader coverLoader);

            // 70 Folgen sind mehr als eine Charge fasst. Bliebe der Rest liegen, füllte sich
            // eine lange Serie nur bis zur sechzigsten Kachel mit Bildern.
            await loader.LoadRestAsync(BuildQueue(70), TestContext.Current.CancellationToken);

            Assert.Equal(70, coverLoader.Calls.Count);
        }

        [Fact]
        public async Task LoadRest_WithManyEpisodes_NeverRunsMoreThanEightLoadsAtOnce()
        {
            ConcurrencyCountingCoverLoader coverLoader = new();
            LocalEpisodeCoverLoader loader = BuildLoader(coverLoader, coverService: null);

            await loader.LoadRestAsync(BuildQueue(120), TestContext.Current.CancellationToken);

            // Die Obergrenze hält den Oberflächenfaden frei. Ohne sie liefen bei einer
            // langen Serie hundert Dateizugriffe zugleich, und die Liste bliebe stehen.
            Assert.Equal(120, coverLoader.CallCount);
            Assert.True(
                coverLoader.PeakConcurrency <= 8,
                $"Gleichzeitig liefen {coverLoader.PeakConcurrency} Ladevorgänge.");
        }

        [Fact]
        public async Task LoadSingle_WithCoverInTheFolder_ReadsItExactlyOnce()
        {
            ConcurrencyCountingCoverLoader coverLoader = new();
            Episode episode = new() { Title = "Folge 1", LocalFolderPath = FolderWithoutCover };
            LocalEpisodeCoverLoader loader = BuildLoader(coverLoader, coverService: null);

            await loader.LoadSingleAsync(BuildCard(episode), episode);

            Assert.Equal(1, coverLoader.CallCount);
        }

        // ── Aufbau ───────────────────────────────────────────────────────

        /// <summary>Ein Ordner, in dem kein cover.jpg liegt — er existiert bewusst nicht.</summary>
        private const string FolderWithoutCover = @"D:\Media\TKKG\Folge 001";

        private static List<(LocalEpisodeCardViewModel Card, Episode Episode)> BuildQueue(int count)
        {
            List<(LocalEpisodeCardViewModel Card, Episode Episode)> queue = new(count);
            for (int i = 0; i < count; i++)
            {
                Episode episode = new() { Title = $"Folge {i}", LocalFolderPath = FolderWithoutCover };
                queue.Add((BuildCard(episode), episode));
            }
            return queue;
        }

        private static EchoPlay.App.Services.CoverService BuildCoverService()
        {
            // Eine einzige Ablage für alle Bereiche — mit einem neuen Nachbau je Bereich
            // fände das Lesen nie, was das Schreiben zuvor hinterlegt hat.
            FakeCoverImageDataService coverImages = new();
            ServiceCollection services = new();
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImages);
            ServiceProvider provider = services.BuildServiceProvider();
            return new EchoPlay.App.Services.CoverService(provider.GetRequiredService<IServiceScopeFactory>(), new FakeLoggerFactory());
        }

        private static LocalEpisodeCoverLoader BuildLoader(
            ILocalCoverLoader coverLoader,
            EchoPlay.App.Services.CoverService? coverService,
            FakeLocalTrackDataService? trackService = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => trackService ?? new FakeLocalTrackDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            return new LocalEpisodeCoverLoader(
                provider.GetRequiredService<IServiceScopeFactory>(),
                coverLoader,
                coverService,
                logger: null,
                dispatcherQueue: null);
        }

        private static LocalEpisodeCardViewModel BuildCard(Episode episode)
        {
            return new LocalEpisodeCardViewModel(
                episode.Id,
                episodeNumber: null,
                episode.Title,
                localTrackCount: 1,
                episode.LocalFolderPath,
                coverImage: null,
                isCompleted: false,
                isSpecialEpisode: false);
        }

        private static LocalEpisodeCoverLoader BuildLoader(out ConfigurableLocalCoverLoader coverLoader)
        {
            coverLoader = new ConfigurableLocalCoverLoader();

            ServiceCollection services = new();
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            return new LocalEpisodeCoverLoader(
                provider.GetRequiredService<IServiceScopeFactory>(),
                coverLoader,
                coverService: null,
                logger: null,
                dispatcherQueue: null);
        }
    }
}
